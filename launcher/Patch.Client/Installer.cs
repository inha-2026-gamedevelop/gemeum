using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Geumeum.Patch.Client;

public enum InstallStage
{
    Preparing,
    Copying,
    Downloading,
    Finalizing,
    Verifying,
    Repairing,
}

/// 화면 진행률. 파일 검사·복사와 다운로드를 따로 보여 준다(설계서 9장).
public readonly record struct InstallProgress(
    InstallStage Stage,
    long         DoneBytes,
    long         TotalBytes,
    long         DownloadedBytes,
    long         DownloadTotalBytes);

public sealed class InstallerOptions
{
    public int      Concurrency       { get; init; } = 3;
    public int      MaxAttempts       { get; init; } = 4;
    public TimeSpan RetryBaseDelay    { get; init; } = TimeSpan.FromSeconds(1);
    public long     SafetyMarginBytes { get; init; } = 512L * 1024 * 1024;
    public bool     CheckVolume       { get; init; } = true;

    /// 자동 검증이 강제 종료를 흉내 내는 지점. 운영에서는 비워 둔다.
    public Action<string>? Checkpoint { get; init; }

    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;
}

public sealed class LocalStatus
{
    public ActivePointer?   Active         { get; init; }
    public ReleaseManifest? ActiveManifest { get; init; }

    /// 설치가 있지만 실행할 수 없는 이유. 없으면 null
    public string? Problem { get; init; }

    public bool IsInstalled => Active != null && Problem == null;
}

public sealed class UpdateCheck
{
    public required ChannelPayload  Payload       { get; init; }
    public required ReleaseManifest Manifest      { get; init; }
    public required byte[]          ManifestBytes { get; init; }
    public required string          Notes         { get; init; }
    public required bool            IsUpToDate    { get; init; }
}

public sealed record RepairResult(int Checked, int Repaired, int RemovedExtras);

/// 설계서 7장 설치 순서와 복구. 기존 버전 폴더를 덮어쓰지 않고 새 폴더를 완성한 뒤 포인터만 바꾼다.
public sealed class Installer
{
    public const int MAX_NOTES_BYTES = 64 * 1024;

    // 설치 폴더 안에 있어도 되는 실행 파일 외 부산물. 나머지 exe·dll은 manifest에 없으면 지운다
    private static readonly string[] ExecutableSuffixes = { ".exe", ".dll" };

    private readonly InstallLayout                       _layout;
    private readonly IRemoteRepository                   _remote;
    private readonly IReadOnlyDictionary<string, string> _trustedKeys;
    private readonly Version                             _launcherVersion;
    private readonly PatchLog                            _log;
    private readonly InstallerOptions                    _options;

    public Installer(InstallLayout layout, IRemoteRepository remote, IReadOnlyDictionary<string, string> trustedKeys,
                     string channel, Version launcherVersion, PatchLog log, InstallerOptions? options = null)
    {
        if (!RepoLayout.IsValidChannel(channel))
        {
            throw new ArgumentException($"알 수 없는 채널 '{channel}'");
        }

        _layout          = layout;
        _remote          = remote;
        _trustedKeys     = trustedKeys;
        Channel          = channel;
        _launcherVersion = launcherVersion;
        _log             = log;
        _options         = options ?? new InstallerOptions();
    }

    public string        Channel { get; }
    public InstallLayout Layout  => _layout;

    /****************************************
    *              Local State
    ****************************************/

    public LocalStatus GetLocalStatus()
    {
        using InstallLock _ = InstallLock.Acquire(_layout);
        Recover();
        return ReadLocalStatus();
    }

    private LocalStatus ReadLocalStatus()
    {
        ActivePointer? active = TryLoadPointer(_layout.ActivePath, out ReleaseManifest? manifest, out string? problem);
        if (active == null && problem == null)
        {
            return new LocalStatus();
        }
        return new LocalStatus { Active = active, ActiveManifest = manifest, Problem = problem };
    }

