using System.Net;
using System.Net.Sockets;
using System.Text;
using Geumeum.Packager;
using Geumeum.Patch.Client;
using Xunit;

namespace Geumeum.Patch.Tests;

/// <summary>
/// P2 통과 기준과 필수 검증 목록 중 런처 엔진이 맡는 항목
/// </summary>
public sealed class InstallerTests : IDisposable
{
    private const string KEY_ID  = "test-key";
    private const string CHANNEL = "internal";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "geumeum-installer-tests", Guid.NewGuid().ToString("N"));
    private readonly string _repo;
    private readonly string _install;
    private readonly string _privateKey;
    private readonly Dictionary<string, string> _trusted;
    private long _sequence;

    public InstallerTests()
    {
        _repo    = Path.Combine(_root, "repo");
        _install = Path.Combine(_root, "install");
        Directory.CreateDirectory(_repo);
        (string priv, string pub) = ReleaseSigner.GenerateKeyPair();
        _privateKey = priv;
        _trusted    = new Dictionary<string, string> { [KEY_ID] = pub };
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /****************************************
    *                Fixture
    ****************************************/

    private static byte[] Bytes(int seed, int size)
    {
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        return data;
    }

    private static Dictionary<string, byte[]> V1() => new()
    {
        ["gemeum.exe"]                    = Bytes(1, 5_000),
        ["gemeum_Data/globalgamemanagers"] = Bytes(2, 40_000),
        ["gemeum_Data/sharedassets0.assets"] = Bytes(3, 300_000),
        ["gemeum_Data/removed.bin"]       = Bytes(4, 1_000),
        ["steam_appid.txt"]               = Encoding.ASCII.GetBytes("480"),
        ["dup/a.bin"]                     = Bytes(9, 2_000),
        ["dup/b.bin"]                     = Bytes(9, 2_000),
        ["gemeum_Data/empty.resS"]        = Array.Empty<byte>(),
    };

    private static Dictionary<string, byte[]> V2()
    {
        Dictionary<string, byte[]> files = V1();
        files["gemeum_Data/sharedassets0.assets"] = Bytes(30, 300_000);
        files.Remove("gemeum_Data/removed.bin");
        files["gemeum_Data/added.bin"] = Bytes(5, 7_000);
        return files;
    }

    private string Publish(string releaseId, Dictionary<string, byte[]> files, string minLauncher = "1.0.0")
    {
        string build = Path.Combine(_root, "builds", releaseId);
        foreach ((string path, byte[] content) in files)
        {
            string full = Path.Combine(build, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, content);
        }

        if (!File.Exists(RepoLayout.LocalPath(_repo, RepoLayout.ManifestKey(releaseId))))
        {
            RepoPublisher.Pack(build, _repo, new ManifestBuilder.Options
            {
                ReleaseId = releaseId, DisplayVersion = releaseId[^3..], ProtocolVersion = 1, ContentVersion = releaseId,
            }, null, allowMissingIdentity: true);
        }

        RepoPublisher.PublishChannel(_repo, CHANNEL, releaseId, ++_sequence, KEY_ID, _privateKey, minLauncher,
            ChannelPayload.DEFAULT_LIFETIME, DateTimeOffset.UtcNow);
        return releaseId;
    }

    private Installer NewInstaller(IRemoteRepository remote, Action<string>? checkpoint = null)
    {
        return new Installer(new InstallLayout(_install), remote, _trusted, CHANNEL, new Version(1, 0, 0), new PatchLog(null),
            new InstallerOptions { Checkpoint = checkpoint, RetryBaseDelay = TimeSpan.FromMilliseconds(5), SafetyMarginBytes = 0 });
    }

    private async Task<Installer> InstallAsync(IRemoteRepository remote, Action<string>? checkpoint = null)
    {
        Installer installer = NewInstaller(remote, checkpoint);
        UpdateCheck check = await installer.CheckAsync(CancellationToken.None);
        await installer.InstallAsync(check, null, CancellationToken.None);
        return installer;
    }

    private void AssertInstalled(string releaseId, Dictionary<string, byte[]> expected)
    {
        LocalStatus status = NewInstaller(new FolderRemoteRepository(_repo)).GetLocalStatus();
        Assert.True(status.IsInstalled, status.Problem);
        Assert.Equal(releaseId, status.Active!.ReleaseId);

        string dir = new InstallLayout(_install).VersionDir(releaseId);
        string[] actual = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected.Keys.OrderBy(p => p, StringComparer.Ordinal), actual);
        foreach ((string path, byte[] content) in expected)
        {
            Assert.Equal(content, File.ReadAllBytes(Path.Combine(dir, path.Replace('/', Path.DirectorySeparatorChar))));
        }
    }

    private sealed class CountingRemote : IRemoteRepository
    {
        private readonly IRemoteRepository _inner;
        public CountingRemote(IRemoteRepository inner) { _inner = inner; }
        public List<string> Downloads { get; } = new();
        public string Description => _inner.Description;
        public Task<byte[]> GetBytesAsync(string key, int maxBytes, CancellationToken ct) => _inner.GetBytesAsync(key, maxBytes, ct);
        public Task DownloadAsync(string key, long size, string part, Action<long> onLength, CancellationToken ct)
        {
            lock (Downloads) { Downloads.Add(key); }
            return _inner.DownloadAsync(key, size, part, onLength, ct);
        }
        public void Dispose() => _inner.Dispose();
    }

    private sealed class SimulatedCrash : Exception
    {
        public SimulatedCrash(string at) : base(at) { }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /****************************************
    *                 Tests
    ****************************************/

    [Fact]
    public async Task FreshInstall_ThenSameVersion_DownloadsNothing()
    {
        string v1 = Publish("win64-20260919-0001", V1());
        var remote = new CountingRemote(new FolderRemoteRepository(_repo));

        await InstallAsync(remote);
        AssertInstalled(v1, V1());
        Assert.Equal(6, remote.Downloads.Count); // 8개 파일 중 dup 두 개는 같은 객체, 빈 파일은 받지 않는다

        remote.Downloads.Clear();
        Installer again = NewInstaller(remote);
        UpdateCheck check = await again.CheckAsync(CancellationToken.None);
        Assert.True(check.IsUpToDate);
        await again.InstallAsync(check, null, CancellationToken.None);
        Assert.Empty(remote.Downloads);
    }

    [Fact]
    public async Task Update_TransfersOnlyChangedAndAdded_KeepsPreviousOnly()
    {
        string v1 = Publish("win64-20260919-0001", V1());
        await InstallAsync(new FolderRemoteRepository(_repo));

        string v2 = Publish("win64-20260919-0002", V2());
        var remote = new CountingRemote(new FolderRemoteRepository(_repo));
        await InstallAsync(remote);

        AssertInstalled(v2, V2());
        Assert.Equal(2, remote.Downloads.Count);
        var layout = new InstallLayout(_install);
        Assert.True(Directory.Exists(layout.VersionDir(v1)), "직전 버전은 남는다");

        Dictionary<string, byte[]> v3Files = V2();
        v3Files["gemeum.exe"] = Bytes(100, 5_000);
        string v3 = Publish("win64-20260919-0003", v3Files);
        await InstallAsync(new FolderRemoteRepository(_repo));

        AssertInstalled(v3, v3Files);
        Assert.False(Directory.Exists(layout.VersionDir(v1)), "현재·직전 외 버전은 정리한다");
        Assert.True(Directory.Exists(layout.VersionDir(v2)));
        Assert.Empty(Directory.EnumerateFileSystemEntries(layout.StagingDir));
        Assert.Empty(Directory.EnumerateFileSystemEntries(layout.CacheDir));
    }

    [Theory]
    [InlineData("after-copy", false)]
    [InlineData("after-download", false)]
    [InlineData("before-move", false)]
    [InlineData("before-switch", false)]
    [InlineData("after-switch", true)]
    public async Task CrashAtAnyPoint_LeavesCompleteOldOrNew_ThenResumes(string crashAt, bool expectNew)
    {
        string v1 = Publish("win64-20260919-0001", V1());
        await InstallAsync(new FolderRemoteRepository(_repo));
        string v2 = Publish("win64-20260919-0002", V2());

        await Assert.ThrowsAsync<SimulatedCrash>(() => InstallAsync(new FolderRemoteRepository(_repo),
            point => { if (point == crashAt) throw new SimulatedCrash(point); }));

        if (expectNew) AssertInstalled(v2, V2());
        else           AssertInstalled(v1, V1());

        var remote = new CountingRemote(new FolderRemoteRepository(_repo));
        await InstallAsync(remote);
        AssertInstalled(v2, V2());

        // 다운로드가 끝난 뒤 멈췄다면 staging, 버전 폴더를 이어 써서 다시 받지 않는다
        if (crashAt is "after-download" or "before-move" or "before-switch" or "after-switch")
        {
            Assert.Empty(remote.Downloads);
        }
    }

    [Fact]
    public async Task Http_ResumesInterruptedDownloadWithRange()
    {
        Publish("win64-20260919-0001", V1());
        using var server = new RepoHttpServer(_repo, $"http://localhost:{FreePort()}/") { AbortAfterBytes = 100_000, AbortCount = 1 };
        server.Start();

        using var remote = new HttpRemoteRepository(new Uri(server.Prefix), allowInsecureHttp: true);
        await InstallAsync(remote);

        AssertInstalled("win64-20260919-0001", V1());
        string[] log = server.Requests.ToArray();
        Assert.Contains(log, l => l.StartsWith("GET objects/", StringComparison.Ordinal) && l.EndsWith(" -200", StringComparison.Ordinal));
        Assert.Contains(log, l => l.StartsWith("GET objects/", StringComparison.Ordinal) && l.EndsWith(" 206", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Http_IfRangeMismatch_RestartsFromZero()
    {
        Publish("win64-20260919-0001", V1());
        byte[] big = V1()["gemeum_Data/sharedassets0.assets"];
        string sha = Sha256Hex.OfBytes(big);

        var layout = new InstallLayout(_install);
        Directory.CreateDirectory(layout.CacheDir);
        File.WriteAllBytes(layout.PartPath(sha), Bytes(77, 1_000)); // 다른 내용의 조각
        File.WriteAllText(layout.PartPath(sha) + ".json", $$"""{"size":{{big.Length}},"eTag":"\"stale\""}""");

        using var server = new RepoHttpServer(_repo, $"http://localhost:{FreePort()}/");
        server.Start();
        using var remote = new HttpRemoteRepository(new Uri(server.Prefix), allowInsecureHttp: true);
        await InstallAsync(remote);

        AssertInstalled("win64-20260919-0001", V1());
        Assert.Contains($"GET objects/sha256/{sha} 200", server.Requests);
    }

    [Fact]
    public async Task TamperedObject_FailsAndKeepsPreviousVersion()
    {
        string v1 = Publish("win64-20260919-0001", V1());
        await InstallAsync(new FolderRemoteRepository(_repo));
        Publish("win64-20260919-0002", V2());

        string sha = Sha256Hex.OfBytes(V2()["gemeum_Data/added.bin"]);
        File.WriteAllBytes(RepoLayout.LocalPath(_repo, RepoLayout.ObjectKey(sha)), Bytes(666, 7_000));

        await Assert.ThrowsAsync<RemoteTransientException>(() => InstallAsync(new FolderRemoteRepository(_repo)));
        AssertInstalled(v1, V1());
        Assert.False(Directory.Exists(new InstallLayout(_install).VersionDir("win64-20260919-0002")));
    }

    [Fact]
    public async Task MissingObject_IsIncompleteRelease_NotRetriedForever()
    {
        string v1 = Publish("win64-20260919-0001", V1());
        await InstallAsync(new FolderRemoteRepository(_repo));
        Publish("win64-20260919-0002", V2());
        File.Delete(RepoLayout.LocalPath(_repo, RepoLayout.ObjectKey(Sha256Hex.OfBytes(V2()["gemeum_Data/added.bin"]))));

        await Assert.ThrowsAsync<PatchTrustException>(() => InstallAsync(new FolderRemoteRepository(_repo)));
        AssertInstalled(v1, V1());
    }

    [Fact]
    public async Task OlderSequence_AndSameSequenceDifferentPayload_AreRejected()
    {
        Publish("win64-20260919-0001", V1());
        string envelopePath = RepoLayout.LocalPath(_repo, RepoLayout.EnvelopeKey(CHANNEL));
        byte[] seq1 = File.ReadAllBytes(envelopePath);

        Publish("win64-20260919-0002", V2());
        await InstallAsync(new FolderRemoteRepository(_repo));

        File.WriteAllBytes(envelopePath, seq1);
        await Assert.ThrowsAsync<PatchTrustException>(() => NewInstaller(new FolderRemoteRepository(_repo)).CheckAsync(CancellationToken.None));

        // 같은 seq 2에 다른 releaseId를 서명해 올린 경우
        byte[] manifestBytes = File.ReadAllBytes(RepoLayout.LocalPath(_repo, RepoLayout.ManifestKey("win64-20260919-0001")));
        var forged = new ChannelPayload
        {
            Channel = CHANNEL, Platform = BuildIdentity.PLATFORM_WIN_X64, Sequence = 2,
            IssuedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
            ReleaseId = "win64-20260919-0001", ManifestSha256 = Sha256Hex.OfBytes(manifestBytes),
            ManifestSize = manifestBytes.Length, MinLauncherVersion = "1.0.0",
        };
        File.WriteAllBytes(envelopePath, SignedEnvelope.Create(KEY_ID, _privateKey, forged).ToUtf8Bytes());
        await Assert.ThrowsAsync<PatchTrustException>(() => NewInstaller(new FolderRemoteRepository(_repo)).CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Rollback_ReusesKeptPreviousVersionWithoutDownload()
    {
        string v1 = Publish("win64-20260919-0001", V1());
        await InstallAsync(new FolderRemoteRepository(_repo));
        Publish("win64-20260919-0002", V2());
        await InstallAsync(new FolderRemoteRepository(_repo));

        Publish(v1, V1()); // 새 sequence가 이전 releaseId를 가리킨다
        var remote = new CountingRemote(new FolderRemoteRepository(_repo));
        await InstallAsync(remote);

        AssertInstalled(v1, V1());
        Assert.Empty(remote.Downloads);
    }

    [Fact]
    public async Task VerifyAndRepair_RestoresCorruptAndMissing_RemovesExtraExecutables()
    {
        string v1 = Publish("win64-20260919-0001", V1());
        Installer installer = await InstallAsync(new FolderRemoteRepository(_repo));

        string dir = new InstallLayout(_install).VersionDir(v1);
        File.WriteAllBytes(Path.Combine(dir, "gemeum.exe"), Bytes(55, 5_000));
        File.Delete(Path.Combine(dir, "steam_appid.txt"));
        File.WriteAllBytes(Path.Combine(dir, "injected.dll"), Bytes(56, 10));
        File.WriteAllText(Path.Combine(dir, "player.log"), "게임이 남긴 로그는 건드리지 않는다");

        RepairResult result = await installer.VerifyAndRepairAsync(null, CancellationToken.None);

        Assert.Equal(2, result.Repaired);
        Assert.Equal(1, result.RemovedExtras);
        File.Delete(Path.Combine(dir, "player.log"));
        AssertInstalled(v1, V1());
    }

    [Fact]
    public async Task SecondLauncher_IsBlockedByInstallLock()
    {
        Publish("win64-20260919-0001", V1());
        Installer installer = NewInstaller(new FolderRemoteRepository(_repo));
        UpdateCheck check = await installer.CheckAsync(CancellationToken.None);

        using (InstallLock.Acquire(new InstallLayout(_install)))
        {
            await Assert.ThrowsAsync<InstallBusyException>(() => installer.InstallAsync(check, null, CancellationToken.None));
        }
    }

    [Fact]
    public async Task NewerMinLauncherVersion_AsksForLauncherUpdate()
    {
        Publish("win64-20260919-0001", V1(), minLauncher: "2.0.0");
        LauncherTooOldException e = await Assert.ThrowsAsync<LauncherTooOldException>(
            () => NewInstaller(new FolderRemoteRepository(_repo)).CheckAsync(CancellationToken.None));
        Assert.Equal(new Version(2, 0, 0), e.Required);
    }

    [Fact]
    public async Task CancelledInstall_ResumesWithoutRedownloading()
    {
        Publish("win64-20260919-0001", V1());
        using var cts = new CancellationTokenSource();
        Installer installer = NewInstaller(new FolderRemoteRepository(_repo), point => { if (point == "after-download") cts.Cancel(); });
        UpdateCheck check = await installer.CheckAsync(CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await installer.InstallAsync(check, null, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
        });

        var remote = new CountingRemote(new FolderRemoteRepository(_repo));
        await InstallAsync(remote);
        AssertInstalled("win64-20260919-0001", V1());
    }

    [Fact]
    public void PathGuard_RejectsEscapes()
    {
        Assert.Throws<PatchFormatException>(() => PathGuard.Resolve(_root, "../x.dll"));
        Assert.Throws<PatchFormatException>(() => PathGuard.Resolve(_root, "a/../../x.dll"));
        Assert.Throws<PatchFormatException>(() => PathGuard.Resolve(_root, "C:/Windows/x.dll"));
        Assert.EndsWith(Path.Combine("a", "b.dll"), PathGuard.Resolve(_root, "a/b.dll"));
    }
}
