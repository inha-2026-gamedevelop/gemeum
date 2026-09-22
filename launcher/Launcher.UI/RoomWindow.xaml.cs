using System.ComponentModel;
using System.Net;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Geumeum.Accounts;

namespace Geumeum.Launcher;

public partial class RoomWindow : Window
{
    private readonly AccountViewModel _account;
    private readonly string _build;
    private readonly CancellationTokenSource _lifetime = new();
    // 참가 중인 파티의 임대/출발만 확인한다. 방 목록은 사용자 요청으로만 조회한다
    private readonly DispatcherTimer _partyPulse = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _pulseBusy;
    private bool _pulseError;
    private bool _busy;
    private bool _closing;
    private bool _restored;
    private AccountRoom? _current;
    public AccountRoom? CurrentRoom => _current;
    public AccountRoom? SelectedRoom { get; private set; }

    public RoomWindow(AccountViewModel account, string build)
    {
        InitializeComponent();
        _account = account;
        _build = build;
        Width = Math.Max(MinWidth, Math.Min(Width, SystemParameters.WorkArea.Width - 30));
        Height = Math.Max(MinHeight, Math.Min(Height, SystemParameters.WorkArea.Height - 30));
        UpdateSelection();
        Loaded += async (_, _) => await RunAsync(RefreshAsync);
        _partyPulse.Tick += async (_, _) => await PulsePartyAsync();
        Closing += OnClosing;
        Closed += (_, _) => { _partyPulse.Stop(); _lifetime.Cancel(); };
    }

    private async Task PulsePartyAsync()
    {
        if (_busy || _pulseBusy || _current == null || _lifetime.IsCancellationRequested) return;
        var previous = _current;
        _pulseBusy = true;
        try
        {
            var room = await _account.LobbyAsync(previous.RoomId, "pulse", ct: _lifetime.Token);
            // 사용자 조작과 겹친 응답이 최신 파티 상태를 덮어쓰지 않게 한다
            if (_busy || !ReferenceEquals(_current, previous) || _lifetime.IsCancellationRequested) return;
            bool changed = JsonSerializer.Serialize(previous) != JsonSerializer.Serialize(room);
            _current = room;
            if (changed) UpdateSelection();
            if (_pulseError)
            {
                _pulseError = false;
                Status.Text = room.State == "waiting" ? "파티 연결이 복구되었습니다." : "출발 준비 중입니다.";
            }
            TryJoinCurrentGame();
        }
        catch (AccountException ex)
        {
            if (_busy || !ReferenceEquals(_current, previous) || _lifetime.IsCancellationRequested) return;
            if (ex.Status is HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.Unauthorized)
            {
                _current = null;
                UpdateSelection();
            }
            Status.Text = ex.Message;
            _pulseError = true;
        }
        catch (OperationCanceledException) { }
        finally { _pulseBusy = false; }
    }

    private bool TryJoinCurrentGame()
    {
        if (_current?.State is "ready" or "running" ||
            (_current?.Hosting == "listen" && _current.State == "hosting" && _current.OwnerId == _account.AccountId))
        {
            JoinGame(_current);
            return true;
        }
        return false;
    }

