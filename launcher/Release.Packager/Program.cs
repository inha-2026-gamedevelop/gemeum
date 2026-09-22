using Geumeum.Patch;
using Geumeum.Patch.Client;

namespace Geumeum.Packager;

/// <summary>
/// 게시 순서의 도구. 객체, manifest는 불변 경로에, 채널 envelope만 교체한다
/// </summary>
internal static class Program
{
    private const string USAGE = """
        geumeum-pack <command> [options]

          keygen          --out-dir <dir> --key-id <id>
          pack            --build <unityBuildDir> --out <repoDir> [--notes <file>]
                          (build-identity.json이 없는 옛 빌드만: --release-id <id> --display-version <v> --allow-missing-identity)
          publish-channel --out <repoDir> --channel <stable|internal> --release-id <id> --sequence <n>
                          --key <private.pem> --key-id <id> [--min-launcher <v>] [--lifetime-days <n>]
          verify-channel  --out <repoDir> --channel <stable|internal> --public-key <public.pem> --key-id <id>
          diff            --a <manifest.json> --b <manifest.json>
          serve           --out <repoDir> [--port <n>] [--lan]
          upload          --out <repoDir> --channel <stable|internal>
                          (환경 변수 R2_ACCOUNT_ID · R2_ACCESS_KEY_ID · R2_SECRET_ACCESS_KEY · R2_BUCKET)
          publish-launcher --out <repoDir> --file <GeumeumLauncher.exe> --key <private.pem> --key-id <id>
          upload-launcher --out <repoDir>
          verify-remote   --url <공개 주소> --channel <stable|internal> --public-key <public.pem> --key-id <id>
        """;

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length == 0)
        {
            Console.Error.WriteLine(USAGE);
            return 2;
        }

        try
        {
            var options = ParseOptions(args.Skip(1));
            return args[0] switch
            {
                "keygen"          => KeyGen(options),
                "pack"            => Pack(options),
                "publish-channel" => PublishChannel(options),
                "verify-channel"  => VerifyChannel(options),
                "diff"            => Diff(options),
                "serve"           => Serve(options),
                "upload"          => await UploadAsync(options),
                "publish-launcher" => PublishLauncher(options),
                "upload-launcher" => await UploadLauncherAsync(options),
                "verify-remote"   => await VerifyRemoteAsync(options),
                _                 => Fail($"알 수 없는 명령 '{args[0]}'\n{USAGE}"),
            };
        }
        catch (Exception e) when (e is PatchFormatException or PatchTrustException or PatchClockException or IOException
                                     or ArgumentException or FormatException or RemoteNotFoundException
                                     or RemoteTransientException or RemoteProtocolException or Amazon.Runtime.AmazonServiceException)
        {
            return Fail(e.Message);
        }
    }

    private static int KeyGen(Dictionary<string, string> o)
    {
        string outDir = Require(o, "out-dir");
        string keyId  = Require(o, "key-id");
        Directory.CreateDirectory(outDir);

        string privatePath = Path.Combine(outDir, $"{keyId}.private.pem");
        string publicPath  = Path.Combine(outDir, $"{keyId}.public.pem");
        if (File.Exists(privatePath))
        {
            return Fail($"이미 있는 키를 덮어쓰지 않는다: {privatePath}");
        }

        (string privatePem, string publicPem) = ReleaseSigner.GenerateKeyPair();
        File.WriteAllText(privatePath, privatePem);
        File.WriteAllText(publicPath, publicPem);
        Console.WriteLine($"개인키: {privatePath}\n공개키: {publicPath}\n개인키는 저장소·런처·게임에 넣지 않는다. 공개키는 런처 launcher-config.json에 넣는다.");
        return 0;
    }

    private static int Pack(Dictionary<string, string> o)
    {
        string buildDir = Require(o, "build");
        string outDir   = Require(o, "out");

        // 게임이 내장한 정체성이 원본이다. 명령줄 값은 비었을 때만 쓰고, 주면 대조에서 어긋남이 드러난다
        string identityPath = Path.Combine(buildDir, BuildIdentity.RELATIVE_FILE_PATH.Replace('/', Path.DirectorySeparatorChar));
        BuildIdentity? identity = File.Exists(identityPath) ? BuildIdentity.Parse(File.ReadAllBytes(identityPath)) : null;

        var builderOptions = new ManifestBuilder.Options
        {
            ReleaseId       = o.GetValueOrDefault("release-id")      ?? identity?.ReleaseId      ?? Require(o, "release-id"),
            DisplayVersion  = o.GetValueOrDefault("display-version") ?? identity?.DisplayVersion ?? Require(o, "display-version"),
            ProtocolVersion = o.TryGetValue("protocol", out string? protocol) ? int.Parse(protocol) : identity?.ProtocolVersion ?? 1,
            ContentVersion  = o.GetValueOrDefault("content-version") ?? identity?.ContentVersion ?? Require(o, "release-id"),
        };

        if (identity == null)
        {
            Console.WriteLine("경고: build-identity 없이 패키징한다. 게임의 접속 검사와 releaseId가 연결되지 않는다.");
        }

        RepoPublisher.PackResult result = RepoPublisher.Pack(buildDir, outDir, builderOptions, identity,
            o.ContainsKey("allow-missing-identity"), o.GetValueOrDefault("notes"));

        Console.WriteLine($"{result.Manifest.ReleaseId} ({result.Manifest.DisplayVersion}) 파일 {result.Manifest.Files.Count}개, {result.Manifest.TotalSize / 1048576.0:F1}MB");
        Console.WriteLine($"객체 신규 {result.NewObjects}개 (기존 {result.Manifest.Files.Count - result.NewObjects}개 재사용)\nmanifest: {result.ManifestPath}");
        return 0;
    }

    private static int PublishChannel(Dictionary<string, string> o)
    {
        string channel = Require(o, "channel");
        if (!RepoLayout.IsValidChannel(channel))
        {
            return Fail($"채널은 stable 또는 internal: {channel}");
        }

        ChannelPayload payload = RepoPublisher.PublishChannel(
            Require(o, "out"), channel, Require(o, "release-id"), long.Parse(Require(o, "sequence")),
            Require(o, "key-id"), File.ReadAllText(Require(o, "key")),
            o.GetValueOrDefault("min-launcher", "1.0.0"),
            TimeSpan.FromDays(double.Parse(o.GetValueOrDefault("lifetime-days", ChannelPayload.DEFAULT_LIFETIME.TotalDays.ToString()))),
            DateTimeOffset.UtcNow);

        Console.WriteLine($"{channel} → {payload.ReleaseId} (seq {payload.Sequence}, 만료 {payload.ExpiresAtUtc:u})");
        return 0;
    }

    private static int VerifyChannel(Dictionary<string, string> o)
    {
        string outDir  = Require(o, "out");
        string channel = Require(o, "channel");

        var trusted = new Dictionary<string, string> { [Require(o, "key-id")] = File.ReadAllText(Require(o, "public-key")) };
        SignedEnvelope envelope = SignedEnvelope.Parse(File.ReadAllBytes(RepoLayout.LocalPath(outDir, RepoLayout.EnvelopeKey(channel))));
        ChannelPayload payload  = envelope.VerifyAndOpen(trusted, DateTimeOffset.UtcNow, null);

        byte[] manifestBytes     = File.ReadAllBytes(RepoLayout.LocalPath(outDir, RepoLayout.ManifestKey(payload.ReleaseId)));
        ReleaseManifest manifest = payload.OpenManifest(manifestBytes);

        int missing = manifest.Files.Count(f => !File.Exists(RepoLayout.LocalPath(outDir, RepoLayout.ObjectKey(f.Sha256))));
        Console.WriteLine($"서명 OK · {channel} seq {payload.Sequence} → {manifest.ReleaseId} ({manifest.DisplayVersion}) · 파일 {manifest.Files.Count}개 · 객체 누락 {missing}개");
        return missing == 0 ? 0 : 1;
    }

    // P1 완료 조건의 측정 명령. 같은 소스 빌드 2회를 비교해 파일 단위 패치의 실효를 본다
    private static int Diff(Dictionary<string, string> o)
    {
        ReleaseManifest a = ReleaseManifest.Parse(File.ReadAllBytes(Require(o, "a")));
        ReleaseManifest b = ReleaseManifest.Parse(File.ReadAllBytes(Require(o, "b")));

        var byPathA = a.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        var pathsB  = new HashSet<string>(b.Files.Select(f => f.Path), StringComparer.Ordinal);
        long changedBytes = 0;
        int  changed = 0, added = 0;
        var  changedFiles = new List<(string Line, long Size)>();

        foreach (ManifestFile file in b.Files)
        {
            if (!byPathA.TryGetValue(file.Path, out ManifestFile? old))
            {
                ++added; changedBytes += file.Size; changedFiles.Add(("+ " + file.Path, file.Size));
            }
            else if (!string.Equals(old.Sha256, file.Sha256, StringComparison.Ordinal))
            {
                ++changed; changedBytes += file.Size; changedFiles.Add(("~ " + file.Path, file.Size));
            }
        }
        int removed = a.Files.Count(f => !pathsB.Contains(f.Path));

        Console.WriteLine($"{a.ReleaseId} → {b.ReleaseId}: 변경 {changed} · 추가 {added} · 삭제 {removed} · 전송 {changedBytes / 1048576.0:F1}MB / 전체 {b.TotalSize / 1048576.0:F1}MB");
        foreach ((string line, long size) in changedFiles.OrderByDescending(f => f.Size).Take(40))
        {
            Console.WriteLine($"  {size / 1048576.0,9:F1}MB  {line}");
        }
        return 0;
    }

    private static int Serve(Dictionary<string, string> o)
    {
        string port = o.GetValueOrDefault("port", "8765");
        bool   lan  = o.ContainsKey("lan");
        using var server = new RepoHttpServer(Require(o, "out"), $"http://{(lan ? "0.0.0.0" : "localhost")}:{port}/") { Log = Console.WriteLine };
        server.Start();

        Console.WriteLine($"배포 저장소 서빙: {Path.GetFullPath(Require(o, "out"))}  (Ctrl+C로 종료)");
        Console.WriteLine($"  이 PC:  http://localhost:{port}/");
        if (lan)
        {
            foreach (string address in LocalAddresses())
            {
                Console.WriteLine($"  팀원:   http://{address}:{port}/");
            }
            Console.WriteLine("처음 실행하면 Windows 방화벽 허용 창이 뜬다. 팀원이 다른 네트워크면 '공용 네트워크'도 허용해야 한다.");
        }

        using var done = new ManualResetEventSlim();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.Set(); };
        done.Wait();
        return 0;
    }

    private static IEnumerable<string> LocalAddresses()
    {
        return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a)
                        && !a.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .Select(a => a.ToString())
            .Distinct();
    }

    private static async Task<int> UploadAsync(Dictionary<string, string> o)
    {
        string channel = Require(o, "channel");
        using R2Uploader uploader = R2Uploader.FromEnvironment(Console.WriteLine);
        await uploader.UploadChannelAsync(Require(o, "out"), channel, CancellationToken.None);
        return 0;
    }

    // 런처 자동 업데이트용 서명 문서를 만든다. 버전은 exe의 파일 버전(Launcher.UI.csproj <Version>)에서 읽는다
    private static int PublishLauncher(Dictionary<string, string> o)
    {
        string file = Require(o, "file");
        string? fileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(file).FileVersion;
        if (!Version.TryParse(fileVersion, out Version? version))
        {
            return Fail($"런처 exe에서 버전을 읽지 못했다: {file} ({fileVersion})");
        }

        LauncherRelease release = RepoPublisher.PublishLauncher(Require(o, "out"), file, version,
            Require(o, "key-id"), File.ReadAllText(Require(o, "key")), DateTimeOffset.UtcNow);
        Console.WriteLine($"런처 {release.Version} 게시: {release.Size / 1048576.0:F1}MB, sha256 {release.Sha256[..12]}…");
        return 0;
    }

    private static async Task<int> UploadLauncherAsync(Dictionary<string, string> o)
    {
        using R2Uploader uploader = R2Uploader.FromEnvironment(Console.WriteLine);
        await uploader.UploadLauncherAsync(Require(o, "out"), CancellationToken.None);
        return 0;
    }

    // 게시 순서 4단계: 실제 다운로드 경로에서 서명, 해시가 맞는지 런처와 같은 코드로 확인한다
    private static async Task<int> VerifyRemoteAsync(Dictionary<string, string> o)
    {
        string channel = Require(o, "channel");
        var trusted = new Dictionary<string, string> { [Require(o, "key-id")] = File.ReadAllText(Require(o, "public-key")) };
        var url = new Uri(Require(o, "url"));

        using var remote = new HttpRemoteRepository(url, allowInsecureHttp: url.IsLoopback);
        byte[] envelopeBytes = await remote.GetBytesAsync(RepoLayout.EnvelopeKey(channel), ManifestLimits.MAX_ENVELOPE_BYTES, CancellationToken.None);
        ChannelPayload payload = SignedEnvelope.Parse(envelopeBytes).VerifyAndOpen(trusted, DateTimeOffset.UtcNow, null);

        byte[] manifestBytes = await remote.GetBytesAsync(RepoLayout.ManifestKey(payload.ReleaseId), ManifestLimits.MAX_MANIFEST_BYTES, CancellationToken.None);
        ReleaseManifest manifest = payload.OpenManifest(manifestBytes);
        Console.WriteLine($"서명 OK · {channel} seq {payload.Sequence} → {manifest.ReleaseId} ({manifest.DisplayVersion}) · 파일 {manifest.Files.Count}개");

        // 작은 객체 몇 개를 실제로 받아 해시를 맞춰 본다. 전부는 런처 설치로 확인한다
        string temp = Path.Combine(Path.GetTempPath(), "geumeum-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            foreach (ManifestFile file in manifest.Files.DistinctBy(f => f.Sha256).OrderBy(f => f.Size).Take(5))
            {
                string part = Path.Combine(temp, file.Sha256);
                if (file.Size == 0)
                {
                    // 이어받기는 0/0을 완료로 보므로 빈 객체도 GET으로 존재, 내용을 확인한다
                    byte[] empty = await remote.GetBytesAsync(RepoLayout.ObjectKey(file.Sha256), 1, CancellationToken.None);
                    await File.WriteAllBytesAsync(part, empty);
                }
                else
                {
                    await remote.DownloadAsync(RepoLayout.ObjectKey(file.Sha256), file.Size, part, _ => { }, CancellationToken.None);
                }
                bool ok = Sha256Hex.OfFile(part) == file.Sha256;
                Console.WriteLine($"  {(ok ? "OK " : "불일치")} {file.Path} ({file.Size}B)");
                if (!ok)
                {
                    return 1;
                }
            }
        }
        finally
        {
            Directory.Delete(temp, true);
        }
        return 0;
    }

    private static Dictionary<string, string> ParseOptions(IEnumerable<string> args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string? key = null;
        foreach (string arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (key != null) result[key] = "";
                key = arg[2..];
            }
            else if (key != null)
            {
                result[key] = arg;
                key = null;
            }
            else
            {
                throw new ArgumentException($"옵션 없이 값이 왔다: {arg}");
            }
        }
        if (key != null) result[key] = "";
        return result;
    }

    private static string Require(Dictionary<string, string> o, string name)
    {
        if (!o.TryGetValue(name, out string? value) || value.Length == 0)
        {
            throw new ArgumentException($"--{name} 가 필요하다");
        }
        return value;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("오류: " + message);
        return 1;
    }
}
