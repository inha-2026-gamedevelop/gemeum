using System.Net;
using System.Net.Http.Headers;

namespace Geumeum.Patch.Client;

/// <summary>
/// 배포 저장소 읽기 창구. 키는 RepoLayout 규칙을 따른다
/// </summary>
public interface IRemoteRepository : IDisposable
{
    string Description { get; }

    /// <summary>
    /// 작은 메타데이터를 통째로 받는다. 상한을 넘으면 거부한다
    /// </summary>
    Task<byte[]> GetBytesAsync(string key, int maxBytes, CancellationToken ct);

    /// <summary>
    /// partPath에 이어 받는다. 끝나면 partPath 길이가 expectedSize다. 해시 검증은 호출자가 한다
    /// onLength는 지금까지 받은 누적 길이(이어받기 시작점 포함)를 알린다
    /// </summary>
    Task DownloadAsync(string key, long expectedSize, string partPath, Action<long> onLength, CancellationToken ct);
}

/// <summary>
/// R2 공개 URL 같은 HTTPS 정적 저장소. redirect와 전송 압축을 허용하지 않는다
/// </summary>
public sealed class HttpRemoteRepository : IRemoteRepository
{
    private const int BUFFER_SIZE = 256 * 1024;

    private readonly HttpClient _http;
    private readonly Uri        _base;
    private readonly TimeSpan   _idleTimeout;

    public HttpRemoteRepository(Uri baseUri, bool allowInsecureHttp = false, TimeSpan? idleTimeout = null, HttpMessageHandler? handler = null)
    {
        if (baseUri.Scheme != Uri.UriSchemeHttps && !(allowInsecureHttp && baseUri.Scheme == Uri.UriSchemeHttp))
        {
            throw new ArgumentException($"배포 주소는 HTTPS여야 한다: {baseUri}");
        }

        _base        = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/");
        _idleTimeout = idleTimeout ?? TimeSpan.FromSeconds(30);
        _http        = new HttpClient(handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect        = false,
            AutomaticDecompression   = DecompressionMethods.None,
            ConnectTimeout           = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GeumeumLauncher/1");
    }

    public string Description => _base.AbsoluteUri;

    public async Task<byte[]> GetBytesAsync(string key, int maxBytes, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_base, key));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

        using HttpResponseMessage response = await SendAsync(request, ct);
        EnsureSuccess(response, key);
        EnsureNoTransform(response, key);

        if (response.Content.Headers.ContentLength > maxBytes)
        {
            throw new RemoteProtocolException($"{key} 크기가 상한 {maxBytes}를 넘는다");
        }