    private async Task RefreshAsync()
    {
        if (_current != null)
        {
            try { _current = await _account.LobbyAsync(_current.RoomId, "pulse", ct: _lifetime.Token); }
            catch (AccountException ex) when (ex.Status is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
            {
                _current = null;
                Status.Text = "참가 중인 파티가 종료되었거나 참가 시간이 만료되었습니다. 다시 참가해 주세요.";
            }
        }
        var result = await _account.RoomsAsync(_lifetime.Token);
        string? selected = (Rooms.SelectedItem as AccountRoom)?.RoomId;
        Rooms.ItemsSource = result.Rooms.Where(r => r.BuildId == _build).ToList();
        Rooms.SelectedItem = result.Rooms.FirstOrDefault(r => r.RoomId == selected && r.BuildId == _build);
        if (!_restored && _current == null)
            _current = result.Rooms.FirstOrDefault(r => r.Members.Any(m => m.AccountId == _account.AccountId));
        _restored = true;
        UpdateSelection();
        if (TryJoinCurrentGame()) return;
        if (result.BuildId != _build) Status.Text = "설치된 게임과 서버 버전이 다릅니다. 업데이트해 주세요.";
        else if (_current != null)
            Status.Text = _current.State == "waiting"
                ? "다른 파티를 선택해도 현재 방은 유지됩니다. 무기를 고르고 준비를 완료해 주세요."
                : "출발이 확정되었습니다. 게임 서버 준비가 끝나면 자동으로 게임을 실행합니다.";
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy || _lifetime.IsCancellationRequested) return;
        _busy = true;
        Actions.IsEnabled = false;
        Preparation.IsEnabled = false;
        try { await action(); }
        catch (AccountException ex) { Status.Text = ex.Message; }
        catch (OperationCanceledException) { }
        finally { _busy = false; Actions.IsEnabled = true; Preparation.IsEnabled = true; UpdateSelection(); }
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await RunAsync(RefreshAsync);
    private async void OnCreate(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        _current = await _account.CreateRoomAsync(RoomName.Text.Trim(), _build, _lifetime.Token);
        await RefreshAsync();
    });
    private async void OnCloseRoom(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_current == null) return;
        await _account.LobbyAsync(_current.RoomId, "leave", ct: _lifetime.Token);
        _current = null;
        await RefreshAsync();
    });
    private async void OnJoin(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (Rooms.SelectedItem is not AccountRoom room || !CanJoin(room)) return;
        _current = await _account.LobbyAsync(room.RoomId, "join", ct: _lifetime.Token);
        await RefreshAsync();
    });
    private async void OnPreference(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && int.TryParse(button.Tag?.ToString(), out int value))
            await ChangeAsync("preference", value);
    }
    private async void OnReady(object sender, RoutedEventArgs e)
        => await ChangeAsync("ready", enabled: _current?.Members.FirstOrDefault(m => m.AccountId == _account.AccountId)?.Ready != true);
    private async void OnBots(object sender, RoutedEventArgs e)
        => await ChangeAsync("bots", enabled: _current?.FillWithBots != true);
    private async void OnStart(object sender, RoutedEventArgs e) => await ChangeAsync("start");
    private Task ChangeAsync(string action, int? preference = null, bool enabled = false) => RunAsync(async () =>
    {
        if (_current == null) return;
        _current = await _account.LobbyAsync(_current.RoomId, action, preference, enabled, _lifetime.Token);
        await RefreshAsync();
    });

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closing || SelectedRoom != null || _current == null || !_account.IsSignedIn) return;
        e.Cancel = true;
        if (_busy) { Status.Text = "요청 처리 중입니다. 잠시 후 닫아 주세요."; return; }
        if (_current.State != "waiting") { Status.Text = "파티가 출발 중입니다. 게임 연결이 끝날 때까지 기다려 주세요."; return; }
        await RunAsync(async () =>
        {
            await _account.LobbyAsync(_current.RoomId, "leave", ct: _lifetime.Token);
            _current = null;
            _closing = true;
            Close();
        });
    }

    private void JoinGame(AccountRoom room)
    {
        if (!Guid.TryParse(room.RoomId, out _) || !IPAddress.TryParse(room.Address, out _) || room.Port is < 1024 or > 65535 || room.BuildId != _build)
        { Status.Text = "방 접속 정보가 올바르지 않습니다."; return; }
        SelectedRoom = room;
        DialogResult = true;
    }
    private bool CanJoin(AccountRoom room) => room.State == "waiting" && room.RoomId != _current?.RoomId
        && room.Members.Count < room.MaxPlayers && (_current == null || _current.State == "waiting");
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelection();
    private void UpdateSelection()
    {
        if (Members == null || CloseRoomButton == null || _account == null) return;
        var selected = Rooms.SelectedItem as AccountRoom;
        var room = _current;
        _partyPulse.IsEnabled = room != null && !_lifetime.IsCancellationRequested;
        RoomCreationOptions.Visibility = room == null ? Visibility.Visible : Visibility.Collapsed;
        CreateRoomButton.Visibility = RoomCreationOptions.Visibility;
        PartyPanel.Visibility = room == null ? Visibility.Collapsed : Visibility.Visible;
        PartyColumn.Width = room == null ? new GridLength(0) : new GridLength(450);
        Members.ItemsSource = Enumerable.Range(0, 3).Select(i =>
        {
            var member = room?.Members.ElementAtOrDefault(i);
            return new
            {
                Position = (i + 1).ToString("00"),
                Name = member?.Nickname ?? (room?.FillWithBots == true ? "봇 참가 예정" : "빈자리"),
                Role = member == null ? (room?.FillWithBots == true ? "남은 역할로 참가" : "파티원 모집 중") : (member.IsOwner ? "방장 · " : "") + member.WeaponLabel,
                Status = member == null ? (room?.FillWithBots == true ? "자동 충원" : "모집 중") : member.Ready ? "준비 완료" : "준비 중",
                Connected = member?.Ready == true
            };
        }).ToList();
        SelectedTitle.Text = room?.Name ?? "원정대를 선택하세요";
        PartyHeading.Text = _current == null ? "선택한 파티 정보" : "현재 참가 중인 파티";
        OwnerInfo.Text = room == null ? "파티에 참가하면 여기에서 출발을 준비합니다." : $"방장: {room.OwnerNickname}";
        RoomCount.Text = $"{Rooms.Items.Count}개 파티";
        EmptyRooms.Visibility = Rooms.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BrowseInfo.Text = selected == null ? "목록을 선택해 다른 파티를 둘러보세요." : $"선택: {selected.Name} · {selected.Occupancy} · {selected.StateLabel}";
        bool waiting = _current?.State == "waiting";
        bool owner = _current?.OwnerId == _account.AccountId;
        var me = _current?.Members.FirstOrDefault(m => m.AccountId == _account.AccountId);
        CloseRoomButton.IsEnabled = waiting;
        CloseRoomButton.Content = owner ? "파티 해산" : "파티 나가기";
        JoinButton.IsEnabled = selected != null && CanJoin(selected);
        JoinButton.Content = _current == null ? "파티 참가" : "선택한 방으로 이동";
        CreateRoomButton.IsEnabled = _current == null || waiting;
        WeaponButtons.IsEnabled = waiting;
        foreach (Button button in WeaponButtons.Children)
            button.BorderBrush = new System.Windows.Media.SolidColorBrush(me?.Preference?.ToString() == button.Tag?.ToString()
                ? System.Windows.Media.Color.FromRgb(216, 185, 121) : System.Windows.Media.Color.FromRgb(76, 80, 80));
        ReadyButton.IsEnabled = waiting && me?.Preference != null;
        ReadyButton.Content = me?.Ready == true ? "준비 취소" : "준비 완료";
        BotButton.IsEnabled = waiting && owner;
        BotButton.Content = _current?.FillWithBots == true ? "봇 충원 취소" : "빈자리 봇 추가";
        StartButton.IsEnabled = waiting && owner && _current!.Members.Count > 0
            && (_current.FillWithBots || _current.Members.Count == 3) && _current.Members.All(m => m.Ready);
        PartyNotice.Text = _current == null ? "참가 후 무기·봇·준비 설정을 할 수 있습니다."
            : !waiting ? "게임 서버 준비 중 · 자동 실행 예정"
            : $"준비 {_current.Members.Count(m => m.Ready)} / {_current.Members.Count} · " + (_current.FillWithBots ? "빈자리 봇 충원" : "3인 파티")
                + "\n전원 준비 후 방장이 원정 출발을 누릅니다.";
    }
}
