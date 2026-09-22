namespace Geumeum.Patch;

/// <summary>
/// 채널 최신 배포를 가리키는 서명 봉투. 서명 대상은 payloadBase64를 디코딩한 원본 바이트다 - JSON을 다시 만들어 검증하지 않는다
/// </summary>
public sealed class SignedEnvelope
{
    public string KeyId           { get; set; } = "";
    public string PayloadBase64   { get; set; } = "";
    public string SignatureBase64 { get; set; } = "";

    public static SignedEnvelope Parse(ReadOnlySpan<byte> utf8)
    {
        return StrictJson.Deserialize<SignedEnvelope>(utf8, ManifestLimits.MAX_ENVELOPE_BYTES);
    }

    public byte[] ToUtf8Bytes() => StrictJson.SerializeToUtf8Bytes(this);

    public static SignedEnvelope Create(string keyId, string privateKeyPem, ChannelPayload payload)
    {
        return Create(keyId, privateKeyPem, payload.ToUtf8Bytes());
    }

    public static SignedEnvelope Create(string keyId, string privateKeyPem, byte[] payloadBytes)
    {
        return new SignedEnvelope
        {
            KeyId           = keyId,
            PayloadBase64   = Convert.ToBase64String(payloadBytes),
            SignatureBase64 = Convert.ToBase64String(ReleaseSigner.Sign(privateKeyPem, payloadBytes)),
        };
    }

    /// <summary>
    /// 신뢰 키 목록으로 서명을 확인하고 payload를 돌려준다. 실패는 전부 예외 - 우회 경로를 두지 않는다
    /// </summary>
    public ChannelPayload VerifyAndOpen(IReadOnlyDictionary<string, string> trustedPublicKeysPem, DateTimeOffset nowUtc, long? lastSeenSequence)
    {
        ChannelPayload opened = ChannelPayload.Parse(VerifyPayload(trustedPublicKeysPem));

        if (nowUtc < opened.IssuedAtUtc - ChannelPayload.CLOCK_SKEW_TOLERANCE)
        {
            throw new PatchClockException($"발행 시각이 미래다: issued={opened.IssuedAtUtc:u}, now={nowUtc:u}");
        }
        if (nowUtc > opened.ExpiresAtUtc)
        {
            throw new PatchTrustException($"만료된 채널 문서: expires={opened.ExpiresAtUtc:u}");
        }
        if (lastSeenSequence.HasValue && opened.Sequence < lastSeenSequence.Value)
        {
            throw new PatchTrustException($"sequence 후퇴: 저장={lastSeenSequence.Value}, 수신={opened.Sequence}");
        }

        return opened;
    }

    /// <summary>
    /// 서명만 확인하고 원본 payload 바이트를 돌려준다. 해석은 문서 종류별 Parse가 한다
    /// </summary>
    public byte[] VerifyPayload(IReadOnlyDictionary<string, string> trustedPublicKeysPem)
    {
        if (!trustedPublicKeysPem.TryGetValue(KeyId, out string? publicKeyPem))
        {
            throw new PatchTrustException($"알 수 없는 서명 키 '{KeyId}'");
        }

        byte[] payload;
        byte[] signature;
        try
        {
            payload   = Convert.FromBase64String(PayloadBase64);
            signature = Convert.FromBase64String(SignatureBase64);
        }
        catch (FormatException e)
        {
            throw new PatchTrustException("Base64 형식 오류", e);
        }

        if (!ReleaseSigner.Verify(publicKeyPem, payload, signature))
        {
            throw new PatchTrustException("서명 검증 실패");
        }

        return payload;
    }
}

/// <summary>
/// 최신 런처를 가리키는 서명 문서(launcher/latest.json의 payload). exe는 게임 파일과 같은 객체 저장소에 해시 이름으로 둔다
/// </summary>
public sealed class LauncherRelease
{
    public const int  CURRENT_SCHEMA_VERSION = 1;
    public const long MAX_SIZE = 512L * 1024 * 1024;

    public int            SchemaVersion { get; set; } = CURRENT_SCHEMA_VERSION;
    public string         Platform      { get; set; } = BuildIdentity.PLATFORM_WIN_X64;
    public string         Version       { get; set; } = "";
    public string         Sha256        { get; set; } = "";
    public long           Size          { get; set; }
    public DateTimeOffset IssuedAtUtc   { get; set; }

    public System.Version ParsedVersion => System.Version.Parse(Version);

    public byte[] ToUtf8Bytes() => StrictJson.SerializeToUtf8Bytes(this);