    /// 포인터와 그 manifest, 버전 폴더의 파일 존재·크기까지 본다. 해시는 비싸서 검사·복구와 업데이트 때만 계산한다.
    private ActivePointer? TryLoadPointer(string path, out ReleaseManifest? manifest, out string? problem, bool checkFiles = true)
    {
        manifest = null;
        problem  = null;
        if (!File.Exists(path))
        {
            return null;
        }

        ActivePointer? pointer;
        try
        {
            pointer = StateFile.TryRead<ActivePointer>(path);
        }
        catch (PatchFormatException e)
        {
            problem = $"설치 기록이 손상됐다: {e.Message}";
            return null;
        }

        if (pointer == null || !ManifestValidator.IsValidReleaseId(pointer.ReleaseId))
        {
            problem = "설치 기록의 버전 ID가 올바르지 않다";
            return null;
        }

        manifest = TryLoadStoredManifest(pointer.ReleaseId, pointer.ManifestSha256, out problem);
        if (manifest == null)
        {
            return pointer;
        }

        if (checkFiles)
        {
            problem = QuickCheckVersion(pointer.ReleaseId, manifest);
        }
        return pointer;
    }

    private ReleaseManifest? TryLoadStoredManifest(string releaseId, string expectedSha, out string? problem)
    {
        problem = null;
        string path = _layout.ManifestPath(releaseId);
        if (!File.Exists(path))
        {
            problem = $"{releaseId} 파일 목록이 없다";
            return null;
        }

        byte[] bytes = File.ReadAllBytes(path);
        if (!string.Equals(Sha256Hex.OfBytes(bytes), expectedSha, StringComparison.Ordinal))
        {
            problem = $"{releaseId} 파일 목록이 손상됐다";
            return null;
        }

        try
        {
            return ReleaseManifest.Parse(bytes);
        }
        catch (PatchFormatException e)
        {
            problem = $"{releaseId} 파일 목록을 읽지 못했다: {e.Message}";
            return null;
        }
    }

    private string? QuickCheckVersion(string releaseId, ReleaseManifest manifest)
    {
        string dir = _layout.VersionDir(releaseId);
        if (!Directory.Exists(dir))
        {
            return $"{releaseId} 설치 폴더가 없다";
        }

        foreach (ManifestFile file in manifest.Files)
        {
            var info = new FileInfo(PathGuard.Resolve(dir, file.Path));
            if (!info.Exists)
            {
                return $"파일이 없다: {file.Path}";
            }
            if (info.Length != file.Size)
            {
                return $"파일 크기가 다르다: {file.Path}";
            }
        }
        return null;
    }

    /// 강제 종료 뒤 상태를 맞춘다. 잠금을 가진 상태에서만 부른다(설계서 7장 마지막 문단).
    private void Recover()
    {
        TransactionRecord? transaction = ReadTransaction();

        if (transaction?.Phase == TransactionRecord.PREPARED)
        {
            ActivePointer? active = TryLoadPointer(_layout.ActivePath, out _, out string? activeProblem);
            if (active != null && activeProblem == null && active.ReleaseId == transaction.TargetReleaseId)
            {
                _log.Info($"복구: {transaction.TargetReleaseId} 전환은 끝나 있었다, 커밋을 마무리한다");
            }
            else
            {
                // 새 버전 폴더는 완성돼 있다. 다음 업데이트가 검증 후 재사용한다
                _log.Warn($"복구: {transaction.TargetReleaseId} 전환 전에 멈췄다, 현재 설치를 유지한다");
            }
            File.Delete(_layout.TransactionPath);
        }

        // preparing은 지우지 않는다. 같은 목표로 다시 설치하면 staging을 이어서 쓴다

        ActivePointer? current = TryLoadPointer(_layout.ActivePath, out _, out string? problem);
        if (current != null && problem == null)
        {
            return;
        }

        ActivePointer? backup = TryLoadPointer(_layout.ActiveBackupPath, out _, out string? backupProblem);
        if (backup != null && backupProblem == null)
        {
            _log.Warn($"복구: 현재 설치 기록이 불량이다({problem ?? "없음"}), 이전 정상 버전 {backup.ReleaseId}로 되돌린다");
            AtomicFile.Write(_layout.ActivePath, File.ReadAllBytes(_layout.ActiveBackupPath));
        }
    }

