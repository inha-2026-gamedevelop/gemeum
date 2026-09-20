using Geumeum.Patch.Client;
using Xunit;

namespace Geumeum.Patch.Tests;

/// 런처 자기 업데이트: 서명된 latest.json → 해시 검증 → exe 교체 → 이전 exe 정리.
public sealed class LauncherUpdaterTests : IDisposable
{
    private const string KEY_ID = "test-key";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "geumeum-launcher-update-tests", Guid.NewGuid().ToString("N"));
    private readonly string _repo;
    private readonly string _installed;
    private readonly string _privateKey;
    private readonly Dictionary<string, string> _trusted;

    public LauncherUpdaterTests()
    {
        _repo      = Path.Combine(_root, "repo");
        _installed = Path.Combine(_root, "user", "GeumeumLauncher.exe");
        Directory.CreateDirectory(_repo);
        Directory.CreateDirectory(Path.GetDirectoryName(_installed)!);
        File.WriteAllText(_installed, "launcher 1.1.0");
        (string priv, string pub) = ReleaseSigner.GenerateKeyPair();
        _privateKey = priv;
        _trusted    = new Dictionary<string, string> { [KEY_ID] = pub };
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private LauncherRelease PublishLauncher(string version, string content)
    {
        string exe = Path.Combine(_root, "builds", version, "GeumeumLauncher.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, content);
        return RepoPublisher.PublishLauncher(_repo, exe, Version.Parse(version), KEY_ID, _privateKey, DateTimeOffset.UtcNow);
    }

    private LauncherUpdater Updater(string current = "1.1.0", Dictionary<string, string>? trusted = null)
    {
        return new LauncherUpdater(new FolderRemoteRepository(_repo), trusted ?? _trusted, Version.Parse(current), new PatchLog(null));
    }

    [Fact]
    public async Task NothingPublished_NoUpdate()
    {
        Assert.Null(await Updater().CheckAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("1.1.0")]
    [InlineData("1.0.5")]
    public async Task SameOrOlderVersion_NoUpdate(string published)
    {
        PublishLauncher(published, "launcher " + published);
        Assert.Null(await Updater("1.1.0").CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task NewerVersion_DownloadsVerifiesAndSwaps_ThenCleansOld()
    {
        PublishLauncher("1.2.0", "launcher 1.2.0");
        LauncherUpdater updater = Updater();

        LauncherRelease? release = await updater.CheckAsync(CancellationToken.None);
        Assert.NotNull(release);
        Assert.Equal("1.2.0", release!.Version);

        string newPath = await updater.DownloadAsync(release, _installed, _ => { }, CancellationToken.None);
        LauncherUpdater.Swap(_installed, newPath);

        Assert.Equal("launcher 1.2.0", File.ReadAllText(_installed));
        Assert.Equal("launcher 1.1.0", File.ReadAllText(_installed + ".old"));

        await LauncherUpdater.CleanupAsync(_installed, new PatchLog(null));
        Assert.Equal(new[] { "GeumeumLauncher.exe" }, Directory.GetFiles(Path.GetDirectoryName(_installed)!).Select(Path.GetFileName));
    }

    [Fact]
    public async Task TamperedObject_IsRejected_InstalledExeUntouched()
    {
        LauncherRelease published = PublishLauncher("1.2.0", "launcher 1.2.0");
        File.WriteAllText(RepoLayout.LocalPath(_repo, RepoLayout.ObjectKey(published.Sha256)), "launcher 6.6.6");

        LauncherUpdater updater = Updater();
        LauncherRelease release = (await updater.CheckAsync(CancellationToken.None))!;
        await Assert.ThrowsAsync<PatchTrustException>(() => updater.DownloadAsync(release, _installed, _ => { }, CancellationToken.None));

        Assert.Equal("launcher 1.1.0", File.ReadAllText(_installed));
        Assert.False(File.Exists(_installed + ".new"));
    }

    [Fact]
    public async Task UnknownSigningKey_IsRejected()
    {
        PublishLauncher("1.2.0", "launcher 1.2.0");
        (_, string otherPublic) = ReleaseSigner.GenerateKeyPair();
        var wrongTrust = new Dictionary<string, string> { [KEY_ID] = otherPublic };

        await Assert.ThrowsAsync<PatchTrustException>(() => Updater(trusted: wrongTrust).CheckAsync(CancellationToken.None));
    }

    [Fact]
    public void Publisher_RefusesDowngradeAndSameVersionDifferentExe()
    {
        PublishLauncher("1.2.0", "launcher 1.2.0");
        Assert.Throws<PatchFormatException>(() => PublishLauncher("1.1.5", "launcher 1.1.5"));
        Assert.Throws<PatchFormatException>(() => PublishLauncher("1.2.0", "launcher 1.2.0 rebuilt"));
        PublishLauncher("1.2.0", "launcher 1.2.0"); // 같은 exe 재게시는 허용
    }

    [Fact]
    public void Swap_RestoresOriginal_WhenNewFileIsMissing()
    {
        Assert.ThrowsAny<IOException>(() => LauncherUpdater.Swap(_installed, _installed + ".new"));
        Assert.Equal("launcher 1.1.0", File.ReadAllText(_installed));
    }
}
