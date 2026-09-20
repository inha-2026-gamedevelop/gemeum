namespace Geumeum.Patch.Client;

/// 화면에 사용자 조치를 안내하려고 오류를 종류별로 나눈다(설계서 8장 표).
public sealed class InstallBusyException : Exception
{
    public InstallBusyException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class GameRunningException : Exception
{
    public GameRunningException() : base("게임이 실행 중이라 설치·전환을 미뤘다. 게임을 끄고 다시 시도한다") { }
}

public sealed class LauncherTooOldException : Exception
{
    public LauncherTooOldException(Version required, Version current)
        : base($"런처를 새로 받아야 한다: 필요 {required}, 현재 {current}")
    {
        Required = required;
    }

    public Version Required { get; }
}

public sealed class DiskSpaceException : Exception
{
    public DiskSpaceException(long needed, long available)
        : base($"디스크 공간이 부족하다: 필요 {Format.Bytes(needed)}, 남은 공간 {Format.Bytes(available)}")
    {
        Needed    = needed;
        Available = available;
    }

    public long Needed    { get; }
    public long Available { get; }
}

public sealed class InstallBrokenException : Exception
{
    public InstallBrokenException(string message) : base(message) { }
}

/// 네트워크 404. 배포가 불완전하다는 뜻이라 재시도하지 않는다.
public sealed class RemoteNotFoundException : Exception
{
    public RemoteNotFoundException(string key) : base($"배포 서버에 없다: {key}") { }
}

/// 재시도할 가치가 있는 원격 오류(5xx·연결 끊김·무응답).
public sealed class RemoteTransientException : Exception
{
    public RemoteTransientException(string message, Exception? inner = null) : base(message, inner) { }
}

/// 재시도할 가치가 없는 원격 오류(4xx·응답 형식 위반).
public sealed class RemoteProtocolException : Exception
{
    public RemoteProtocolException(string message) : base(message) { }
}

public static class Format
{
    public static string Bytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (Math.Abs(value) >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            ++unit;
        }
        return unit == 0 ? $"{bytes} B" : $"{value:0.0} {units[unit]}";
    }
}