    private TransactionRecord? ReadTransaction()
    {
        try
        {
            return StateFile.TryRead<TransactionRecord>(_layout.TransactionPath);
        }
        catch (PatchFormatException e)
        {
            _log.Warn($"손상된 설치 기록을 버린다: {e.Message}");
            File.Delete(_layout.TransactionPath);
            return null;
        }
    }

    /****************************************
    *              Update Check
    ****************************************/

    public async Task<UpdateCheck> CheckAsync(CancellationToken ct)
    {
        byte[] envelopeBytes = await _remote.GetBytesAsync(RepoLayout.EnvelopeKey(Channel), ManifestLimits.MAX_ENVELOPE_BYTES, ct);
        SignedEnvelope envelope = SignedEnvelope.Parse(envelopeBytes);

        ChannelMemory? memory = ReadMemory();
        ChannelPayload payload = envelope.VerifyAndOpen(_trustedKeys, _options.Clock(), memory?.LastSequence);

        if (payload.Channel != Channel)
        {
            throw new PatchTrustException($"다른 채널 문서를 받았다: 요청={Channel}, 응답={payload.Channel}");
        }

        string payloadSha = Sha256Hex.OfBytes(Convert.FromBase64String(envelope.PayloadBase64));
        if (memory != null && payload.Sequence == memory.LastSequence && payloadSha != memory.LastPayloadSha256)
        {
            throw new PatchTrustException($"같은 sequence {payload.Sequence}에 다른 내용이 게시됐다");
        }

        var required = Version.Parse(payload.MinLauncherVersion);
        if (required > _launcherVersion)
        {
            throw new LauncherTooOldException(required, _launcherVersion);
        }

        byte[] manifestBytes = await _remote.GetBytesAsync(RepoLayout.ManifestKey(payload.ReleaseId), ManifestLimits.MAX_MANIFEST_BYTES, ct);
        ReleaseManifest manifest = payload.OpenManifest(manifestBytes);

        string notes;
        try
        {
            notes = System.Text.Encoding.UTF8.GetString(await _remote.GetBytesAsync(RepoLayout.NotesKey(payload.ReleaseId), MAX_NOTES_BYTES, ct));
        }
        catch (RemoteNotFoundException)
        {
            notes = "";
        }

        LocalStatus local;
        using (InstallLock.Acquire(_layout))
        {
            StateFile.Write(_layout.ChannelMemoryPath(Channel), new ChannelMemory
            {
                Channel           = Channel,
                LastSequence      = Math.Max(payload.Sequence, memory?.LastSequence ?? 0),
                LastPayloadSha256 = payloadSha,
            });
            Recover();
            local = ReadLocalStatus();
        }

        _log.Info($"업데이트 확인: {Channel} seq {payload.Sequence} → {manifest.ReleaseId} ({manifest.DisplayVersion}), 현재 {local.Active?.ReleaseId ?? "없음"}");

        return new UpdateCheck
        {
            Payload       = payload,
            Manifest      = manifest,
            ManifestBytes = manifestBytes,
            Notes         = notes,
            IsUpToDate    = local.IsInstalled && local.Active!.ReleaseId == manifest.ReleaseId,
        };
    }

    private ChannelMemory? ReadMemory()
    {
        ChannelMemory? memory = StateFile.TryRead<ChannelMemory>(_layout.ChannelMemoryPath(Channel));
        return memory?.Channel == Channel ? memory : null;
    }

    /****************************************
    *                Install
    ****************************************/

