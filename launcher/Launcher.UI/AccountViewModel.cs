using System.IO;
using System.Security.Cryptography;
using System.Text;
using Geumeum.Accounts;

namespace Geumeum.Launcher;

public sealed class AccountViewModel : ObservableObject, IDisposable
{
    private readonly AccountSession? _session;
    public string ApiOrigin { get; }
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private bool _busy;
    private bool _disposed;
    private string _message = "아이디와 비밀번호로 로그인해 주세요.";
    private bool _error;
    private string _loginId = "";
    private string _nickname = "";
    private bool _remember;

    public AccountViewModel(string origin, bool local, string stateDirectory)
    {
        ApiOrigin = origin;
        AccountApi? api = null;
        try
        {
            api = new AccountApi(origin, local);
            _session = new AccountSession(api, new CredentialStore(stateDirectory, api.Origin));
        }
        catch (Exception ex) when (ex is AccountException or IOException or UnauthorizedAccessException)
        {
            api?.Dispose();
            _error = true;
            _message = ex is AccountException ? ex.Message : "계정 저장소를 열 수 없습니다. 런처를 다시 실행해 주세요.";
        }
    }

    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) Raise(nameof(CanEdit)); } }
    public bool CanEdit => !_busy && _session != null;
    public bool IsSignedIn => _session?.Profile != null;
    public string? AccountId => _session?.Profile?.AccountId;
    public bool IsSignedOut => !IsSignedIn;
    public string Summary => IsSignedIn ? $"{_session!.Profile!.Nickname} · {_session.Profile.LoginId}" : "계정 로그인";
    public string LoginId { get => _loginId; set => Set(ref _loginId, value); }
    public string Nickname { get => _nickname; set => Set(ref _nickname, value); }
    public bool RememberLogin { get => _remember; set => Set(ref _remember, value); }
    public string Message { get => _message; private set => Set(ref _message, value); }
    public bool IsError { get => _error; private set => Set(ref _error, value); }
    public event Action? SessionChanged;

    public Task<TokenReply> CreateGameSessionAsync(CancellationToken ct)
        => _session?.CreateGameSessionAsync(ct) ?? throw new AccountException("먼저 로그인해 주세요.");

    public Task<RoomList> RoomsAsync(CancellationToken ct)
        => _session?.RoomsAsync(ct) ?? throw new AccountException("먼저 로그인해 주세요.");

    public Task<AccountRoom> LobbyAsync(string room, string action, int? preference = null, bool enabled = false, CancellationToken ct = default)
        => _session?.LobbyAsync(room, action, preference, enabled, ct) ?? throw new AccountException("먼저 로그인해 주세요.");

    public Task<AccountRoom> CreateRoomAsync(string name, string build, CancellationToken ct)
        => _session?.CreateRoomAsync(name, build, ct) ?? throw new AccountException("먼저 로그인해 주세요.");

    public Task CloseRoomAsync(string room, CancellationToken ct)
        => _session?.CloseRoomAsync(room, ct) ?? throw new AccountException("먼저 로그인해 주세요.");

    public Task RestoreAsync() => RunAsync(async ct =>
    {
        await _session!.RestoreAsync(ct);
        Message = IsSignedIn ? "자동 로그인되었습니다." : "아이디와 비밀번호로 로그인해 주세요.";
        RememberLogin = _session.Remembered;
    }, "자동 로그인 확인 중…");

    public Task LoginAsync(string password) => RunAsync(async ct =>
    {
        if (string.IsNullOrWhiteSpace(LoginId) || password.Length == 0)
            throw new AccountException("아이디와 비밀번호를 입력해 주세요.");
        await _session!.LoginAsync(LoginId.Trim(), password, RememberLogin, ct);
        Message = "로그인되었습니다.";
    }, "로그인 중…");

    public Task RefreshProfileAsync() => RunAsync(async ct =>
    {
        if (!IsSignedIn) return;
        await _session!.RefreshProfileAsync(ct);
        Message = "계정 정보를 확인했습니다.";
    }, "계정 확인 중…");

    public Task ChangeNicknameAsync() => RunAsync(async ct =>
    {
        await _session!.SetNicknameAsync(Nickname.Trim(), ct);
        Message = "닉네임을 변경했습니다.";
    }, "닉네임 변경 중…");

    public Task ChangePasswordAsync(string current, string next, string confirm) => RunAsync(async ct =>
    {
        if (next.EnumerateRunes().Count() is < 8 or > 15) throw new AccountException("새 비밀번호는 8~15자로 입력해 주세요.");
        if (next != confirm) throw new AccountException("새 비밀번호 확인이 일치하지 않습니다.");
        if (current.Length == 0) throw new AccountException("현재 비밀번호를 입력해 주세요.");
        await _session!.ChangePasswordAsync(current, next, ct);
        Message = "비밀번호를 변경했습니다. 새 비밀번호로 다시 로그인해 주세요.";
    }, "비밀번호 변경 중…");

    public Task LogoutAsync() => RunAsync(async ct =>
    {
        bool revoked = await _session!.LogoutAsync(ct);
        Message = revoked ? "로그아웃되었습니다."
            : "이 PC의 로그인 정보를 지웠습니다. 서버 연결 실패로 원격 세션 종료는 확인하지 못했습니다.";
    }, "로그아웃 중…");

    private async Task RunAsync(Func<CancellationToken, Task> action, string progress)
    {
        if (IsBusy || _disposed || _session == null) return;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = operation;
        IsBusy = true;
        IsError = false;
        Message = progress;
        try { await action(operation.Token); }
        catch (AccountException ex) { Message = ex.Message; IsError = true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            Message = "로그인 정보를 안전하게 저장하지 못했습니다. 저장 권한을 확인해 주세요.";
            IsError = true;
        }
        catch (OperationCanceledException) { Message = "요청을 취소했습니다. 필요하면 다시 로그인해 주세요."; IsError = true; }
        finally
        {
            _operation = null;
            IsBusy = false;
            if (IsSignedIn)
            {
                Nickname = _session.Profile!.Nickname;
                LoginId = _session.Profile.LoginId;
            }
            Raise(nameof(IsSignedIn));
            Raise(nameof(IsSignedOut));
            Raise(nameof(Summary));
            SessionChanged?.Invoke();
        }
    }

    public void CancelOperation() => _operation?.Cancel();

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _session?.Dispose();
        _lifetime.Dispose();
    }
}