    public static LauncherRelease Parse(ReadOnlySpan<byte> utf8)
    {
        LauncherRelease r = StrictJson.Deserialize<LauncherRelease>(utf8, ManifestLimits.MAX_ENVELOPE_BYTES);
        var errors = new List<string>();

        if (r.SchemaVersion != CURRENT_SCHEMA_VERSION)                 errors.Add($"미지원 schemaVersion {r.SchemaVersion}");
        if (r.Platform != BuildIdentity.PLATFORM_WIN_X64)              errors.Add($"미지원 platform '{r.Platform}'");
        if (!System.Version.TryParse(r.Version, out _))                errors.Add($"version 형식 오류 '{r.Version}'");
        if (!Sha256Hex.IsValid(r.Sha256))                              errors.Add("sha256 형식 오류");
        if (r.Size <= 0 || r.Size > MAX_SIZE)                          errors.Add($"size 범위 밖: {r.Size}");

        if (errors.Count > 0)
        {
            throw new PatchFormatException("런처 배포 정보 거부:\n  " + string.Join("\n  ", errors));
        }
        return r;
    }

    /// <summary>
    /// 1.2.0.0 같은 파일 버전을 세 자리로 맞춘다. 런처 자기 버전 비교와 같은 규칙이다
    /// </summary>
    public static System.Version Normalize(System.Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}

public sealed class ChannelPayload
{
    public const int CURRENT_SCHEMA_VERSION = 1;
    public static readonly TimeSpan CLOCK_SKEW_TOLERANCE = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan DEFAULT_LIFETIME     = TimeSpan.FromDays(180);

    public int            SchemaVersion      { get; set; } = CURRENT_SCHEMA_VERSION;
    public string         Channel            { get; set; } = "";
    public string         Platform           { get; set; } = "";
    public long           Sequence           { get; set; }
    public DateTimeOffset IssuedAtUtc        { get; set; }
    public DateTimeOffset ExpiresAtUtc       { get; set; }
    public string         ReleaseId          { get; set; } = "";
    public string         ManifestSha256     { get; set; } = "";
    public long           ManifestSize       { get; set; }
    public string         MinLauncherVersion { get; set; } = "";

    public byte[] ToUtf8Bytes() => StrictJson.SerializeToUtf8Bytes(this);

    public static ChannelPayload Parse(ReadOnlySpan<byte> utf8)
    {
        ChannelPayload p = StrictJson.Deserialize<ChannelPayload>(utf8, ManifestLimits.MAX_ENVELOPE_BYTES);
        var errors = new List<string>();

        if (p.SchemaVersion != CURRENT_SCHEMA_VERSION)             errors.Add($"미지원 schemaVersion {p.SchemaVersion}");
        if (p.Channel is not ("stable" or "internal"))             errors.Add($"알 수 없는 채널 '{p.Channel}'");
        if (p.Platform != BuildIdentity.PLATFORM_WIN_X64)          errors.Add($"미지원 platform '{p.Platform}'");
        if (p.Sequence <= 0)                                       errors.Add("sequence는 1 이상");
        if (p.ExpiresAtUtc <= p.IssuedAtUtc)                       errors.Add("만료가 발행보다 앞선다");
        if (!ManifestValidator.IsValidReleaseId(p.ReleaseId))      errors.Add($"releaseId 형식 오류 '{p.ReleaseId}'");
        if (!Sha256Hex.IsValid(p.ManifestSha256))                  errors.Add("manifestSha256 형식 오류");
        if (p.ManifestSize <= 0 || p.ManifestSize > ManifestLimits.MAX_MANIFEST_BYTES) errors.Add("manifestSize 범위 밖");
        if (!Version.TryParse(p.MinLauncherVersion, out _))       errors.Add($"minLauncherVersion 형식 오류 '{p.MinLauncherVersion}'");

        if (errors.Count > 0)
        {
            throw new PatchFormatException("채널 payload 거부:\n  " + string.Join("\n  ", errors));
        }
        return p;
    }

    /// <summary>
    /// 다운로드한 manifest 바이트가 서명된 payload가 가리키는 것인지 확인한 뒤에만 파싱한다
    /// </summary>
    public ReleaseManifest OpenManifest(ReadOnlySpan<byte> manifestUtf8)
    {
        if (manifestUtf8.Length != ManifestSize)
        {
            throw new PatchTrustException($"manifest 크기 불일치: 기대={ManifestSize}, 실제={manifestUtf8.Length}");
        }
        string actual = Sha256Hex.OfBytes(manifestUtf8);
        if (!string.Equals(actual, ManifestSha256, StringComparison.Ordinal))
        {
            throw new PatchTrustException("manifest 해시 불일치");
        }

        ReleaseManifest manifest = ReleaseManifest.Parse(manifestUtf8);
        if (!string.Equals(manifest.ReleaseId, ReleaseId, StringComparison.Ordinal))
        {
            throw new PatchTrustException($"manifest releaseId가 채널과 다르다: 채널={ReleaseId}, manifest={manifest.ReleaseId}");
        }
        return manifest;
    }
}

public sealed class PatchTrustException : Exception
{
    public PatchTrustException(string message) : base(message) { }
    public PatchTrustException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// PC 시계 오차는 다른 오류와 구분해 안내한다
/// </summary>
public sealed class PatchClockException : Exception
{
    public PatchClockException(string message) : base(message) { }
}