    public async Task InstallAsync(UpdateCheck check, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        using InstallLock _ = InstallLock.Acquire(_layout);
        PrepareRoot();
        Recover();

        ReleaseManifest target    = check.Manifest;
        string          releaseId = target.ReleaseId;
        string          manifestSha = check.Payload.ManifestSha256;
        string          processName = Path.GetFileNameWithoutExtension(target.EntryPoint);

        if (GameProcess.IsRunning(_layout, processName))
        {
            throw new GameRunningException();
        }

        LocalStatus local = ReadLocalStatus();
        if (local.IsInstalled && local.Active!.ReleaseId == releaseId)
        {
            _log.Info($"{releaseId}는 이미 설치돼 있다");
            return;
        }

        AtomicFile.Write(_layout.ManifestPath(releaseId), check.ManifestBytes);
        _log.Info($"설치 시작: {local.Active?.ReleaseId ?? "없음"} → {releaseId} ({target.Files.Count}개, {Format.Bytes(target.TotalSize)})");

        string versionDir = _layout.VersionDir(releaseId);
        if (Directory.Exists(versionDir))
        {
            // 롤백이나 전환 직전 중단으로 이미 완성된 폴더. 검증되면 다시 받지 않는다
            List<ManifestFile> bad = await VerifyTreeAsync(versionDir, target, InstallStage.Verifying, progress, ct);
            if (bad.Count == 0 && ExtraExecutables(versionDir, target).Count == 0)
            {
                _log.Info($"{releaseId} 폴더가 이미 온전하다, 전환만 한다");
                StateFile.Write(_layout.TransactionPath, NewTransaction(releaseId, manifestSha, TransactionRecord.PREPARED));
                SwitchActive(releaseId, manifestSha, progress, target.TotalSize);
                return;
            }

            _log.Warn($"{releaseId} 폴더가 불완전하다({bad.Count}개 불량), 새로 만든다");
            PathGuard.DeleteTree(_layout.Root, versionDir);
        }

        TransactionRecord transaction = ResumeOrStartTransaction(releaseId, manifestSha);
        string staging = _layout.StagingFor(transaction.Id);
        Directory.CreateDirectory(staging);
        PathGuard.EnsureNoReparsePoints(staging);

        Dictionary<string, List<string>> sources = LocalSources(local);
        EnsureDiskSpace(target, staging, sources);

        List<ManifestFile> pending = await FillFromLocalAsync(target, staging, sources, progress, ct);
        _options.Checkpoint?.Invoke("after-copy");

        await DownloadMissingAsync(target, staging, pending, progress, ct);
        _options.Checkpoint?.Invoke("after-download");

        progress?.Report(new InstallProgress(InstallStage.Finalizing, target.TotalSize, target.TotalSize, 0, 0));
        ValidateStaging(staging, target);
        _options.Checkpoint?.Invoke("before-move");

        await MoveDirectoryWithRetryAsync(staging, versionDir, ct);
        transaction.Phase = TransactionRecord.PREPARED;
        StateFile.Write(_layout.TransactionPath, transaction);
        _options.Checkpoint?.Invoke("before-switch");

        SwitchActive(releaseId, manifestSha, progress, target.TotalSize);
    }

    private void PrepareRoot()
    {
        if (_options.CheckVolume && LocalVolume.Problem(_layout.Root) is string volumeProblem)
        {
            throw new InstallBrokenException(volumeProblem);
        }

        foreach (string dir in _layout.OwnedDirectories())
        {
            Directory.CreateDirectory(dir);
            if (new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new PatchFormatException($"링크 폴더는 허용하지 않는다: {dir}");
            }
        }
    }

    private TransactionRecord NewTransaction(string releaseId, string manifestSha, string phase)
    {
        return new TransactionRecord
        {
            Id                   = Guid.NewGuid().ToString("N"),
            Phase                = phase,
            Channel              = Channel,
            TargetReleaseId      = releaseId,
            TargetManifestSha256 = manifestSha,
        };
    }

    private TransactionRecord ResumeOrStartTransaction(string releaseId, string manifestSha)
    {
        TransactionRecord? existing = ReadTransaction();
        if (existing != null && existing.Phase == TransactionRecord.PREPARING
            && existing.TargetReleaseId == releaseId && existing.TargetManifestSha256 == manifestSha
            && Guid.TryParseExact(existing.Id, "N", out _))
        {
            _log.Info($"중단된 설치를 이어서 한다: {existing.Id}");
            return existing;
        }

        foreach (string dir in Directory.EnumerateDirectories(_layout.StagingDir))
        {
            PathGuard.DeleteTree(_layout.Root, dir);
        }

        TransactionRecord created = NewTransaction(releaseId, manifestSha, TransactionRecord.PREPARING);
        StateFile.Write(_layout.TransactionPath, created);
        return created;
    }

