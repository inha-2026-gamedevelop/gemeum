namespace Geumeum.Patch;

/// <summary>
/// 게임 빌드가 자기 배포 정체성을 담는 파일. Unity 빌드 스크립트가 쓰고 패키징 도구가 manifest와 대조한다
/// </summary>
public sealed class BuildIdentity
{
    public const string PLATFORM_WIN_X64  = "win-x64";
    public const string RELATIVE_FILE_PATH = "gemeum_Data/StreamingAssets/build-identity.json";
    public const int    MAX_BYTES          = 4 * 1024;

    public string ReleaseId       { get; set; } = "";
    public string DisplayVersion  { get; set; } = "";
    public int    ProtocolVersion { get; set; }
    public string ContentVersion  { get; set; } = "";

    public static BuildIdentity Parse(ReadOnlySpan<byte> utf8)
    {
        BuildIdentity identity = StrictJson.Deserialize<BuildIdentity>(utf8, MAX_BYTES);
        if (!ManifestValidator.IsValidReleaseId(identity.ReleaseId))
        {
            throw new PatchFormatException($"build-identity releaseId 형식 오류: '{identity.ReleaseId}'");
        }
        return identity;
    }

    public byte[] ToUtf8Bytes() => StrictJson.SerializeToUtf8Bytes(this);

    /// <summary>
    /// 게임 내장값과 manifest가 어긋나면 사유 목록, 같으면 빈 목록
    /// </summary>
    public List<string> Mismatches(ReleaseManifest manifest)
    {
        var errors = new List<string>();
        if (!string.Equals(ReleaseId, manifest.ReleaseId, StringComparison.Ordinal))
            errors.Add($"releaseId: 게임={ReleaseId}, manifest={manifest.ReleaseId}");
        if (!string.Equals(DisplayVersion, manifest.DisplayVersion, StringComparison.Ordinal))
            errors.Add($"displayVersion: 게임={DisplayVersion}, manifest={manifest.DisplayVersion}");
        if (ProtocolVersion != manifest.ProtocolVersion)
            errors.Add($"protocolVersion: 게임={ProtocolVersion}, manifest={manifest.ProtocolVersion}");
        if (!string.Equals(ContentVersion, manifest.ContentVersion, StringComparison.Ordinal))
            errors.Add($"contentVersion: 게임={ContentVersion}, manifest={manifest.ContentVersion}");
        return errors;
    }
}
