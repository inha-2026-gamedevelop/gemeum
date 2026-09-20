namespace Geumeum.Patch;

/// 배포 저장소의 경로 규칙. 로컬 폴더·R2 버킷·HTTP 경로가 모두 같은 키를 쓴다(설계서 6장).
public static class RepoLayout
{
    /// 처음 받는 사람을 위한 고정 주소. 자동 업데이트는 이 파일이 아니라 LAUNCHER_LATEST_KEY가 가리키는 객체를 받는다
    public const string LAUNCHER_KEY        = "launcher/GeumeumLauncher.exe";
    public const string LAUNCHER_LATEST_KEY = "launcher/latest.json";

    public static string EnvelopeKey(string channel)   => $"channels/{channel}/envelope.json";
    public static string ManifestKey(string releaseId) => $"releases/{releaseId}/manifest.json";
    public static string ObjectKey(string sha256)      => $"objects/sha256/{sha256}";
    public static string NotesKey(string releaseId)    => $"notes/{releaseId}.txt";

    public static bool IsValidChannel(string? channel) => channel is "stable" or "internal";

    public static string LocalPath(string repoDir, string key)
    {
        return Path.Combine(repoDir, key.Replace('/', Path.DirectorySeparatorChar));
    }

    /// 객체·manifest는 불변이라 길게, 채널 문서는 매번 재검증하게 캐시한다(설계서 10장).
    public static string CacheControlFor(string key)
    {
        return key.StartsWith("channels/", StringComparison.Ordinal) || key == LAUNCHER_KEY || key == LAUNCHER_LATEST_KEY
            ? "no-cache, max-age=0"
            : "public, max-age=31536000, immutable";
    }
}

/// 설계서 10장 게시 순서 중 로컬 저장소를 만드는 부분. CLI와 테스트가 같이 쓴다.
public static class RepoPublisher
{
    public sealed record PackResult(ReleaseManifest Manifest, int NewObjects, string ManifestPath);

    public static PackResult Pack(string buildDir, string repoDir, ManifestBuilder.Options options,
                                  BuildIdentity? identity, bool allowMissingIdentity, string? notesPath = null)
    {
        string manifestPath = RepoLayout.LocalPath(repoDir, RepoLayout.ManifestKey(options.ReleaseId));
        if (File.Exists(manifestPath))
        {
            throw new PatchFormatException($"releaseId 재사용 금지 — 이미 게시된 manifest가 있다: {manifestPath}");
        }

        if (notesPath != null && !File.Exists(notesPath))
        {
            throw new PatchFormatException($"패치 노트 파일이 없다: {notesPath}");
        }

        ReleaseManifest manifest = ManifestBuilder.Build(buildDir, options);

        if (identity != null)
        {
            List<string> mismatches = identity.Mismatches(manifest);
            if (mismatches.Count > 0)
            {
                throw new PatchFormatException("게임 내장 build-identity와 manifest가 다르다:\n  " + string.Join("\n  ", mismatches));
            }
        }
        else if (!allowMissingIdentity)
        {
            throw new PatchFormatException($"build-identity가 없다: {BuildIdentity.RELATIVE_FILE_PATH}. 기존 빌드면 --allow-missing-identity를 준다.");
        }

        int copied = 0;
        foreach (ManifestFile file in manifest.Files)
        {
            string target = RepoLayout.LocalPath(repoDir, RepoLayout.ObjectKey(file.Sha256));
            if (File.Exists(target))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string temp = target + ".part";
            File.Copy(Path.Combine(buildDir, file.Path.Replace('/', Path.DirectorySeparatorChar)), temp, true);
            if (!string.Equals(Sha256Hex.OfFile(temp), file.Sha256, StringComparison.Ordinal))
            {
                File.Delete(temp);
                throw new PatchFormatException($"복사 중 파일이 바뀌었다: {file.Path}");
            }
            File.Move(temp, target);
            ++copied;
        }

        if (notesPath != null)
        {
            string notesTarget = RepoLayout.LocalPath(repoDir, RepoLayout.NotesKey(options.ReleaseId));
            Directory.CreateDirectory(Path.GetDirectoryName(notesTarget)!);
            File.Copy(notesPath, notesTarget, true);
        }

        // manifest가 있으면 "패키징 끝"으로 본다. 다른 단계가 모두 성공한 뒤 마지막에 원자적으로 쓴다
        byte[] manifestBytes = manifest.ToUtf8Bytes();
        ReleaseManifest.Parse(manifestBytes);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        string manifestTemp = manifestPath + ".tmp";
        File.WriteAllBytes(manifestTemp, manifestBytes);
        File.Move(manifestTemp, manifestPath);

        return new PackResult(manifest, copied, manifestPath);
    }