    /// 현재·직전 버전에서 같은 해시 파일을 찾을 후보. 실제 내용은 복사하면서 다시 해시한다.
    private Dictionary<string, List<string>> LocalSources(LocalStatus local)
    {
        var sources = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        void Add(string? releaseId, ReleaseManifest? manifest)
        {
            if (releaseId == null || manifest == null)
            {
                return;
            }
            string dir = _layout.VersionDir(releaseId);
            foreach (ManifestFile file in manifest.Files)
            {
                if (!sources.TryGetValue(file.Sha256, out List<string>? list))
                {
                    sources[file.Sha256] = list = new List<string>();
                }
                list.Add(PathGuard.Resolve(dir, file.Path));
            }
        }

        Add(local.Active?.ReleaseId, local.ActiveManifest);
        ActivePointer? backup = TryLoadPointer(_layout.ActiveBackupPath, out ReleaseManifest? backupManifest, out _, checkFiles: false);
        Add(backup?.ReleaseId, backupManifest);
        return sources;
    }

    // 새 버전 전체 복사 크기 + 받아야 할 크기 + 여유분을 본다(설계서 7장 5단계)
    private void EnsureDiskSpace(ReleaseManifest target, string staging, Dictionary<string, List<string>> sources)
    {
        long already = 0;
        foreach (ManifestFile file in target.Files)
        {
            var info = new FileInfo(PathGuard.Resolve(staging, file.Path));
            if (info.Exists && info.Length == file.Size)
            {
                already += file.Size;
            }
        }

        long download = target.Files
            .Where(f => !sources.ContainsKey(f.Sha256))
            .GroupBy(f => f.Sha256)
            .Sum(g => g.First().Size);

        long needed    = (target.TotalSize - already) + download + _options.SafetyMarginBytes;
        long available = LocalVolume.AvailableBytes(_layout.Root);
        if (needed > available)
        {
            throw new DiskSpaceException(needed, available);
        }
    }

    private async Task<List<ManifestFile>> FillFromLocalAsync(ReleaseManifest target, string staging,
        Dictionary<string, List<string>> sources, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        var  pending = new List<ManifestFile>();
        long done    = 0;
        long total   = target.TotalSize;

        foreach (ManifestFile file in target.Files)
        {
            ct.ThrowIfCancellationRequested();
            string destination = PathGuard.Resolve(staging, file.Path);

            // 이어서 하는 설치면 이미 들어간 파일을 해시로 확인하고 남긴다
            var existing = new FileInfo(destination);
            if (existing.Exists)
            {
                if (existing.Length == file.Size && await HashFileAsync(destination, ct) == file.Sha256)
                {
                    done += file.Size;
                    progress?.Report(new InstallProgress(InstallStage.Copying, done, total, 0, 0));
                    continue;
                }
                File.Delete(destination);
            }

            bool satisfied = false;
            if (sources.TryGetValue(file.Sha256, out List<string>? candidates))
            {
                foreach (string source in candidates)
                {
                    if (!File.Exists(source) || new FileInfo(source).Length != file.Size)
                    {
                        continue;
                    }

                    string copied = await CopyWithHashAsync(source, destination, ct);
                    if (copied == file.Sha256)
                    {
                        satisfied = true;
                        break;
                    }

                    _log.Warn($"기존 파일이 손상돼 다시 받는다: {source}");
                    File.Delete(destination);
                }
            }

            if (satisfied)
            {
                done += file.Size;
            }
            else
            {
                pending.Add(file);
            }
            progress?.Report(new InstallProgress(InstallStage.Copying, done, total, 0, 0));
        }

        _log.Info($"기존 파일 재사용 {target.Files.Count - pending.Count}개, 다운로드 필요 {pending.Count}개");
        return pending;
    }

