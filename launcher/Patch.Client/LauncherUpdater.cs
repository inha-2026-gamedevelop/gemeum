namespace Geumeum.Patch.Client;

/// <summary>
/// 런처 자기 업데이트. 서명된 latest.json이 더 높은 버전을 가리킬 때만 받는다
/// 실행 중인 exe는 덮어쓸 수 없지만 이름은 바꿀 수 있다 - 현재 exe를 .old로 비키고 새 exe를 그 자리에 옮긴 뒤 재시작한다
/// </summary>
public sealed class LauncherUpdater
{
    public const int MAX_LATEST_BYTES = 16 * 1024;

    private const string NEW_SUFFIX = ".new";
    private const string OLD_SUFFIX = ".old";

    private readonly IRemoteRepository                   _remote;
    private readonly IReadOnlyDictionary<string, string> _trustedKeys;
    private readonly Version                             _current;
    private readonly PatchLog                            _log;

    public LauncherUpdater(IRemoteRepository remote, IReadOnlyDictionary<string, string> trustedKeys, Version current, PatchLog log)
    {
        _remote      = remote;
        _trustedKeys = trustedKeys;
        _current     = LauncherRelease.Normalize(current);
        _log         = log;
    }

    /// <summary>
    /// 더 높은 버전이 게시돼 있으면 그 정보를, 아니면 null. 게시 정보가 아예 없으면(첫 도입 전) null
    /// </summary>
    public async Task<LauncherRelease?> CheckAsync(CancellationToken ct)
    {
        byte[] bytes;
        try
        {
            bytes = await _remote.GetBytesAsync(RepoLayout.LAUNCHER_LATEST_KEY, MAX_LATEST_BYTES, ct);
        }
        catch (RemoteNotFoundException)
        {
            return null;
        }

        LauncherRelease release = LauncherRelease.Parse(SignedEnvelope.Parse(bytes).VerifyPayload(_trustedKeys));
        if (release.ParsedVersion <= _current)
        {
            return null;
        }

        _log.Info($"새 런처 발견: {_current} → {release.Version}");
        return release;
    }

    /// <summary>
    /// exe 옆에 .new로 받고 크기, 해시를 확인한다. 같은 폴더라 교체가 이름 바꾸기로 끝난다
    /// </summary>
    public async Task<string> DownloadAsync(LauncherRelease release, string exePath, Action<long> onLength, CancellationToken ct)
    {
        // 다른 버전의 조각이 남아 있을 수 있다. 런처 exe는 작아 이어받기보다 새로 받는 편이 안전하다
        string newPath = exePath + NEW_SUFFIX;
        File.Delete(newPath);
        File.Delete(newPath + ".json");
        await _remote.DownloadAsync(RepoLayout.ObjectKey(release.Sha256), release.Size, newPath, onLength, ct);
        File.Delete(newPath + ".json");

        if (new FileInfo(newPath).Length != release.Size || Sha256Hex.OfFile(newPath) != release.Sha256)
        {
            File.Delete(newPath);
            throw new PatchTrustException($"받은 런처 {release.Version}의 해시가 서명된 값과 다르다");
        }
        return newPath;
    }

    /// <summary>
    /// 현재 exe를 비키고 새 exe를 그 자리에 둔다. 중간에 실패하면 원래 exe를 되돌려 놓는다
    /// </summary>
    public static void Swap(string exePath, string newPath)
    {
        string oldPath = exePath + OLD_SUFFIX;
        if (File.Exists(oldPath))
        {
            // 직전 업데이트의 .old가 아직 잡혀 있으면 다른 이름으로 비킨다
            try
            {
                File.Delete(oldPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                oldPath = $"{exePath}{OLD_SUFFIX}-{DateTime.UtcNow.Ticks}";
            }
        }

        File.Move(exePath, oldPath);
        try
        {
            File.Move(newPath, exePath);
        }
        catch
        {
            File.Move(oldPath, exePath);
            throw;
        }
    }

    /// <summary>
    /// 새 런처가 뜬 뒤 이전 exe와 남은 조각을 치운다. 이전 프로세스가 아직 끝나는 중이면 잠깐 기다린다
    /// </summary>
    public static async Task CleanupAsync(string exePath, PatchLog log)
    {
        string? dir = Path.GetDirectoryName(exePath);
        if (dir == null)
        {
            return;
        }

        string name = Path.GetFileName(exePath);
        string[] leftovers = Directory.GetFiles(dir, name + OLD_SUFFIX + "*")
            .Concat(Directory.GetFiles(dir, name + NEW_SUFFIX + "*"))
            .ToArray();

        foreach (string path in leftovers)
        {
            for (int attempt = 1; attempt <= 10; ++attempt)
            {
                try
                {
                    File.Delete(path);
                    log.Info($"이전 런처 정리: {Path.GetFileName(path)}");
                    break;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    if (attempt == 10)
                    {
                        log.Warn($"이전 런처를 지우지 못했다, 다음 실행 때 다시 지운다: {path} ({e.Message})");
                        break;
                    }
                    await Task.Delay(500);
                }
            }
        }
    }
}