    public static ChannelPayload PublishChannel(string repoDir, string channel, string releaseId, long sequence,
                                                string keyId, string privateKeyPem, string minLauncherVersion,
                                                TimeSpan lifetime, DateTimeOffset nowUtc)
    {
        byte[] manifestBytes = File.ReadAllBytes(RepoLayout.LocalPath(repoDir, RepoLayout.ManifestKey(releaseId)));
        ReleaseManifest manifest = ReleaseManifest.Parse(manifestBytes);

        var payload = new ChannelPayload
        {
            Channel            = channel,
            Platform           = manifest.Platform,
            Sequence           = sequence,
            IssuedAtUtc        = nowUtc,
            ExpiresAtUtc       = nowUtc + lifetime,
            ReleaseId          = manifest.ReleaseId,
            ManifestSha256     = Sha256Hex.OfBytes(manifestBytes),
            ManifestSize       = manifestBytes.Length,
            MinLauncherVersion = minLauncherVersion,
        };
        ChannelPayload.Parse(payload.ToUtf8Bytes());

        string envelopePath = RepoLayout.LocalPath(repoDir, RepoLayout.EnvelopeKey(channel));
        if (File.Exists(envelopePath))
        {
            ChannelPayload previous = OpenUnverified(File.ReadAllBytes(envelopePath));
            if (sequence <= previous.Sequence)
            {
                throw new PatchFormatException($"sequence는 증가만 한다: 기존={previous.Sequence}, 요청={sequence}");
            }
        }

        SignedEnvelope envelope = SignedEnvelope.Create(keyId, privateKeyPem, payload);
        Directory.CreateDirectory(Path.GetDirectoryName(envelopePath)!);
        string temp = envelopePath + ".tmp";
        File.WriteAllBytes(temp, envelope.ToUtf8Bytes());
        File.Move(temp, envelopePath, true);
        return payload;
    }

    /// 새 런처를 객체 저장소에 넣고 서명된 latest.json을 쓴다. 이미 더 높은 버전이 게시돼 있으면 거부한다(자동 다운그레이드 방지).
    public static LauncherRelease PublishLauncher(string repoDir, string exePath, Version version, string keyId, string privateKeyPem, DateTimeOffset nowUtc)
    {
        var release = new LauncherRelease
        {
            Version     = LauncherRelease.Normalize(version).ToString(),
            Sha256      = Sha256Hex.OfFile(exePath),
            Size        = new FileInfo(exePath).Length,
            IssuedAtUtc = nowUtc,
        };
        LauncherRelease.Parse(release.ToUtf8Bytes());

        string latestPath = RepoLayout.LocalPath(repoDir, RepoLayout.LAUNCHER_LATEST_KEY);
        if (File.Exists(latestPath))
        {
            SignedEnvelope previousEnvelope = SignedEnvelope.Parse(File.ReadAllBytes(latestPath));
            LauncherRelease previous = LauncherRelease.Parse(Convert.FromBase64String(previousEnvelope.PayloadBase64));
            if (previous.ParsedVersion > release.ParsedVersion)
            {
                throw new PatchFormatException($"이미 더 높은 런처 {previous.Version}가 게시돼 있다. 새 런처 버전 {release.Version}을 올려야 한다");
            }
            if (previous.ParsedVersion == release.ParsedVersion && previous.Sha256 != release.Sha256)
            {
                throw new PatchFormatException($"같은 버전 {release.Version}에 다른 exe를 올릴 수 없다. Launcher.UI.csproj의 <Version>을 올린다");
            }
        }

        string objectPath = RepoLayout.LocalPath(repoDir, RepoLayout.ObjectKey(release.Sha256));
        if (!File.Exists(objectPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(objectPath)!);
            File.Copy(exePath, objectPath + ".part", true);
            File.Move(objectPath + ".part", objectPath);
        }

        // 처음 받는 사람용 고정 주소도 같이 갱신한다
        string fixedPath = RepoLayout.LocalPath(repoDir, RepoLayout.LAUNCHER_KEY);
        Directory.CreateDirectory(Path.GetDirectoryName(fixedPath)!);
        File.Copy(exePath, fixedPath + ".tmp", true);
        File.Move(fixedPath + ".tmp", fixedPath, true);

        // latest.json은 객체가 준비된 뒤 마지막에 바꾼다
        File.WriteAllBytes(latestPath + ".tmp", SignedEnvelope.Create(keyId, privateKeyPem, release.ToUtf8Bytes()).ToUtf8Bytes());
        File.Move(latestPath + ".tmp", latestPath, true);
        return release;
    }

    /// 게시 도구가 자기 저장소의 이전 sequence를 읽을 때만 쓴다. 런처는 반드시 VerifyAndOpen을 쓴다.
    public static ChannelPayload OpenUnverified(byte[] envelopeBytes)
    {
        SignedEnvelope envelope = SignedEnvelope.Parse(envelopeBytes);
        return ChannelPayload.Parse(Convert.FromBase64String(envelope.PayloadBase64));
    }
}
