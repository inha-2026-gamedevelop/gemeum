using System.Security.Cryptography;

namespace Geumeum.Patch;

/// <summary>
/// ECDSA P-256 + SHA-256, 서명 형식은 IEEE P1363 고정 64바이트. 송수신이 같은 형식을 써야 한다
/// </summary>
public static class ReleaseSigner
{
    public const int SIGNATURE_LENGTH = 64;

    public static (string privateKeyPem, string publicKeyPem) GenerateKeyPair()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportPkcs8PrivateKeyPem(), key.ExportSubjectPublicKeyInfoPem());
    }

    public static byte[] Sign(string privateKeyPem, ReadOnlySpan<byte> payload)
    {
        using ECDsa key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        EnsureP256(key);

        byte[] signature = key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        if (signature.Length != SIGNATURE_LENGTH)
        {
            throw new CryptographicException($"서명 길이가 {SIGNATURE_LENGTH}가 아니다: {signature.Length}");
        }
        return signature;
    }

    public static bool Verify(string publicKeyPem, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> signature)
    {
        if (signature.Length != SIGNATURE_LENGTH)
        {
            return false;
        }

        using ECDsa key = ECDsa.Create();
        key.ImportFromPem(publicKeyPem);
        EnsureP256(key);
        return key.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private static void EnsureP256(ECDsa key)
    {
        if (key.KeySize != 256)
        {
            throw new CryptographicException($"P-256 키만 허용한다. 키 크기: {key.KeySize}");
        }
    }
}
