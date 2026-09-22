using System.Collections.Concurrent;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using Geumeum.Patch;

namespace Geumeum.Packager;

/// <summary>
/// 로컬 저장소 폴더를 R2와 같은 방식(Range, 강한 ETag)으로 내보낸다. R2 전까지 이 PC를 배포 서버로 쓰고, 런처 자동 검증에도 쓴다
/// Kestrel이라 LAN 주소로 열 때 관리자 권한(urlacl)이 필요 없다
/// </summary>
public sealed class RepoHttpServer : IDisposable
{
    private const int BUFFER_SIZE = 256 * 1024;

    private readonly string          _root;
    private readonly WebApplication  _app;
    private int _abortBudget;

    /// <summary>
    /// prefix 예: http://localhost:8765/ (이 PC만), http://0.0.0.0:8765/ (LAN, 외부)
    /// </summary>
    public RepoHttpServer(string repoRoot, string prefix)
    {
        _root  = Path.GetFullPath(repoRoot);
        Prefix = prefix.EndsWith('/') ? prefix : prefix + "/";

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls(Prefix.TrimEnd('/'));
        builder.Logging.ClearProviders();
        _app = builder.Build();
        _app.Run(HandleAsync);
    }

    public string Prefix { get; }

    /// <summary>
    /// 다음 응답 N개를 본문 도중 이 바이트 수에서 끊는다. 이어받기 검증용
    /// </summary>
    public long AbortAfterBytes { get; set; } = -1;

    public int AbortCount
    {
        get => _abortBudget;
        set => _abortBudget = value;
    }

    /// <summary>
    /// 요청 기록: "GET key 206" 같은 한 줄. 끊은 응답은 상태 코드가 음수다
    /// </summary>
    public ConcurrentQueue<string> Requests { get; } = new();

    public Action<string>? Log { get; set; }

    public void Start()
    {
        _app.StartAsync().GetAwaiter().GetResult();
    }

    private async Task HandleAsync(HttpContext context)
    {
        HttpRequest  request  = context.Request;
        HttpResponse response = context.Response;
        string key = Uri.UnescapeDataString((request.Path.Value ?? "").TrimStart('/'));
        int status = 500;

        try
        {
            if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
            {
                status = response.StatusCode = 405;
                return;
            }

            string? path = TryResolve(key);
            if (path == null)
            {
                status = response.StatusCode = 404;
                return;
            }

            var  info   = new FileInfo(path);
            long length = info.Length;
            string etag = $"\"{length:x}-{info.LastWriteTimeUtc.Ticks:x}\"";

            response.Headers.AcceptRanges = "bytes";
            response.Headers.ETag         = etag;
            response.Headers.CacheControl = RepoLayout.CacheControlFor(key);
            response.ContentType          = key.EndsWith(".json", StringComparison.Ordinal) ? "application/json" : "application/octet-stream";

            long start = 0;
            long end   = length - 1;
            status = 200;

            string? range   = request.Headers.Range;
            string? ifRange = request.Headers.IfRange;
            if (range != null && (ifRange == null || ifRange == etag) && TryParseRange(range, out long from, out long? to))
            {
                if (from >= length)
                {
                    status = response.StatusCode = 416;
                    response.Headers.ContentRange = $"bytes */{length}";
                    return;
                }

                start  = from;
                end    = Math.Min(to ?? length - 1, length - 1);
                status = 206;
                response.Headers.ContentRange = $"bytes {start}-{end}/{length}";
            }

            response.StatusCode    = status;
            response.ContentLength = end - start + 1;
            if (HttpMethods.IsHead(request.Method))
            {
                return;
            }

            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_SIZE, true);
            file.Seek(start, SeekOrigin.Begin);

            long remaining = end - start + 1;
            long cutAt     = key.StartsWith("objects/", StringComparison.Ordinal) && AbortAfterBytes >= 0
                             && remaining > AbortAfterBytes && Interlocked.Decrement(ref _abortBudget) >= 0
                ? AbortAfterBytes
                : long.MaxValue;
            byte[] buffer = new byte[BUFFER_SIZE];
            long sent = 0;

            while (remaining > 0)
            {
                int read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, Math.Min(remaining, cutAt - sent))), context.RequestAborted);
                if (read == 0)
                {
                    break;
                }
                await response.Body.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted);
                remaining -= read;
                sent      += read;
                if (sent >= cutAt)
                {
                    status = -status;
                    await response.Body.FlushAsync();
                    context.Abort();
                    return;
                }
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException)
        {
            // 받는 쪽이 끊었다. 런처는 이어받기로 다시 온다
            Log?.Invoke($"응답 중단: {key} ({e.Message})");
        }
        finally
        {
            Requests.Enqueue($"{request.Method} {key} {status}");
            Log?.Invoke($"{DateTime.Now:HH:mm:ss} {context.Connection.RemoteIpAddress} {request.Method} {key} {status}");
        }
    }

    private string? TryResolve(string key)
    {
        if (RelativePath.Validate(key) != null)
        {
            return null;
        }

        string path = Path.GetFullPath(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)));
        return path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(path)
            ? path
            : null;
    }

    private static bool TryParseRange(string header, out long from, out long? to)
    {
        from = 0;
        to   = null;
        if (!header.StartsWith("bytes=", StringComparison.Ordinal) || header.Contains(','))
        {
            return false;
        }

        string[] parts = header[6..].Split('-');
        if (parts.Length != 2 || !long.TryParse(parts[0], out from))
        {
            return false;
        }
        if (parts[1].Length > 0)
        {
            if (!long.TryParse(parts[1], out long parsed))
            {
                return false;
            }
            to = parsed;
        }
        return true;
    }

    public void Dispose()
    {
        _app.StopAsync().GetAwaiter().GetResult();
        ((IDisposable)_app).Dispose();
    }
}
