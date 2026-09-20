using System.Text.RegularExpressions;

namespace Geumeum.Patch;

public static partial class ManifestValidator
{
    // 예: win64-20260919-1432 (Unity 빌드 메뉴가 빌드 시각으로 찍는다). 재사용 금지는 게시 측 책임이고 여기서는 형식만 본다
    [GeneratedRegex("^[a-z0-9]+-[0-9]{8}-[0-9]{3,4}$")]
    private static partial Regex ReleaseIdPattern();

    public static bool IsValidReleaseId(string? id) => id != null && ReleaseIdPattern().IsMatch(id);

    public static List<string> Validate(ReleaseManifest m)
    {
        var errors = new List<string>();

        if (m.SchemaVersion != ReleaseManifest.CURRENT_SCHEMA_VERSION)
            errors.Add($"미지원 schemaVersion {m.SchemaVersion}");
        if (!IsValidReleaseId(m.ReleaseId))
            errors.Add($"releaseId 형식 오류: '{m.ReleaseId}'");
        if (string.IsNullOrWhiteSpace(m.DisplayVersion))
            errors.Add("displayVersion 없음");
        if (m.Platform != BuildIdentity.PLATFORM_WIN_X64)
            errors.Add($"미지원 platform '{m.Platform}'");
        if (m.ProtocolVersion <= 0)
            errors.Add("protocolVersion은 1 이상");
        if (string.IsNullOrWhiteSpace(m.ContentVersion))
            errors.Add("contentVersion 없음");
        if (m.Files.Count == 0)
            errors.Add("파일 목록이 비었다");
        if (m.Files.Count > ManifestLimits.MAX_FILE_COUNT)
            errors.Add($"파일 개수 상한 초과: {m.Files.Count}");

        long total = 0;
        foreach (ManifestFile file in m.Files)
        {
            string? pathError = RelativePath.Validate(file.Path);
            if (pathError != null)
                errors.Add($"경로 거부 '{file.Path}': {pathError}");
            if (file.Size < 0 || file.Size > ManifestLimits.MAX_FILE_SIZE)
                errors.Add($"파일 크기 범위 밖 '{file.Path}': {file.Size}");
            if (!Sha256Hex.IsValid(file.Sha256))
                errors.Add($"sha256 형식 오류 '{file.Path}'");

            total = checked(total + Math.Max(0, file.Size));
        }

        if (total > ManifestLimits.MAX_TOTAL_SIZE)
            errors.Add($"전체 설치 크기 상한 초과: {total}");

        errors.AddRange(RelativePath.FindCollisions(m.Files.Select(f => f.Path)));

        if (RelativePath.Validate(m.EntryPoint) != null)
            errors.Add($"entryPoint 경로 오류 '{m.EntryPoint}'");
        else if (!m.EntryPoint.EndsWith(".exe", StringComparison.Ordinal))
            errors.Add("entryPoint는 .exe여야 한다");
        else if (!m.Files.Any(f => string.Equals(f.Path, m.EntryPoint, StringComparison.Ordinal)))
            errors.Add($"entryPoint '{m.EntryPoint}'가 파일 목록에 없다");

        return errors;
    }
}