    private async Task DownloadMissingAsync(ReleaseManifest target, string staging, List<ManifestFile> pending,
        IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        List<IGrouping<string, ManifestFile>> groups = pending.GroupBy(f => f.Sha256, StringComparer.Ordinal).ToList();
        long downloadTotal = groups.Sum(g => g.First().Size);
        long localDone     = target.TotalSize - pending.Sum(f => f.Size);

        if (groups.Count == 0)
        {
            return;
        }

        _log.Info($"다운로드 {groups.Count}개 객체, {Format.Bytes(downloadTotal)}");

        var  active    = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);
        long completed = 0;
        void Report()
        {
            long downloaded = Interlocked.Read(ref completed) + active.Values.Sum();
            progress?.Report(new InstallProgress(InstallStage.Downloading, localDone + downloaded, target.TotalSize, downloaded, downloadTotal));
        }

        var parallel = new ParallelOptions { MaxDegreeOfParallelism = _options.Concurrency, CancellationToken = ct };
        await Parallel.ForEachAsync(groups, parallel, async (group, token) =>
        {
            ManifestFile first = group.First();
            string part = _layout.PartPath(first.Sha256);

            await DownloadVerifiedAsync(first.Sha256, first.Size, part, length => { active[first.Sha256] = length; Report(); }, token);

            string firstDestination = PathGuard.Resolve(staging, first.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(firstDestination)!);
            File.Move(part, firstDestination, true);
            File.Delete(part + ".json");

            foreach (ManifestFile duplicate in group.Skip(1))
            {
                string destination = PathGuard.Resolve(staging, duplicate.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(firstDestination, destination, true);
            }

            active.TryRemove(first.Sha256, out _);
            Interlocked.Add(ref completed, first.Size);
            Report();
        });
    }

    private async Task DownloadVerifiedAsync(string sha, long size, string part, Action<long> onLength, CancellationToken ct)
    {
        // 빈 파일은 받을 내용이 없다. 저장소마다 0바이트 응답 처리가 달라 여기서 직접 만든다
        if (size == 0)
        {
            if (sha != Sha256Hex.OfBytes(ReadOnlySpan<byte>.Empty))
            {
                throw new PatchTrustException($"크기 0인데 해시가 빈 파일 해시가 아니다: {sha}");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(part)!);
            await File.WriteAllBytesAsync(part, Array.Empty<byte>(), ct);
            onLength(0);
            return;
        }

        Exception? last = null;
        for (int attempt = 1; attempt <= _options.MaxAttempts; ++attempt)
        {
            try
            {
                await _remote.DownloadAsync(RepoLayout.ObjectKey(sha), size, part, onLength, ct);
                if (new FileInfo(part).Length == size && await HashFileAsync(part, ct) == sha)
                {
                    return;
                }

                _log.Warn($"받은 파일의 해시가 다르다, 버리고 다시 받는다: {sha} (시도 {attempt})");
                DeletePart(part);
                last = new PatchTrustException($"객체 {sha} 해시 불일치");
            }
            catch (RemoteNotFoundException e)
            {
                // 다른 릴리스 파일로 대체하지 않는다(설계서 8장)
                throw new PatchTrustException($"배포가 불완전하다 — 객체가 서버에 없다: {sha}", e);
            }
            catch (Exception e) when (e is RemoteTransientException or IOException && !ct.IsCancellationRequested)
            {
                _log.Warn($"다운로드 실패, 재시도한다: {sha} (시도 {attempt}/{_options.MaxAttempts}) {e.Message}");
                last = e;
            }

            if (attempt < _options.MaxAttempts)
            {
                await Task.Delay(_options.RetryBaseDelay * Math.Pow(2, attempt - 1), ct);
            }
        }

        throw new RemoteTransientException($"객체 {sha}를 {_options.MaxAttempts}번 시도했지만 받지 못했다", last);
    }