        await using Stream body = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        await CopyWithIdleTimeoutAsync(body, buffer, maxBytes, _ => { }, ct);
        return buffer.ToArray();
    }

    public async Task DownloadAsync(string key, long expectedSize, string partPath, Action<long> onLength, CancellationToken ct)
    {
        string metaPath = partPath + ".json";
        PartMeta? meta  = PartMeta.TryRead(metaPath);
        long existing   = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

        // 기록이 없거나 다른 크기를 가리키면 이어 쓸 근거가 없다
        if (existing > 0 && (meta == null || meta.Size != expectedSize || existing > expectedSize))
        {
            File.Delete(partPath);
            existing = 0;
        }
        if (existing == expectedSize)
        {
            onLength(existing);
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_base, key));
        if (existing > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existing, null);
            // 강한 ETag가 있을 때만 If-Range를 건다. 약한 ETag로 이어 쓰면 다른 내용이 섞일 수 있다
            if (meta?.ETag is { Length: > 0 } etag && !etag.StartsWith("W/", StringComparison.Ordinal))
            {
                request.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue(etag));
            }
        }

        using HttpResponseMessage response = await SendAsync(request, ct);
        EnsureNoTransform(response, key);

        long start;
        switch (response.StatusCode)
        {
            case HttpStatusCode.PartialContent:
                ContentRangeHeaderValue? range = response.Content.Headers.ContentRange;
                if (range == null || range.From != existing || range.To != expectedSize - 1 || range.Length != expectedSize)
                {
                    throw new RemoteProtocolException($"{key} Content-Range가 요청과 다르다: {range}");
                }
                start = existing;
                break;

            case HttpStatusCode.OK:
                if (response.Content.Headers.ContentLength is long length && length != expectedSize)
                {
                    throw new RemoteProtocolException($"{key} 크기가 manifest와 다르다: 기대 {expectedSize}, 응답 {length}");
                }
                start = 0;
                break;

            case HttpStatusCode.RequestedRangeNotSatisfiable:
                // 로컬 조각이 이미 끝까지 왔거나 서버 객체가 바뀌었다. 처음부터 다시 받게 한다
                File.Delete(partPath);
                throw new RemoteTransientException($"{key} 이어받기 범위를 서버가 거부했다(416), 처음부터 다시 받는다");

            default:
                EnsureSuccess(response, key);
                throw new RemoteProtocolException($"{key} 예상하지 못한 응답 {(int)response.StatusCode}");
        }

        PartMeta.Write(metaPath, new PartMeta { Size = expectedSize, ETag = response.Headers.ETag?.ToString() ?? "" });

        await using Stream body = await response.Content.ReadAsStreamAsync(ct);
        await using (var file = new FileStream(partPath, start == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.None, BUFFER_SIZE, true))
        {
            onLength(start);
            await CopyWithIdleTimeoutAsync(body, file, expectedSize - start, written => onLength(start + written), ct);
            await file.FlushAsync(ct);
        }

        long finalLength = new FileInfo(partPath).Length;
        if (finalLength != expectedSize)
        {
            throw new RemoteTransientException($"{key} 응답이 중간에 끊겼다: {finalLength}/{expectedSize}");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        headerTimeout.CancelAfter(_idleTimeout);
        try
        {
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerTimeout.Token);
        }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new RemoteTransientException($"{request.RequestUri} 응답이 {_idleTimeout.TotalSeconds:0}초 동안 없다", e);
        }
        catch (HttpRequestException e)
        {
            throw new RemoteTransientException($"{request.RequestUri} 연결 실패: {e.Message}", e);
        }
    }

    // 전체 시간 제한 대신 무응답 구간 제한을 둔다. 큰 파일이 느린 회선에서 잘리지 않게 한다
    private async Task CopyWithIdleTimeoutAsync(Stream source, Stream destination, long maxBytes, Action<long> onWritten, CancellationToken ct)
    {
        byte[] buffer = new byte[BUFFER_SIZE];
        long total = 0;
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);

        while (true)
        {
            idle.CancelAfter(_idleTimeout);
            int read;
            try
            {
                read = await source.ReadAsync(buffer, idle.Token);
            }
            catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
            {
                throw new RemoteTransientException($"다운로드가 {_idleTimeout.TotalSeconds:0}초 동안 멈췄다", e);
            }
            catch (IOException e)
            {
                throw new RemoteTransientException($"다운로드 연결이 끊겼다: {e.Message}", e);
            }

            if (read == 0)
            {
                return;
            }

            total += read;
            if (total > maxBytes)
            {
                throw new RemoteProtocolException($"응답이 예상 크기 {maxBytes}보다 길다");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            onWritten(total);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string key)
    {
        int status = (int)response.StatusCode;
        if (status == 404)
        {
            throw new RemoteNotFoundException(key);
        }
        if (status >= 300 && status < 400)
        {
            throw new RemoteProtocolException($"{key} redirect({status})는 허용하지 않는다");
        }
        if (status >= 500 || status == 408 || status == 429)
        {
            throw new RemoteTransientException($"{key} 서버 오류 {status}");
        }
        if (status < 200 || status >= 300)
        {
            throw new RemoteProtocolException($"{key} 요청 실패 {status}");
        }
    }

    private static void EnsureNoTransform(HttpResponseMessage response, string key)
    {
        if (response.Content.Headers.ContentEncoding.Count > 0)
        {
            throw new RemoteProtocolException($"{key} 응답이 변형 압축됐다({string.Join(",", response.Content.Headers.ContentEncoding)})");
        }
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    private sealed class PartMeta
    {
        public long   Size { get; set; }
        public string ETag { get; set; } = "";

        public static PartMeta? TryRead(string path)
        {
            try
            {
                return StateFile.TryRead<PartMeta>(path);
            }
            catch (PatchFormatException)
            {
                return null;
            }
        }

        public static void Write(string path, PartMeta meta) => StateFile.Write(path, meta);
    }
}

/// <summary>
/// 로컬, 공유 폴더를 저장소로 쓴다. 자동 검증과 개발용 확인에 쓴다
/// </summary>
public sealed class FolderRemoteRepository : IRemoteRepository
{
    private readonly string _root;

    public FolderRemoteRepository(string root)
    {
        _root = Path.GetFullPath(root);
    }

    public string Description => _root;

    public async Task<byte[]> GetBytesAsync(string key, int maxBytes, CancellationToken ct)
    {
        string path = Resolve(key);
        long length = new FileInfo(path).Length;
        if (length > maxBytes)
        {
            throw new RemoteProtocolException($"{key} 크기가 상한 {maxBytes}를 넘는다");
        }
        return await File.ReadAllBytesAsync(path, ct);
    }

    public async Task DownloadAsync(string key, long expectedSize, string partPath, Action<long> onLength, CancellationToken ct)
    {
        string source = Resolve(key);
        long existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
        if (existing > expectedSize)
        {
            File.Delete(partPath);
            existing = 0;
        }

        await using var input  = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true);
        await using var output = new FileStream(partPath, existing == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.None, 1 << 20, true);
        input.Seek(existing, SeekOrigin.Begin);
        onLength(existing);

        byte[] buffer = new byte[1 << 20];
        long total = existing;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            total += read;
            onLength(total);
        }
    }

    private string Resolve(string key)
    {
        string path = PathGuard.Resolve(_root, key);
        if (!File.Exists(path))
        {
            throw new RemoteNotFoundException(key);
        }
        return path;
    }

    public void Dispose() { }
}
