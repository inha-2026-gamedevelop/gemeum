using System.Text;
using Xunit;

namespace Geumeum.Patch.Tests;

/// <summary>
/// P1 통과 기준: 동일 입력 재현, 잘못된 경로, 서명, ID 불일치 거부
/// </summary>
public sealed class PatchCoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "geumeum-patch-tests", Guid.NewGuid().ToString("N"));

    public PatchCoreTests()  { Directory.CreateDirectory(_root); }
    public void Dispose()    { Directory.Delete(_root, true); }

    private string MakeBuild(string name = "build")
    {
        string dir = Path.Combine(_root, name);
        Write(dir, "gemeum.exe", "exe-bytes");
        Write(dir, "gemeum_Data/globalgamemanagers", "ggm");
        Write(dir, "gemeum_Data/StreamingAssets/build-identity.json", Encoding.UTF8.GetString(Identity().ToUtf8Bytes()));
        Write(dir, "steam_appid.txt", "480");
        Write(dir, "tools/run-host.bat", "excluded");
        Write(dir, "gemeum_BurstDebugInformation_DoNotShip/x.pdb", "excluded");
        return dir;
    }

    private static void Write(string dir, string relative, string content)
    {
        string path = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void Packaging_ExcludesProductionSourcesButKeepsRuntimeLibrariesAndData()
    {
        string build = MakeBuild();
        string[] excluded = { "teaser/src/index.jsx", "teaser/node_modules/tool.exe", "Assets/game.cs",
            "ProjectSettings/ProjectSettings.asset", "node_modules/package/index.js", "docs/design.md",
            "gemeum_Data/Managed/Assembly-CSharp.pdb", "game.csproj", "game.slnx", "source.cs" };
        foreach (string path in excluded) Write(build, path, "not-runtime");
        string[] required = { "gemeum_Data/Managed/Assembly-CSharp.dll", "gemeum_Data/StreamingAssets/settings.json",
            "gemeum_Data/Plugins/x86_64/steam_api64.dll", "MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll" };
        foreach (string path in required) Write(build, path, "runtime");
        string[] paths = ManifestBuilder.Build(build, Options()).Files.Select(f => f.Path).ToArray();
        foreach (string path in excluded) Assert.DoesNotContain(path, paths);
        foreach (string path in required) Assert.Contains(path, paths);
        Assert.Contains("steam_appid.txt", paths);
    }

    private static BuildIdentity Identity() => new()
    {
        ReleaseId = "win64-20260919-001", DisplayVersion = "0.4.1", ProtocolVersion = 1, ContentVersion = "c1",
    };

    private static ManifestBuilder.Options Options() => new()
    {
        ReleaseId = "win64-20260919-001", DisplayVersion = "0.4.1", ProtocolVersion = 1, ContentVersion = "c1",
    };

    [Fact]
    public void SameInput_ProducesIdenticalBytes_AndExcludesRuleTargets()
    {
        string build = MakeBuild();
        byte[] first  = ManifestBuilder.Build(build, Options()).ToUtf8Bytes();
        byte[] second = ManifestBuilder.Build(build, Options()).ToUtf8Bytes();

        Assert.Equal(first, second);

        ReleaseManifest manifest = ReleaseManifest.Parse(first);
        string[] paths = manifest.Files.Select(f => f.Path).ToArray();
        Assert.Contains("steam_appid.txt", paths);
        Assert.Contains(BuildIdentity.RELATIVE_FILE_PATH, paths);
        Assert.DoesNotContain(paths, p => p.StartsWith("tools/"));
        Assert.DoesNotContain(paths, p => p.Contains("DoNotShip"));
        Assert.Equal(paths.OrderBy(p => p, StringComparer.Ordinal), paths);
    }

    [Fact]
    public void Identity_MatchesManifest_AndDetectsMismatch()
    {
        ReleaseManifest manifest = ManifestBuilder.Build(MakeBuild(), Options());
        Assert.Empty(Identity().Mismatches(manifest));

        BuildIdentity other = Identity();
        other.ReleaseId = "win64-20260919-002";
        Assert.Single(other.Mismatches(manifest));
    }

    // Unity JsonUtility.ToJson(BuildIdentity.Data)가 내는 모양 그대로. 한쪽 필드 이름이 바뀌면 여기서 깨진다
    [Fact]
    public void Identity_ParsesUnityJsonUtilityOutput()
    {
        const string unity = """{"releaseId":"win64-20260919-1432","displayVersion":"0.1","protocolVersion":1,"contentVersion":"a1b2c3d4e5f6-dirty"}""";
        BuildIdentity identity = BuildIdentity.Parse(Encoding.UTF8.GetBytes(unity));
        Assert.Equal("win64-20260919-1432", identity.ReleaseId);
        Assert.Equal("a1b2c3d4e5f6-dirty", identity.ContentVersion);
    }

    [Theory]
    [InlineData("editor")]
    [InlineData("unknown")]
    [InlineData("dev")]
    [InlineData("win64-20260919-14321")]
    public void Identity_RejectsNonReleaseIds(string releaseId)
    {
        string json = $$"""{"releaseId":"{{releaseId}}","displayVersion":"0.1","protocolVersion":1,"contentVersion":"c"}""";
        Assert.Throws<PatchFormatException>(() => BuildIdentity.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [InlineData("../escape.dll")]
    [InlineData("C:/abs.dll")]
    [InlineData("/abs.dll")]
    [InlineData("a\\b.dll")]
    [InlineData("data:stream.dll")]
    [InlineData("CON.dll")]
    [InlineData("trailing. ")]
    [InlineData("a//b")]
    public void RelativePath_RejectsEscapes(string path)
    {
        Assert.NotNull(RelativePath.Validate(path));
    }

    [Fact]
    public void RelativePath_FindsCaseAndFileDirectoryCollisions()
    {
        List<string> errors = RelativePath.FindCollisions(new[] { "Data/a.bin", "data/A.bin", "Data", "x/y", "x/y" });
        Assert.Equal(3, errors.Count);
    }

    [Fact]
    public void Manifest_RejectsDuplicateJsonField()
    {
        string json = """{"schemaVersion":1,"schemaVersion":2,"releaseId":"win64-20260919-001"}""";
        Assert.Throws<PatchFormatException>(() => ReleaseManifest.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Manifest_RejectsUnknownField_AndBadEntryPoint()
    {
        ReleaseManifest manifest = ManifestBuilder.Build(MakeBuild(), Options());
        manifest.EntryPoint = "missing.exe";
        Assert.Contains(ManifestValidator.Validate(manifest), e => e.Contains("entryPoint"));

        string json = Encoding.UTF8.GetString(ManifestBuilder.Build(MakeBuild("b2"), Options()).ToUtf8Bytes())
            .Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"cmd\":\"calc.exe\"");
        Assert.Throws<PatchFormatException>(() => ReleaseManifest.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Envelope_RoundTrips_AndRejectsTampering()
    {
        (string priv, string pub) = ReleaseSigner.GenerateKeyPair();
        var trusted = new Dictionary<string, string> { ["k1"] = pub };
        byte[] manifestBytes = ManifestBuilder.Build(MakeBuild(), Options()).ToUtf8Bytes();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        ChannelPayload payload = Payload(manifestBytes, now);
        SignedEnvelope envelope = SignedEnvelope.Create("k1", priv, payload);

        ChannelPayload opened = SignedEnvelope.Parse(envelope.ToUtf8Bytes()).VerifyAndOpen(trusted, now, 3);
        Assert.Equal("win64-20260919-001", opened.OpenManifest(manifestBytes).ReleaseId);

        // payload 한 바이트 변조
        byte[] tampered = Convert.FromBase64String(envelope.PayloadBase64);
        tampered[^1] ^= 1;
        var bad = new SignedEnvelope { KeyId = "k1", PayloadBase64 = Convert.ToBase64String(tampered), SignatureBase64 = envelope.SignatureBase64 };
        Assert.Throws<PatchTrustException>(() => bad.VerifyAndOpen(trusted, now, null));

        // 모르는 키
        var unknown = new SignedEnvelope { KeyId = "k2", PayloadBase64 = envelope.PayloadBase64, SignatureBase64 = envelope.SignatureBase64 };
        Assert.Throws<PatchTrustException>(() => unknown.VerifyAndOpen(trusted, now, null));

        // 다른 키로 서명
        (string otherPriv, _) = ReleaseSigner.GenerateKeyPair();
        SignedEnvelope forged = SignedEnvelope.Create("k1", otherPriv, payload);
        Assert.Throws<PatchTrustException>(() => forged.VerifyAndOpen(trusted, now, null));

        // sequence 후퇴, 만료, 미래 발행
        Assert.Throws<PatchTrustException>(() => envelope.VerifyAndOpen(trusted, now, 6));
        Assert.Throws<PatchTrustException>(() => envelope.VerifyAndOpen(trusted, now.AddDays(200), null));
        Assert.Throws<PatchClockException>(() => envelope.VerifyAndOpen(trusted, now.AddDays(-1), null));

        // manifest 바꿔치기
        byte[] otherManifest = ManifestBuilder.Build(MakeBuild("b3"), new ManifestBuilder.Options
        {
            ReleaseId = "win64-20260919-002", DisplayVersion = "0.4.2", ContentVersion = "c2",
        }).ToUtf8Bytes();
        Assert.Throws<PatchTrustException>(() => opened.OpenManifest(otherManifest));
    }

    private static ChannelPayload Payload(byte[] manifestBytes, DateTimeOffset now) => new()
    {
        Channel = "internal", Platform = BuildIdentity.PLATFORM_WIN_X64, Sequence = 5,
        IssuedAtUtc = now, ExpiresAtUtc = now + ChannelPayload.DEFAULT_LIFETIME,
        ReleaseId = "win64-20260919-001", ManifestSha256 = Sha256Hex.OfBytes(manifestBytes),
        ManifestSize = manifestBytes.Length, MinLauncherVersion = "1.0.0",
    };
}
