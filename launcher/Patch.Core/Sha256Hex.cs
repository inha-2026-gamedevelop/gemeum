using System.Security.Cryptography;

namespace Geumeum.Patch;

public static class Sha256Hex
{
    public const int LENGTH = 64;

    public static string OfBytes(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static string OfFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static bool IsValid(string? hex)
    {
        return hex != null && hex.Length == LENGTH && hex.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
    }
}
