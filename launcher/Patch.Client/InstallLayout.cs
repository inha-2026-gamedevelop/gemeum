namespace Geumeum.Patch.Client;

/// 설치 루트 아래 폴더 구조(설계서 7장). staging·versions·state는 같은 볼륨에 있어야 폴더 이동이 원자적이다.
public sealed class InstallLayout
{
    public InstallLayout(string root)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    public string Root { get; }

    public string StateDir         => Path.Combine(Root, "state");
    public string ActivePath       => Path.Combine(StateDir, "active.json");
    public string ActiveBackupPath => Path.Combine(StateDir, "active.backup.json");
    public string TransactionPath  => Path.Combine(StateDir, "transaction.json");
    public string LockPath         => Path.Combine(StateDir, "install.lock");
    public string ManifestsDir     => Path.Combine(StateDir, "manifests");
    public string VersionsDir      => Path.Combine(Root, "versions");
    public string StagingDir       => Path.Combine(Root, "staging");
    public string CacheDir         => Path.Combine(Root, "cache");
    public string LogsDir          => Path.Combine(Root, "logs");

    public string ChannelMemoryPath(string channel) => Path.Combine(StateDir, $"channel-{channel}.json");
    public string ManifestPath(string releaseId)    => Path.Combine(ManifestsDir, releaseId + ".json");
    public string VersionDir(string releaseId)      => Path.Combine(VersionsDir, releaseId);
    public string StagingFor(string transactionId)  => Path.Combine(StagingDir, transactionId);
    public string PartPath(string sha256)           => Path.Combine(CacheDir, sha256 + ".part");

    public IEnumerable<string> OwnedDirectories()
    {
        yield return StateDir;
        yield return ManifestsDir;
        yield return VersionsDir;
        yield return StagingDir;
        yield return CacheDir;
        yield return LogsDir;
    }
}

/// manifest 경로를 실제 경로로 바꾸는 유일한 통로. 설치 루트 밖으로 나가는 쓰기를 막는다.
public static class PathGuard
{
    public static string Resolve(string baseDir, string manifestPath)
    {
        string? error = RelativePath.Validate(manifestPath);
        if (error != null)
        {
            throw new PatchFormatException($"경로 거부 '{manifestPath}': {error}");
        }

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDir)) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(root, manifestPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new PatchFormatException($"설치 폴더 밖 경로: {manifestPath}");
        }
        return full;
    }

    public static bool IsInside(string root, string path)
    {
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// 트리 안에 심볼릭 링크·정션이 있으면 거부한다. 링크를 따라 설치 루트 밖을 쓰거나 지우는 것을 막는다.
    public static void EnsureNoReparsePoints(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        if (new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new PatchFormatException($"링크 폴더는 허용하지 않는다: {directory}");
        }

        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false };
        foreach (FileSystemInfo entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", options))
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new PatchFormatException($"링크는 허용하지 않는다: {entry.FullName}");
            }
        }
    }

    /// 링크를 따라가지 않고 지운다. 설치 루트 밖이면 아무것도 하지 않고 예외.
    public static void DeleteTree(string root, string directory)
    {
        if (!IsInside(root, directory))
        {
            throw new InvalidOperationException($"설치 루트 밖은 지우지 않는다: {directory}");
        }
        if (!Directory.Exists(directory))
        {
            return;
        }

        var info = new DirectoryInfo(directory);
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            info.Delete(false);
            return;
        }

        foreach (FileInfo file in info.EnumerateFiles("*", new EnumerationOptions { AttributesToSkip = 0 }))
        {
            file.Attributes = FileAttributes.Normal;
            file.Delete();
        }
        foreach (DirectoryInfo child in info.EnumerateDirectories("*", new EnumerationOptions { AttributesToSkip = 0 }))
        {
            DeleteTree(root, child.FullName);
        }
        info.Delete(false);
    }
}

/// 1차 지원 범위는 로컬 NTFS 고정 디스크다(설계서 7장).
public static class LocalVolume
{
    public static string? Problem(string root)
    {
        if (root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return "네트워크 경로에는 설치할 수 없다";
        }

        string? driveRoot = Path.GetPathRoot(Path.GetFullPath(root));
        if (string.IsNullOrEmpty(driveRoot))
        {
            return "드라이브를 알 수 없다";
        }

        var drive = new DriveInfo(driveRoot);
        if (!drive.IsReady)                 return $"{driveRoot} 드라이브를 사용할 수 없다";
        if (drive.DriveType != DriveType.Fixed) return $"{driveRoot}는 고정 디스크가 아니다({drive.DriveType})";
        if (!string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            return $"{driveRoot}는 NTFS가 아니다({drive.DriveFormat})";

        string system = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if ((system.Length > 0 && PathGuard.IsInside(system, root)) || (programFiles.Length > 0 && PathGuard.IsInside(programFiles, root)))
        {
            return "시스템 폴더에는 설치하지 않는다. 관리자 권한 없이 쓸 수 있는 경로를 고른다";
        }
        return null;
    }

    public static long AvailableBytes(string root)
    {
        return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!).AvailableFreeSpace;
    }
}
