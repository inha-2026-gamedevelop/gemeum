using System.Text.Json;
using System.Text.Json.Serialization;

namespace Geumeum.Patch;

/// 원격 메타데이터를 읽는 유일한 통로. 중복 필드·주석·후행 쉼표를 거부하고 크기 상한을 둔다.
public static class StrictJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = JsonIgnoreCondition.Never,
        WriteIndented               = false,
        NumberHandling              = JsonNumberHandling.Strict,
        ReadCommentHandling         = JsonCommentHandling.Disallow,
        AllowTrailingCommas         = false,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling      = JsonUnmappedMemberHandling.Disallow,
        MaxDepth                    = 8,
    };

    public static byte[] SerializeToUtf8Bytes<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, Options);
    }

    public static T Deserialize<T>(ReadOnlySpan<byte> utf8, int maxBytes) where T : class
    {
        if (utf8.Length > maxBytes)
        {
            throw new PatchFormatException($"메타데이터가 {maxBytes}바이트 상한을 넘는다: {utf8.Length}");
        }

        RejectDuplicateProperties(utf8);

        T? result;
        try
        {
            result = JsonSerializer.Deserialize<T>(utf8, Options);
        }
        catch (JsonException e)
        {
            throw new PatchFormatException($"JSON 해석 실패: {e.Message}", e);
        }

        return result ?? throw new PatchFormatException("JSON 본문이 null이다");
    }

    // System.Text.Json은 중복 키를 마지막 값으로 덮어쓴다. 서명된 문서에서 그 동작은 검증 우회 통로가 되므로 직접 막는다
    private static void RejectDuplicateProperties(ReadOnlySpan<byte> utf8)
    {
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions
        {
            CommentHandling     = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            MaxDepth            = Options.MaxDepth,
        });

        var scopes = new Stack<HashSet<string>?>();
        try
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        scopes.Push(new HashSet<string>(StringComparer.Ordinal));
                        break;
                    case JsonTokenType.StartArray:
                        scopes.Push(null);
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        scopes.Pop();
                        break;
                    case JsonTokenType.PropertyName:
                        string name = reader.GetString()!;
                        if (!scopes.Peek()!.Add(name))
                        {
                            throw new PatchFormatException($"중복 JSON 필드: {name}");
                        }
                        break;
                }
            }
        }
        catch (JsonException e)
        {
            throw new PatchFormatException($"JSON 구문 오류: {e.Message}", e);
        }
    }
}

public sealed class PatchFormatException : Exception
{
    public PatchFormatException(string message) : base(message) { }
    public PatchFormatException(string message, Exception inner) : base(message, inner) { }
}
