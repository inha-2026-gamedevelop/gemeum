namespace Geumeum.Patch;

/// Unity 빌드 폴더를 훑어 manifest를 만든다. 제외는 명시 규칙으로만 하고, 나머지는 전부 넣는다.
public static class ManifestBuilder
{
    /// 빌드 폴더에 있어도 배포하면 안 되는 것. Development 빌드의 실행 스크립트와 Unity가 "배포하지 말라"고 이름 붙인 폴더.
    public static readonly string[] DefaultExcludeDirectories =
    {
        "gemeum_BackUpThisFolder_ButDontShipItWithYourGame",
        "gemeum_BurstDebugInformation_DoNotShip",
        "tools",
        "logs",
        "teaser", "node_modules", ".git", ".vs", "obj",
        "Assets", "Packages", "ProjectSettings", "Library", "Temp", "docs",
    };

    public static readonly string[] DefaultExcludeFileSuffixes =
    {
        ".bat", ".ps1", ".log", ".pem", ".key", ".pfx",
        ".cs", ".csproj", ".sln", ".slnx", ".meta", ".pdb", ".mdb",
    };

    public sealed class Options
    {
        public string   ReleaseId       { get; set; } = "";
        public string   DisplayVersion  { get; set; } = "";
        public int      ProtocolVersion { get; set; } = 1;
        public string   ContentVersion  { get; set; } = "";
        public string   EntryPoint      { get; set; } = "gemeum.exe";
        public string[] ExcludeDirectories  { get; set; } = DefaultExcludeDirectories;
        public string[] ExcludeFileSuffixes { get; set; } = DefaultExcludeFileSuffixes;
    }

    public static ReleaseManifest Build(string buildDirectory, Options options, Action<string>? onFile = null)
    {
        string root = Path.GetFullPath(buildDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"빌드 폴더가 없다: {root}");
        }

        var manifest = new ReleaseManifest
        {
            ReleaseId       = options.ReleaseId,
            DisplayVersion  = options.DisplayVersion,
            Platform        = BuildIdentity.PLATFORM_WIN_X64,
            ProtocolVersion = options.ProtocolVersion,
            ContentVersion  = options.ContentVersion,
            EntryPoint      = options.EntryPoint,
        };

        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories    = true,
            AttributesToSkip         = FileAttributes.ReparsePoint,
            IgnoreInaccessible       = false,
            ReturnSpecialDirectories = false,
        };

        foreach (string absolute in Directory.EnumerateFiles(root, "*", enumeration))
        {
            string relative = Path.GetRelativePath(root, absolute).Replace('\\', '/');
            if (IsExcluded(relative, options))
            {
                continue;
            }

            string? pathError = RelativePath.Validate(relative);
            if (pathError != null)
            {
                throw new PatchFormatException($"빌드 폴더의 경로가 manifest 규칙에 걸린다 '{relative}': {pathError}");
            }

            onFile?.Invoke(relative);
            manifest.Files.Add(new ManifestFile
            {
                Path   = relative,
                Size   = new FileInfo(absolute).Length,
                Sha256 = Sha256Hex.OfFile(absolute),
            });
        }

        manifest.SortFiles();

        List<string> errors = ManifestValidator.Validate(manifest);
        if (errors.Count > 0)
        {
            throw new PatchFormatException("생성한 manifest가 검증에 걸린다:\n  " + string.Join("\n  ", errors));
        }

        return manifest;
    }

    private static bool IsExcluded(string relative, Options options)
    {
        string firstSegment = relative.Split('/')[0];
        if (options.ExcludeDirectories.Any(d => string.Equals(d, firstSegment, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
        return options.ExcludeFileSuffixes.Any(s => relative.EndsWith(s, StringComparison.OrdinalIgnoreCase));
    }
}