    // 방금 쓴 파일 수천 개를 백신이 검사하는 동안에는 폴더 이름 바꾸기가 거부된다. 검사가 끝날 때까지 잠깐 기다린다
    private async Task MoveDirectoryWithRetryAsync(string source, string destination, CancellationToken ct)
    {
        const int MAX_ATTEMPTS = 12;
        for (int attempt = 1; ; ++attempt)
        {
            try
            {
                Directory.Move(source, destination);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < MAX_ATTEMPTS)
            {
                _log.Warn($"설치 폴더 이동이 막혔다, 백신 검사 등을 기다린다 (시도 {attempt}/{MAX_ATTEMPTS}): {e.Message}");
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), ct);
            }
        }
    }

    private static void DeletePart(string part)
    {
        File.Delete(part);
        File.Delete(part + ".json");
    }

    // staging이 manifest와 정확히 같은지 본다. 목록에 없는 파일·링크가 있으면 전환하지 않는다
    private static void ValidateStaging(string staging, ReleaseManifest target)
    {
        PathGuard.EnsureNoReparsePoints(staging);

        var expected = target.Files.ToDictionary(f => f.Path, f => f.Size, StringComparer.Ordinal);
        var options  = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 };
        int found    = 0;

        foreach (string file in Directory.EnumerateFiles(staging, "*", options))
        {
            string relative = Path.GetRelativePath(staging, file).Replace(Path.DirectorySeparatorChar, '/');
            if (!expected.TryGetValue(relative, out long size))
            {
                throw new InstallBrokenException($"설치 준비 폴더에 목록에 없는 파일이 있다: {relative}");
            }
            if (new FileInfo(file).Length != size)
            {
                throw new InstallBrokenException($"설치 준비 폴더의 파일 크기가 다르다: {relative}");
            }
            ++found;
        }

        if (found != expected.Count)
        {
            throw new InstallBrokenException($"설치 준비 폴더의 파일 수가 다르다: {found}/{expected.Count}");
        }
    }

    private void SwitchActive(string releaseId, string manifestSha, IProgress<InstallProgress>? progress, long total)
    {
        var pointer = new ActivePointer
        {
            ReleaseId      = releaseId,
            ManifestSha256 = manifestSha,
            Channel        = Channel,
            ActivatedAtUtc = _options.Clock(),
        };

        AtomicFile.ReplaceWithBackup(_layout.ActivePath, _layout.ActiveBackupPath, StrictJson.SerializeToUtf8Bytes(pointer));
        _options.Checkpoint?.Invoke("after-switch");

        LocalStatus after = ReadLocalStatus();
        if (!after.IsInstalled || after.Active!.ReleaseId != releaseId)
        {
            throw new InstallBrokenException($"전환 후 확인 실패: {after.Problem ?? after.Active?.ReleaseId}");
        }

        File.Delete(_layout.TransactionPath);
        _log.Info($"전환 완료: {releaseId}");
        progress?.Report(new InstallProgress(InstallStage.Finalizing, total, total, 0, 0));

        Cleanup(after);
    }

    // 현재·직전 정상 버전만 남긴다. 실행 중인 버전은 지우지 않는다(설계서 10장)
    private void Cleanup(LocalStatus current)
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { current.Active!.ReleaseId };
        ActivePointer? backup = TryLoadPointer(_layout.ActiveBackupPath, out _, out _, checkFiles: false);
        if (backup != null)
        {
            keep.Add(backup.ReleaseId);
        }

        HashSet<string> running = GameProcess.RunningReleaseIds(_layout);

        foreach (string dir in Directory.EnumerateDirectories(_layout.VersionsDir))
        {
            string name = Path.GetFileName(dir);
            if (keep.Contains(name) || running.Contains(name))
            {
                continue;
            }
            TryDelete(() => PathGuard.DeleteTree(_layout.Root, dir), dir);
        }

        foreach (string dir in Directory.EnumerateDirectories(_layout.StagingDir))
        {
            TryDelete(() => PathGuard.DeleteTree(_layout.Root, dir), dir);
        }

        foreach (string file in Directory.EnumerateFiles(_layout.CacheDir))
        {
            TryDelete(() => File.Delete(file), file);
        }

        foreach (string file in Directory.EnumerateFiles(_layout.ManifestsDir, "*.json"))
        {
            if (!keep.Contains(Path.GetFileNameWithoutExtension(file)))
            {
                TryDelete(() => File.Delete(file), file);
            }
        }
    }

    private void TryDelete(Action delete, string path)
    {
        try
        {
            delete();
            _log.Info($"정리: {path}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 백신 검사 중이거나 탐색기가 잡고 있는 경우. 다음 업데이트 때 다시 지운다
            _log.Warn($"정리 보류: {path} ({e.Message})");
        }
    }

    /****************************************
    *            Verify · Repair
    ****************************************/

    public async Task<RepairResult> VerifyAndRepairAsync(IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        using InstallLock _ = InstallLock.Acquire(_layout);
        PrepareRoot();
        Recover();

        ActivePointer? active = TryLoadPointer(_layout.ActivePath, out ReleaseManifest? manifest, out string? problem, checkFiles: false);
        if (active == null || manifest == null)
        {
            throw new InstallBrokenException(problem ?? "설치된 게임이 없다");
        }
        if (GameProcess.IsRunning(_layout, Path.GetFileNameWithoutExtension(manifest.EntryPoint)))
        {
            throw new GameRunningException();
        }

        string dir = _layout.VersionDir(active.ReleaseId);
        Directory.CreateDirectory(dir);
        PathGuard.EnsureNoReparsePoints(dir);

        List<ManifestFile> bad = await VerifyTreeAsync(dir, manifest, InstallStage.Verifying, progress, ct);

        List<string> extras = ExtraExecutables(dir, manifest);
        foreach (string extra in extras)
        {
            _log.Warn($"목록에 없는 실행 파일을 지운다: {extra}");
            File.Delete(extra);
        }

        long repairTotal = bad.Sum(f => f.Size);
        long repaired    = 0;
        foreach (IGrouping<string, ManifestFile> group in bad.GroupBy(f => f.Sha256, StringComparer.Ordinal))
        {
            ManifestFile first = group.First();
            string part = _layout.PartPath(first.Sha256);
            long before = repaired;
            await DownloadVerifiedAsync(first.Sha256, first.Size, part,
                length => progress?.Report(new InstallProgress(InstallStage.Repairing, before + length, repairTotal, before + length, repairTotal)), ct);

            foreach (ManifestFile file in group)
            {
                string destination = PathGuard.Resolve(dir, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                string temp = destination + ".repair";
                File.Copy(part, temp, true);
                File.Move(temp, destination, true);
                _log.Info($"복구: {file.Path}");
            }
            DeletePart(part);
            repaired += first.Size * group.Count();
        }

        _log.Info($"검사 완료: {manifest.Files.Count}개 중 {bad.Count}개 복구, 불필요 실행 파일 {extras.Count}개 제거");
        return new RepairResult(manifest.Files.Count, bad.Count, extras.Count);
    }

    private async Task<List<ManifestFile>> VerifyTreeAsync(string dir, ReleaseManifest manifest, InstallStage stage,
        IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        var  bad   = new List<ManifestFile>();
        long done  = 0;
        long total = manifest.TotalSize;

        foreach (ManifestFile file in manifest.Files)
        {
            ct.ThrowIfCancellationRequested();
            string path = PathGuard.Resolve(dir, file.Path);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != file.Size || await HashFileAsync(path, ct) != file.Sha256)
            {
                bad.Add(file);
            }
            done += file.Size;
            progress?.Report(new InstallProgress(stage, done, total, 0, 0));
        }
        return bad;
    }

    private static List<string> ExtraExecutables(string dir, ReleaseManifest manifest)
    {
        var expected = new HashSet<string>(manifest.Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        var options  = new EnumerationOptions { RecurseSubdirectories = true };
        return Directory.EnumerateFiles(dir, "*", options)
            .Where(f => ExecutableSuffixes.Any(s => f.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
            .Where(f => !expected.Contains(Path.GetRelativePath(dir, f).Replace(Path.DirectorySeparatorChar, '/')))
            .ToList();
    }

    /****************************************
    *              File Helpers
    ****************************************/

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true);
        byte[] hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // 한 번 읽으면서 복사와 해시를 같이 한다. 기존 파일의 실제 내용을 크기·시각이 아니라 해시로 확인한다
    private static async Task<string> CopyWithHashAsync(string source, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1 << 20];

        await using (var input  = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true))
        await using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true))
        {
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
