using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace Geumeum.Launcher;

public enum SteamLoginState { NotRunning, SignedOut, SignedIn, Unknown }

/// <summary>
/// 로컬 Steam 세션만 확인한다. 온라인 서비스 연결 여부는 게임의 Steamworks API가 판정한다
/// </summary>
public static class SteamLoginStatus
{
    public static SteamLoginState Evaluate(bool running, int? activeUser, bool sessionProcessMatches)
    {
        if (!running) return SteamLoginState.NotRunning;
        if (!sessionProcessMatches || activeUser == null) return SteamLoginState.Unknown;
        return activeUser == 0 ? SteamLoginState.SignedOut : SteamLoginState.SignedIn;
    }

    [SupportedOSPlatform("windows")]
    public static SteamLoginState Read()
    {
        Process[] processes = Process.GetProcessesByName("steam");
        try
        {
            if (processes.Length == 0) return SteamLoginState.NotRunning;
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
            int? user = key?.GetValue("ActiveUser") as int?;
            int? pid = key?.GetValue("pid") as int?;
            using Process current = Process.GetCurrentProcess();
            bool matches = processes.Any(p => p.Id == pid && p.SessionId == current.SessionId);
            return Evaluate(true, user, matches);
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException
                                 or System.IO.IOException or InvalidOperationException
                                 or System.ComponentModel.Win32Exception)
        {
            Trace.WriteLine($"Steam 세션 확인 실패: {e.GetType().Name}");
            return SteamLoginState.Unknown;
        }
        finally
        {
            foreach (Process process in processes) process.Dispose();
        }
    }
}
