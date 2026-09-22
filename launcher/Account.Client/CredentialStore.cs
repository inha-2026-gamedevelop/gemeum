using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Geumeum.Accounts;

/// <summary> 자동 로그인용 refresh token의 로컬 저장 수명 계약 </summary>
public interface ICredentialStore : IDisposable
{
    /// <summary> 저장 정보가 없으면 null, 손상된 정보는 삭제 후 인증 오류를 반환 </summary>
    string? Read();
    void Save(string refreshToken);
    void Delete();
}

/// <summary> API origin별 토큰을 현재 Windows 사용자로 암호화하고 중복 런처 접근을 잠근다 </summary>
public sealed class CredentialStore : ICredentialStore
{
    private readonly string _path;
    private readonly string _origin;
    private readonly byte[] _entropy;
    private readonly FileStream _lock;

    public CredentialStore(string directory, Uri origin)
    {
        _origin = origin.AbsoluteUri;
        _entropy = SHA256.HashData(Encoding.UTF8.GetBytes(_origin));
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, Convert.ToHexString(_entropy) + ".bin");
        try
        {
            _lock = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            throw new AccountException("다른 런처에서 계정을 사용 중입니다. 해당 런처를 닫고 다시 실행해 주세요.");
        }
    }

    public string? Read()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            if (new FileInfo(_path).Length > 16 * 1024) throw new CryptographicException();
            byte[] clear = ProtectedData.Unprotect(File.ReadAllBytes(_path), _entropy,
                DataProtectionScope.CurrentUser);
            try
            {
                Stored? saved = JsonSerializer.Deserialize<Stored>(clear);
                if (saved is null || saved.Origin != _origin || !AccountApi.ValidToken(saved.RefreshToken))
                    throw new CryptographicException();
                return saved.RefreshToken;
            }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            Delete();
            throw new AccountException("자동 로그인 정보를 읽지 못했습니다. 다시 로그인해 주세요.");
        }
    }

    public void Save(string refreshToken)
    {
        byte[] clear = JsonSerializer.SerializeToUtf8Bytes(new Stored
        {
            Origin = _origin, RefreshToken = refreshToken,
        });
        try
        {
            byte[] encrypted = ProtectedData.Protect(clear, _entropy, DataProtectionScope.CurrentUser);
            string temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, _path, true);
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public void Delete()
    {
        File.Delete(_path);
        File.Delete(_path + ".tmp");
    }

    public void Dispose() => _lock.Dispose();

    private sealed class Stored
    {
        public string Origin { get; set; } = "";
        public string RefreshToken { get; set; } = "";
    }
}
