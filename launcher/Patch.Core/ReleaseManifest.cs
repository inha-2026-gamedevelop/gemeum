namespace Geumeum.Patch;

/// <summary>
/// manifest. 파일 목록은 경로 Ordinal 정렬로 고정해 같은 입력이면 같은 바이트가 나오게 한다
/// </summary>
public sealed class ReleaseManifest
{
    public const int CURRENT_SCHEMA_VERSION = 1;

    public int    SchemaVersion   { get; set; } = CURRENT_SCHEMA_VERSION;
    public string ReleaseId       { get; set; } = "";
    public string DisplayVersion  { get; set; } = "";
    public string Platform        { get; set; } = "";
    public int    ProtocolVersion { get; set; }
    public string ContentVersion  { get; set; } = "";
    public string EntryPoint      { get; set; } = "";
    public List<ManifestFile> Files { get; set; } = new();

    public long TotalSize => Files.Sum(f => f.Size);

    public void SortFiles()
    {
        Files.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
    }

    public byte[] ToUtf8Bytes()
    {
        SortFiles();
        return StrictJson.SerializeToUtf8Bytes(this);
    }

    /// <summary>
    /// 바이트를 먼저 해시로 검증한 뒤에만 호출한다. 여기서는 형식만 본다
    /// </summary>
    public static ReleaseManifest Parse(ReadOnlySpan<byte> utf8)
    {
        ReleaseManifest manifest = StrictJson.Deserialize<ReleaseManifest>(utf8, ManifestLimits.MAX_MANIFEST_BYTES);
        List<string> errors = ManifestValidator.Validate(manifest);
        if (errors.Count > 0)
        {
            throw new PatchFormatException("manifest 거부:\n  " + string.Join("\n  ", errors));
        }
        return manifest;
    }
}

public sealed class ManifestFile
{
    public string Path   { get; set; } = "";
    public long   Size   { get; set; }
    public string Sha256 { get; set; } = "";
}

public static class ManifestLimits
{
    public const int  MAX_MANIFEST_BYTES = 16 * 1024 * 1024;
    public const int  MAX_ENVELOPE_BYTES = 64 * 1024;
    public const int  MAX_FILE_COUNT     = 50_000;
    public const long MAX_FILE_SIZE      = 16L * 1024 * 1024 * 1024;
    public const long MAX_TOTAL_SIZE     = 64L * 1024 * 1024 * 1024;
}
