namespace Geumeum.Patch;

/// <summary>
/// manifest 안의 파일 경로 규칙. - 설치 루트 밖으로 쓰기가 나가지 않게 막는다
/// </summary>
public static class RelativePath
{
    public const int MAX_LENGTH = 240;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// 통과하면 null, 아니면 거부 사유를 돌려준다. manifest 경로는 항상 '/' 구분자다
    /// </summary>
    public static string? Validate(string path)
    {
        if (string.IsNullOrEmpty(path))              return "빈 경로";
        if (path.Length > MAX_LENGTH)                return $"경로가 {MAX_LENGTH}자를 넘는다";
        if (path.Contains('\\'))                     return "역슬래시 구분자";
        if (path.StartsWith('/'))                    return "절대 경로";
        if (path.Contains(':'))                      return "드라이브 접두사 또는 ADS";
        if (path.Contains('\0'))                     return "NUL 문자";

        foreach (string segment in path.Split('/'))
        {
            if (segment.Length == 0)                              return "빈 세그먼트(//)";
            if (segment == "." || segment == "..")                return "상대 참조(. 또는 ..)";
            if (segment.EndsWith(' ') || segment.EndsWith('.'))   return "끝의 공백·점";
            if (segment.Any(c => c < 0x20 || c == '<' || c == '>' || c == '"' || c == '|' || c == '?' || c == '*'))
            {
                return "Windows 금지 문자";
            }

            string stem = segment.Split('.')[0];
            if (ReservedNames.Contains(stem))                     return $"예약 장치명 {stem}";
        }

        return null;
    }

    /// <summary>
    /// 대소문자 충돌과 파일, 디렉터리 충돌을 목록 전체에서 찾는다
    /// </summary>
    public static List<string> FindCollisions(IEnumerable<string> paths)
    {
        var errors      = new List<string>();
        var seenLower   = new Dictionary<string, string>(StringComparer.Ordinal);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files       = new List<string>();

        foreach (string path in paths)
        {
            string lower = path.ToLowerInvariant();
            if (seenLower.TryGetValue(lower, out string? existing))
            {
                errors.Add(existing == path ? $"중복 경로: {path}" : $"대소문자 충돌: {existing} / {path}");
                continue;
            }
            seenLower[lower] = path;
            files.Add(path);

            int slash = path.LastIndexOf('/');
            while (slash > 0)
            {
                directories.Add(path[..slash]);
                slash = path.LastIndexOf('/', slash - 1);
            }
        }

        foreach (string file in files)
        {
            if (directories.Contains(file))
            {
                errors.Add($"파일과 디렉터리 충돌: {file}");
            }
        }

        return errors;
    }
}
