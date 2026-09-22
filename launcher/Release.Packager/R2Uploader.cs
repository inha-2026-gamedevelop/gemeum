using System.Net;

using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;

using Geumeum.Patch;

namespace Geumeum.Packager;

/// <summary>
/// Cloudflare R2(S3 호환)로 로컬 저장소를 올린다. 객체, manifest를 먼저, 채널 envelope를 마지막에 올린다
/// </summary>
public sealed class R2Uploader : IDisposable
{
    public const string ENV_ACCOUNT_ID = "R2_ACCOUNT_ID";
    public const string ENV_ACCESS_KEY = "R2_ACCESS_KEY_ID";
    public const string ENV_SECRET_KEY = "R2_SECRET_ACCESS_KEY";
    public const string ENV_BUCKET     = "R2_BUCKET";

    private readonly AmazonS3Client  _client;
    private readonly TransferUtility _transfer;
    private readonly string          _bucket;
    private readonly Action<string>  _log;

    private R2Uploader(AmazonS3Client client, string bucket, Action<string> log)
    {
        _client   = client;
        _transfer = new TransferUtility(client);
        _bucket   = bucket;
        _log      = log;
    }

    /// <summary>
    /// 자격 증명은 환경 변수로만 받는다. 저장소, 명령줄, 로그에 남기지 않는다
    /// </summary>
    public static R2Uploader FromEnvironment(Action<string> log)
    {
        string accountId = RequireEnv(ENV_ACCOUNT_ID);
        string accessKey = RequireEnv(ENV_ACCESS_KEY);
        string secretKey = RequireEnv(ENV_SECRET_KEY);
        string bucket    = RequireEnv(ENV_BUCKET);

        var config = new AmazonS3Config
        {
            ServiceURL                 = $"https://{accountId}.r2.cloudflarestorage.com",
            ForcePathStyle             = true,
            AuthenticationRegion       = "auto",
            // R2는 SDK 기본 CRC 체크섬 트레일러를 지원하지 않는다
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };
        return new R2Uploader(new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), config), bucket, log);
    }

    private static string RequireEnv(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"환경 변수 {name}가 필요하다");
        }
        return value;
    }

    /// <summary>
    /// 채널 envelope가 가리키는 배포 전체를 올린다. 원격 채널이 더 앞서 있으면 덮어쓰지 않는다
    /// </summary>
    public async Task UploadChannelAsync(string repoDir, string channel, CancellationToken ct)
    {
        string envelopeKey   = RepoLayout.EnvelopeKey(channel);
        byte[] envelopeBytes = await File.ReadAllBytesAsync(RepoLayout.LocalPath(repoDir, envelopeKey), ct);
        ChannelPayload local = RepoPublisher.OpenUnverified(envelopeBytes);

        byte[]? remoteEnvelope = await TryGetAsync(envelopeKey, ct);
        if (remoteEnvelope != null)
        {
            ChannelPayload remote = RepoPublisher.OpenUnverified(remoteEnvelope);
            if (remote.Sequence >= local.Sequence && !remoteEnvelope.AsSpan().SequenceEqual(envelopeBytes))
            {
                throw new PatchFormatException(
                    $"R2의 {channel} 채널이 로컬보다 앞서 있다(원격 seq {remote.Sequence} → {remote.ReleaseId}, 로컬 seq {local.Sequence}). " +
                    "다른 사람이 게시했을 수 있다. 원격 sequence보다 큰 값으로 다시 publish-channel 한다.");
            }
        }

        string manifestKey   = RepoLayout.ManifestKey(local.ReleaseId);
        byte[] manifestBytes = await File.ReadAllBytesAsync(RepoLayout.LocalPath(repoDir, manifestKey), ct);
        ReleaseManifest manifest = local.OpenManifest(manifestBytes);

        List<ManifestFile> distinct = manifest.Files.GroupBy(f => f.Sha256).Select(g => g.First()).ToList();
        int uploaded = 0, skipped = 0;
        long uploadedBytes = 0;

        await Parallel.ForEachAsync(distinct, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (file, token) =>
        {
            string key = RepoLayout.ObjectKey(file.Sha256);
            long? remoteSize = await TryGetSizeAsync(key, token);
            if (remoteSize == file.Size)
            {
                Interlocked.Increment(ref skipped);
                return;
            }

            await PutFileAsync(RepoLayout.LocalPath(repoDir, key), key, "application/octet-stream", token);
            Interlocked.Increment(ref uploaded);
            Interlocked.Add(ref uploadedBytes, file.Size);
            _log($"  객체 {file.Sha256[..12]}… {file.Size / 1048576.0:F1}MB ({file.Path})");
        });
        _log($"객체 업로드 {uploaded}개 {uploadedBytes / 1048576.0:F1}MB, 이미 있음 {skipped}개");

        await PutFileAsync(RepoLayout.LocalPath(repoDir, manifestKey), manifestKey, "application/json", ct);

        string notesPath = RepoLayout.LocalPath(repoDir, RepoLayout.NotesKey(local.ReleaseId));
        if (File.Exists(notesPath))
        {
            await PutFileAsync(notesPath, RepoLayout.NotesKey(local.ReleaseId), "text/plain; charset=utf-8", ct);
        }

        // 모든 객체가 올라간 뒤에만 채널을 바꾼다. 중간에 멈춰도 런처는 이전 배포를 계속 본다
        await PutFileAsync(RepoLayout.LocalPath(repoDir, envelopeKey), envelopeKey, "application/json", ct);
        _log($"{channel} 채널 게시: seq {local.Sequence} → {local.ReleaseId}");
    }

    /// <summary>
    /// 런처 객체와 처음 받는 사람용 exe를 먼저 올리고, 자동 업데이트가 읽는 latest.json을 마지막에 올린다
    /// </summary>
    public async Task UploadLauncherAsync(string repoDir, CancellationToken ct)
    {
        string latestKey = RepoLayout.LAUNCHER_LATEST_KEY;
        byte[] latestBytes = await File.ReadAllBytesAsync(RepoLayout.LocalPath(repoDir, latestKey), ct);
        LauncherRelease release = LauncherRelease.Parse(Convert.FromBase64String(SignedEnvelope.Parse(latestBytes).PayloadBase64));

        string objectKey = RepoLayout.ObjectKey(release.Sha256);
        if (await TryGetSizeAsync(objectKey, ct) != release.Size)
        {
            await PutFileAsync(RepoLayout.LocalPath(repoDir, objectKey), objectKey, "application/octet-stream", ct);
        }
        await PutFileAsync(RepoLayout.LocalPath(repoDir, RepoLayout.LAUNCHER_KEY), RepoLayout.LAUNCHER_KEY, "application/vnd.microsoft.portable-executable", ct);
        await PutFileAsync(RepoLayout.LocalPath(repoDir, latestKey), latestKey, "application/json", ct);
        _log($"런처 {release.Version} 업로드 ({release.Size / 1048576.0:F1}MB)");
    }

    private async Task PutFileAsync(string path, string key, string contentType, CancellationToken ct)
    {
        var request = new TransferUtilityUploadRequest
        {
            BucketName            = _bucket,
            Key                   = key,
            FilePath              = path,
            ContentType           = contentType,
            DisablePayloadSigning = true,
        };
        request.Headers.CacheControl = RepoLayout.CacheControlFor(key);
        await _transfer.UploadAsync(request, ct);
    }

    private async Task<long?> TryGetSizeAsync(string key, CancellationToken ct)
    {
        try
        {
            GetObjectMetadataResponse meta = await _client.GetObjectMetadataAsync(_bucket, key, ct);
            return meta.ContentLength;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<byte[]?> TryGetAsync(string key, CancellationToken ct)
    {
        try
        {
            using GetObjectResponse response = await _client.GetObjectAsync(_bucket, key, ct);
            using var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, ct);
            return buffer.ToArray();
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _transfer.Dispose();
        _client.Dispose();
    }
}
