using System.Net;

namespace Geumeum.Accounts;

/// <summary> 계정 변경과 토큰 갱신을 직렬화하며 일회용 refresh token을 자동 재시도하지 않는다 </summary>
public sealed class AccountSession : IDisposable
{
    private readonly AccountApi _api;
    private readonly ICredentialStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TokenReply? _tokens;
    private DateTimeOffset _accessExpiresAt;
    private bool _remember;
    private bool _disposed;

    public AccountSession(AccountApi api, ICredentialStore store) { _api = api; _store = store; }
    public AccountProfile? Profile => _tokens?.Account;
    public bool Remembered => _remember;

    public async Task RestoreAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            string? refresh = _store.Read();
            if (refresh == null) return;
            _remember = true;
            _store.Delete(); // crash/network ambiguity must never replay the persisted old token
            try { Accept(await _api.RefreshAsync(refresh, ct).ConfigureAwait(false)); }
            catch { Clear(); throw; }
        }
        finally { _gate.Release(); }
    }

    public async Task LoginAsync(string login, string password, bool remember, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_tokens != null) throw new AccountException("먼저 로그아웃해 주세요.");
            _store.Delete();
            TokenReply tokens = await _api.LoginAsync(login, password, ct).ConfigureAwait(false);
            _remember = remember;
            Accept(tokens);
        }
        finally { _gate.Release(); }
    }

    public async Task RefreshProfileAsync(CancellationToken ct = default)
        => await WithAccess(async access =>
        {
            AccountProfile profile = await _api.MeAsync(access, ct).ConfigureAwait(false);
            if (profile.AccountId != _tokens!.Account.AccountId || profile.Status != "active")
                throw new AccountException("계정 정보를 확인하지 못했습니다.", HttpStatusCode.Unauthorized);
            _tokens.Account = profile;
        }, ct).ConfigureAwait(false);

    public async Task SetNicknameAsync(string nickname, CancellationToken ct = default)
        => await WithAccess(async access =>
            _tokens!.Account = await _api.NicknameAsync(access, nickname, ct).ConfigureAwait(false), ct)
            .ConfigureAwait(false);

    public async Task ChangePasswordAsync(string current, string next, CancellationToken ct = default)
        => await WithAccess(async access =>
        {
            try { await _api.PasswordAsync(access, current, next, ct).ConfigureAwait(false); }
            finally { Clear(); } // the server may have changed it even if its response was lost
        }, ct).ConfigureAwait(false);

    public async Task<bool> LogoutAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_tokens == null) { Clear(); return true; }
            try
            {
                await FreshAsync(ct).ConfigureAwait(false);
                await _api.LogoutAsync(_tokens!.AccessToken, ct).ConfigureAwait(false);
                return true;
            }
            catch (AccountException ex) { return ex.Status == HttpStatusCode.Unauthorized; }
            finally { Clear(); }
        }
        finally { _gate.Release(); }
    }

    public async Task<RoomList> RoomsAsync(CancellationToken ct = default)
    {
        RoomList? result = null;
        await WithAccess(async access => result = await _api.RoomsAsync(access, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        return result!;
    }

    public async Task<AccountRoom> CreateRoomAsync(string name, string build, CancellationToken ct = default)
    {
        AccountRoom? result = null;
        await WithAccess(async access => result = await _api.CreateRoomAsync(access, name, build, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        return result!;
    }

    public Task CloseRoomAsync(string room, CancellationToken ct = default)
        => WithAccess(access => _api.CloseRoomAsync(access, room, ct), ct);

    public async Task<AccountRoom> LobbyAsync(string room, string action, int? preference = null, bool enabled = false, CancellationToken ct = default)
    {
        AccountRoom? result = null;
        await WithAccess(async access => result = await _api.LobbyAsync(access, room, action, preference, enabled, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        return result!;
    }

    // Never put this reply in command-line arguments
    public async Task<TokenReply> CreateGameSessionAsync(CancellationToken ct = default)
    {
        TokenReply? result = null;
        await WithAccess(async access =>
            result = await _api.CreateGameSessionAsync(access, ct).ConfigureAwait(false), ct)
            .ConfigureAwait(false);
        return result!;
    }

    private async Task WithAccess(Func<string, Task> operation, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await FreshAsync(ct).ConfigureAwait(false);
            await operation(_tokens!.AccessToken).ConfigureAwait(false);
        }
        catch (AccountException ex) when (ex.Status == HttpStatusCode.Unauthorized)
        {
            Clear();
            throw;
        }
        finally { _gate.Release(); }
    }

    private async Task FreshAsync(CancellationToken ct)
    {
        if (_tokens == null) throw new AccountException("먼저 로그인해 주세요.", HttpStatusCode.Unauthorized);
        if (_accessExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30)) return;
        string refresh = _tokens.RefreshToken;
        _store.Delete();
        try { Accept(await _api.RefreshAsync(refresh, ct).ConfigureAwait(false)); }
        catch { Clear(); throw; }
    }

    private void Accept(TokenReply tokens)
    {
        if (_remember) _store.Save(tokens.RefreshToken);
        _tokens = tokens;
        _accessExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn);
    }

    private void Clear()
    {
        _tokens = null;
        _remember = false;
        _store.Delete();
    }

    public void Dispose()
    {
        _gate.Wait();
        try
        {
            if (_disposed) return;
            _disposed = true;
            _tokens = null;
            _api.Dispose();
            _store.Dispose();
        }
        finally { _gate.Release(); }
    }
}
