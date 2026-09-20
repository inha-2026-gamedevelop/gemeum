namespace Geumeum.Patch.Client;

/// 지금 실행할 버전을 가리키는 작은 파일. 이 파일 하나를 바꾸는 것이 "업데이트 적용"이다.
public sealed class ActivePointer
{
    public string         ReleaseId      { get; set; } = "";
    public string         ManifestSha256 { get; set; } = "";
    public string         Channel        { get; set; } = "";
    public DateTimeOffset ActivatedAtUtc { get; set; }
}

/// 진행 중인 설치. 강제 종료 뒤 어디까지 했는지 복구에 쓴다.
public sealed class TransactionRecord
{
    /// staging을 채우는 중. 재시작하면 같은 목표일 때 이어서 한다
    public const string PREPARING = "preparing";

    /// 새 버전 폴더가 완성됐고 active 교체만 남았다
    public const string PREPARED = "prepared";

    public string Id                   { get; set; } = "";
    public string Phase                { get; set; } = PREPARING;
    public string Channel              { get; set; } = "";
    public string TargetReleaseId      { get; set; } = "";
    public string TargetManifestSha256 { get; set; } = "";
}

/// 채널별로 마지막에 받아들인 sequence. 오래된 서명 문서로 되돌리는 공격을 거른다.
public sealed class ChannelMemory
{
    public string Channel           { get; set; } = "";
    public long   LastSequence      { get; set; }
    public string LastPayloadSha256 { get; set; } = "";
}

internal static class StateFile
{
    public const int MAX_BYTES = 64 * 1024;

    public static T? TryRead<T>(string path) where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }
        return StrictJson.Deserialize<T>(File.ReadAllBytes(path), MAX_BYTES);
    }

    public static void Write<T>(string path, T value)
    {
        AtomicFile.Write(path, StrictJson.SerializeToUtf8Bytes(value));
    }
}

internal static class AtomicFile
{
    /// 임시 파일을 디스크까지 flush한 뒤 이름을 바꾼다. 중간에 꺼져도 이전 내용 또는 새 내용 중 하나만 남는다.
    public static void Write(string path, byte[] bytes)
    {
        string temp = WriteTemp(path, bytes);
        File.Move(temp, path, true);
    }

    /// 기존 파일을 backupPath로 남기면서 교체한다. 기존 파일이 없으면 그냥 옮긴다(최초 설치).
    public static void ReplaceWithBackup(string path, string backupPath, byte[] bytes)
    {
        string temp = WriteTemp(path, bytes);
        if (File.Exists(path))
        {
            File.Replace(temp, path, backupPath, true);
        }
        else
        {
            File.Move(temp, path);
        }
    }

    private static string WriteTemp(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(true);
        }
        return temp;
    }
}

/// 설치 경로 단위 배타 잠금. 런처 두 개가 같은 설치를 동시에 고치지 못하게 한다.
public sealed class InstallLock : IDisposable
{
    private readonly FileStream _stream;

    private InstallLock(FileStream stream)
    {
        _stream = stream;
    }

    public static InstallLock Acquire(InstallLayout layout)
    {
        Directory.CreateDirectory(layout.StateDir);
        try
        {
            return new InstallLock(new FileStream(layout.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException e)
        {
            throw new InstallBusyException("다른 런처가 이 설치 폴더를 사용하고 있다", e);
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}
