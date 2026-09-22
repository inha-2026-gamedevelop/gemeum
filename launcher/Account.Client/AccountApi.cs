using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Geumeum.Accounts;

/// <summary> 계정 요청 실패를 사용자 메시지와 선택적 HTTP 상태로 전달한다 </summary>
public sealed class AccountException : Exception
{
    public AccountException(string message, HttpStatusCode? status = null) : base(message)
        => Status = status;
    public HttpStatusCode? Status { get; }
}

public sealed class AccountProfile
{
    public string AccountId { get; set; } = "";
    public string LoginId { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string Status { get; set; } = "";
}

/// <summary> 토큰 응답, 자격증명이 자동 ToString에 노출되지 않도록 record를 사용하지 않는다 </summary>
public sealed class TokenReply
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string TokenType { get; set; } = "";
    public int ExpiresIn { get; set; }
    public DateTimeOffset SessionExpiresAt { get; set; }
    public string SessionKind { get; set; } = "";
    public AccountProfile Account { get; set; } = new();
}

/// <summary> 검증한 origin에만 계정 요청을 보내며 리디렉션으로 인증 정보를 전달하지 않는다 </summary>
public sealed class AccountApi : IDisposable
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        MaxDepth = 12,
    };

    private readonly HttpClient _http;
    public Uri Origin { get; }

    public AccountApi(string origin, bool allowLocalHttp = false, HttpMessageHandler? handler = null)
    {
        Origin = ValidateOrigin(origin, allowLocalHttp);
        _http = new HttpClient(handler ?? new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = !Origin.IsLoopback,
            UseCookies = false,
        }) { BaseAddress = Origin, Timeout = TimeSpan.FromSeconds(10) };
    }

    /// <summary> HTTPS origin만 허용하며 개발 옵션이 켜진 루프백 주소에만 HTTP를 허용한다 </summary>
    public static Uri ValidateOrigin(string value, bool allowLocalHttp)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || uri.AbsolutePath != "/"
            || (uri.Scheme != Uri.UriSchemeHttps
                && !(allowLocalHttp && uri.Scheme == Uri.UriSchemeHttp
                     && (uri.Host == "127.0.0.1" || uri.Host == "localhost" || uri.Host == "[::1]"))))
            throw new AccountException("계정 서버 주소가 올바르지 않습니다. HTTPS 주소를 사용해 주세요.");
        return uri;
    }

    public Task<TokenReply> LoginAsync(string login, string password, CancellationToken ct = default)
        => TokensAsync("v1/auth/login", new { login_id = login, password }, null, ct);

    public Task<TokenReply> RefreshAsync(string refresh, CancellationToken ct = default)
        => TokensAsync("v1/auth/refresh", new { refresh_token = refresh }, null, ct);

    public Task<TokenReply> CreateGameSessionAsync(string access, CancellationToken ct = default)
        => TokensAsync("v1/auth/game-sessions", null, access, ct);

    public Task<RoomList> RoomsAsync(string access, CancellationToken ct = default)
        => ReadAsync<RoomList>(HttpMethod.Get, "v1/rooms", null, access, ct);

    public Task<AccountRoom> LobbyAsync(string access, string room, string action, int? preference = null, bool enabled = false, CancellationToken ct = default)
        => ReadAsync<AccountRoom>(HttpMethod.Post, "v1/rooms/" + Guid.Parse(room) + "/lobby", new { action, preference, enabled }, access, ct);

    public Task<AccountRoom> CreateRoomAsync(string access, string name, string build, CancellationToken ct = default)
        => ReadAsync<AccountRoom>(HttpMethod.Post, "v1/rooms", new { name, build_id = build }, access, ct);

    public async Task CloseRoomAsync(string access, string room, CancellationToken ct = default)
        => _ = await SendAsync(HttpMethod.Delete, "v1/rooms/" + Guid.Parse(room).ToString(), null, access, ct).ConfigureAwait(false);

    public Task<AccountProfile> MeAsync(string access, CancellationToken ct = default)
        => ProfileAsync(HttpMethod.Get, "v1/me", null, access, ct);

    public Task<AccountProfile> NicknameAsync(string access, string nickname, CancellationToken ct = default)
        => ProfileAsync(HttpMethod.Patch, "v1/me/nickname", new { nickname }, access, ct);

    private async Task<AccountProfile> ProfileAsync(HttpMethod method, string path, object? body,
        string access, CancellationToken ct)
    {
        AccountProfile profile = await ReadAsync<AccountProfile>(method, path, body, access, ct).ConfigureAwait(false);
        if (!ValidProfile(profile)) throw new AccountException("계정 서버 응답을 확인할 수 없습니다.");
        return profile;
    }

    private static bool ValidProfile(AccountProfile? profile) => profile != null
        && ulong.TryParse(profile.AccountId, out ulong id) && id > 0 && profile.Status == "active"
        && !string.IsNullOrWhiteSpace(profile.LoginId) && !string.IsNullOrWhiteSpace(profile.Nickname);

    public async Task PasswordAsync(string access, string current, string next, CancellationToken ct = default)
        => _ = await SendAsync(HttpMethod.Post, "v1/auth/password",
            new { current_password = current, new_password = next }, access, ct).ConfigureAwait(false);

    public async Task LogoutAsync(string access, CancellationToken ct = default)
        => _ = await SendAsync(HttpMethod.Post, "v1/auth/logout", null, access, ct).ConfigureAwait(false);

    private async Task<TokenReply> TokensAsync(string path, object? body, string? access, CancellationToken ct)
    {
        TokenReply result = await ReadAsync<TokenReply>(HttpMethod.Post, path, body, access, ct).ConfigureAwait(false);
        if (!ValidProfile(result.Account) || result.TokenType != "Bearer"
            || result.ExpiresIn <= 0 || result.ExpiresIn > 3600
            || result.SessionExpiresAt <= DateTimeOffset.UtcNow
            || !ValidToken(result.AccessToken) || !ValidToken(result.RefreshToken)
            || result.SessionKind != (path == "v1/auth/game-sessions" ? "game" : "launcher"))
            throw new AccountException("계정 서버 응답을 확인할 수 없습니다. 다시 로그인해 주세요.");
        return result;
    }

    internal static bool ValidToken(string? value) => value != null && value.Length is >= 40 and <= 128
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    private async Task<T> ReadAsync<T>(HttpMethod method, string path, object? body, string? access,
        CancellationToken ct)
    {
        byte[] bytes = await SendAsync(method, path, body, access, ct).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, Json)
                ?? throw new AccountException("계정 서버의 응답이 비어 있습니다.");
        }
        catch (JsonException)
        {
            throw new AccountException("계정 서버 응답 형식이 올바르지 않습니다.");
        }
    }

    private async Task<byte[]> SendAsync(HttpMethod method, string path, object? body, string? access,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        CancellationToken requestToken = deadline.Token;
        if (body != null) request.Content = JsonContent.Create(body);
        if (access != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, requestToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // Never include arbitrary response bodies or request credentials in errors
                string message = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized when path == "v1/auth/login" => "아이디 또는 비밀번호를 확인해 주세요.",
                    HttpStatusCode.Unauthorized when path == "v1/auth/password" => "현재 비밀번호 또는 로그인 상태를 확인해 주세요.",
                    HttpStatusCode.Unauthorized => "로그인이 만료되었습니다. 다시 로그인해 주세요.",
                    HttpStatusCode.Forbidden => "이 계정으로 요청을 처리할 수 없습니다.",
                    HttpStatusCode.TooManyRequests => "요청이 많습니다. 잠시 기다린 후 다시 시도해 주세요.",
                    HttpStatusCode.UnprocessableEntity => "입력값의 길이와 형식을 확인해 주세요.",
                    HttpStatusCode.Conflict => "방을 사용할 수 없습니다. 빌드 버전, 기존 방 또는 서버 정원을 확인해 주세요.",
                    _ => "계정 서버에서 요청을 처리하지 못했습니다. 잠시 후 다시 시도해 주세요.",
                };
                throw new AccountException(message, response.StatusCode);
            }
            using Stream stream = await response.Content.ReadAsStreamAsync(requestToken).ConfigureAwait(false);
            using var output = new MemoryStream();
            byte[] buffer = new byte[4096];
            int count;
            while ((count = await stream.ReadAsync(buffer, requestToken).ConfigureAwait(false)) > 0)
            {
                if (output.Length + count > 64 * 1024)
                    throw new AccountException("계정 서버 응답 크기가 너무 큽니다.");
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
        catch (HttpRequestException)
        {
            throw new AccountException("계정 서버에 연결할 수 없습니다. 서버 실행 상태를 확인해 주세요.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AccountException("계정 서버 응답 시간이 초과되었습니다. 다시 로그인해 주세요.");
        }
    }

    public void Dispose() => _http.Dispose();
}
