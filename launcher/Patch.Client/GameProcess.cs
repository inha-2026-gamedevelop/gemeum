using System.ComponentModel;
using System.Diagnostics;

namespace Geumeum.Patch.Client;

/// 게임 실행과 실행 중 확인. 런처 밖에서 직접 켠 게임도 경로로 찾아낸다(설계서 9장).
public static class GameProcess
{
    public const string DEFAULT_PROCESS_NAME = "gemeum";

    /// 이 설치의 versions 아래에서 실행 중인 버전 폴더 이름들.
    public static HashSet<string> RunningReleaseIds(InstallLayout layout, string processName = DEFAULT_PROCESS_NAME)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string prefix = layout.VersionsDir + Path.DirectorySeparatorChar;

        foreach (Process process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                string? path;
                try
                {
                    path = process.MainModule?.FileName;
                }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException)
                {
                    // 권한이 없어 경로를 못 읽는 프로세스는 우리가 띄운 게임일 수 있다. 안전하게 "실행 중"으로 본다
                    result.Add("?");
                    continue;
                }

                if (path != null && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(path[prefix.Length..].Split(Path.DirectorySeparatorChar)[0]);
                }
            }
        }
        return result;
    }

    public static bool IsRunning(InstallLayout layout, string processName = DEFAULT_PROCESS_NAME)
    {
        return RunningReleaseIds(layout, processName).Count > 0;
    }

    /// 검증된 manifest의 entryPoint만 실행한다. 명령줄은 런처 코드가 정한 값만 쓰고 원격 문서에서 받지 않는다.
    public static Process Launch(InstallLayout layout, string releaseId, ReleaseManifest manifest, IReadOnlyList<string> arguments)
    {
        string versionDir = layout.VersionDir(releaseId);
        string exe = PathGuard.Resolve(versionDir, manifest.EntryPoint);
        ManifestFile entry = manifest.Files.Single(f => f.Path == manifest.EntryPoint);

        var info = new FileInfo(exe);
        if (!info.Exists || info.Length != entry.Size)
        {
            throw new InstallBrokenException($"실행 파일이 없거나 손상됐다: {exe}. 설정에서 파일 검사·복구를 실행한다");
        }

        var start = new ProcessStartInfo(exe)
        {
            WorkingDirectory = versionDir,
            UseShellExecute  = false,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        return Process.Start(start) ?? throw new InvalidOperationException($"게임을 시작하지 못했다: {exe}");
    }
}

/// 날짜별 로그 파일. 오류 안내에 "로그 폴더 열기"로 이어진다.
public sealed class PatchLog
{
    private readonly object  _gate = new();
    private readonly string? _directory;

    public PatchLog(string? directory)
    {
        _directory = directory;
    }

    public event Action<string>? Written;

    public void Info(string message)  => Write("INFO", message);
    public void Warn(string message)  => Write("WARN", message);
    public void Error(string message, Exception? e = null) => Write("ERROR", e == null ? message : $"{message}\n{e}");

    private void Write(string level, string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (_gate)
        {
            if (_directory != null)
            {
                try
                {
                    Directory.CreateDirectory(_directory);
                    File.AppendAllText(Path.Combine(_directory, $"launcher-{DateTime.Now:yyyyMMdd}.log"), line + Environment.NewLine);
                }
                catch (IOException e)
                {
                    // 로그를 못 쓴다고 설치를 멈추지 않는다. 디버거 출력에는 남긴다
                    Trace.WriteLine($"[PatchLog] 로그 기록 실패: {e.Message}\n{line}");
                }
            }
        }
        Written?.Invoke(line);
    }
}
