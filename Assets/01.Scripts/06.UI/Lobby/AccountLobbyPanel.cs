// System
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// Unity
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Network;

namespace Geumeum.UI
{
    /// <summary>
    /// 런처 로그인 뒤 오프라인 로비에서 방과 파티를 조작하는 계정 화면.
    /// 첫 화면은 Title 씬의 AccountTitleMenu가 맡고, 이 화면은 Lobby 씬의 방 목록, 파티와 Boot, 연결 대기를 맡는다.
    /// </summary>
    public sealed class AccountLobbyPanel : MonoBehaviour
    {
        private const float PARTY_PULSE_SECONDS     = 2f;
        private const int   WEAPON_PREFERENCE_COUNT = 3;     // 검, 활, 방울
        private const int   MIN_PARTY_SLOTS         = 3;
        private const int   MAX_PARTY_SLOTS         = 6;
        public const int    MIN_ROOM_NAME_LENGTH    = 2;
        public const int    MAX_ROOM_NAME_LENGTH    = 16;

        private static readonly Color SELECTED_PREFERENCE_COLOR   = new Color(0.42f, 0.32f, 0.15f);
        private static readonly Color UNSELECTED_PREFERENCE_COLOR = new Color(0.12f, 0.17f, 0.22f);

        private enum TitleIntent
        {
            None = 0,
            Rooms = 1,
            Create = 2
        }

        [SerializeField] private Canvas _canvas;
        [SerializeField] private CanvasGroup _canvasGroup;
        // SessionManagers의 RunEventSystem은 Mirror가 스폰하기 전까지 꺼져 있어 오프라인 화면 입력을 따로 둔다.
        [SerializeField] private GameObject _frontendEventSystem;
        [SerializeField] private GameObject _loadingPage;
        [SerializeField] private GameObject _roomsPage;
        [SerializeField] private GameObject _partyPage;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private TMP_Text _identityText;
        [SerializeField] private TMP_Text _roomSummaryText;
        [SerializeField] private TMP_Text _partyTitleText;
        [SerializeField] private TMP_Text _partyMembersText;
        [SerializeField] private TMP_Text _partyHintText;
        [SerializeField] private TMP_InputField _roomNameInput;
        [SerializeField] private RectTransform _roomListContent;
        [SerializeField] private Button _roomRowTemplate;
        [SerializeField] private Button _roomsRefreshButton;
        [SerializeField] private Button _roomsCreateButton;
        [SerializeField] private Button _roomsBackButton;
        [SerializeField] private Button _roomsPartyButton;
        [SerializeField] private Button _partyBrowseButton;
        [SerializeField] private Button _partyLeaveButton;
        [SerializeField] private Button _partyReadyButton;
        [SerializeField] private Button _partyBotsButton;
        [SerializeField] private Button _partyStartButton;
        [SerializeField] private Button _partyRetryButton;
        [SerializeField] private Button[] _preferenceButtons;

        private AccountRoomList _roomList;
        private AccountRoom _currentRoom;
        private CancellationTokenSource _lifetime;
        private CancellationTokenSource _pulseCancellation;
        private float _nextPulse;
        private bool _busy;
        private bool _pulseInFlight;
        private int _pulseRevision;
        private bool _connectionAttempted;
        private string _screen = "hidden";
        private string _status = string.Empty;
        private readonly List<Button> _roomRows = new List<Button>();
        private readonly List<AccountRoom> _rowRooms = new List<AccountRoom>();
        private NetworkManager _hudOwner;
        private NetworkManagerHUD _hud;
        private TMP_Text _readyLabel;
        private TMP_Text _botsLabel;
        private TitleIntent _pendingIntent;
        private string _pendingRoomName;
        private bool _inputWanted;
        private Scene _inputScene;
        private bool _nameErrorShown;

        public static AccountLobbyPanel Instance { get; private set; }
        public string ScreenName => _screen;
        public bool IsVisible => (_canvasGroup != null) && (_canvasGroup.blocksRaycasts);
        public bool FrontendInputActive => (_frontendEventSystem != null) && (_frontendEventSystem.activeInHierarchy);
        public string StatusMessage => _status;
        public string CurrentRoomId => _currentRoom?.room_id ?? string.Empty;
        public int RoomCount => _roomList?.rooms?.Length ?? 0;
        public int PartyMemberCount => _currentRoom?.members?.Length ?? 0;
        public bool IsBusy => (_busy) || (LauncherGameSession.ConnectionPending);
        public AccountRoom[] Rooms => _roomList?.rooms ?? Array.Empty<AccountRoom>();
        public AccountRoom CurrentRoom => _currentRoom;

        private void Awake()
        {
            if ((Instance != null) && (Instance != this))
            {
                Debug.LogError("[AccountLobbyPanel] 계정 로비 화면이 중복 생성되었다", this);
                enabled = false;
                return;
            }
            Instance = this;
            _lifetime = new CancellationTokenSource();
            if (!HasRequiredReferences())
            {
                enabled = false;
                return;
            }

            _roomsRefreshButton.onClick.AddListener(() => RefreshRooms());
            _roomsCreateButton.onClick.AddListener(() => CreateRoom(_roomNameInput.text));
            _roomsBackButton.onClick.AddListener(() => ShowHome());
            _roomsPartyButton.onClick.AddListener(() => ShowParty());
            _partyBrowseButton.onClick.AddListener(() => ShowRooms());
            _partyLeaveButton.onClick.AddListener(() => LeaveRoom());
            _partyReadyButton.onClick.AddListener(() => SetReady(!LocalMemberReady()));
            _partyBotsButton.onClick.AddListener(() => SetBots(!(_currentRoom?.fill_with_bots ?? false)));
            _partyStartButton.onClick.AddListener(() => StartRoom());
            _partyRetryButton.onClick.AddListener(() => RetryConnection());
            _readyLabel = _partyReadyButton.GetComponentInChildren<TMP_Text>();
            _botsLabel = _partyBotsButton.GetComponentInChildren<TMP_Text>();
            for (int i = 0; i < _preferenceButtons.Length; ++i)
            {
                int preference = i;
                _preferenceButtons[i].onClick.AddListener(() => SelectPreference(preference));
            }
            _roomRowTemplate.gameObject.SetActive(false);
            _roomNameInput.characterLimit = MAX_ROOM_NAME_LENGTH;
            LauncherGameSession.ReturnedToLobby += OnReturnedToLobby;
            FixedAspectViewport.Register(_canvas);
            _frontendEventSystem.SetActive(false);
            ShowScreen("loading");
            SetVisible(false);
        }

        private void OnDestroy()
        {
            LauncherGameSession.ReturnedToLobby -= OnReturnedToLobby;
            InvalidatePulse();
            _lifetime?.Cancel();
            _lifetime?.Dispose();
            _lifetime = null;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (!LauncherGameSession.IsFrontendMode)
            {
                SetVisible(false);
                UpdateFrontendInput(false);
                return;
            }

            // The Boot prewarmer may restore this HUD after its own completion.
            NetworkManager manager = NetworkManager.singleton;
            if (manager != _hudOwner)
            {
                _hudOwner = manager;
                _hud = manager == null ? null : manager.GetComponent<NetworkManagerHUD>();
            }
            if ((_hud != null) && (_hud.enabled))
            {
                _hud.enabled = false;
            }

            string sceneName = SceneManager.GetActiveScene().name;
            bool lobbyScene = sceneName == Constants.Scene.LOBBY;
            bool bootScene = sceneName == Constants.Scene.BOOT;
            bool titleScene = sceneName == Constants.Scene.TITLE;
            bool connected = (NetworkClient.active) || (NetworkServer.active);
            UpdateFrontendInput(((lobbyScene) || (bootScene) || (titleScene)) && (!connected));
            if (((!lobbyScene) && (!bootScene) && (!titleScene)) || (connected))
            {
                SetVisible(false);
                if (connected)
                {
                    _screen = "hidden";
                }
                return;
            }

            if (LauncherGameSession.ConnectionPending)
            {
                SetVisible(true);
                ShowScreen("connecting");
                SetStatus("원정에 입장하는 중입니다.");
                return;
            }
            if ((LauncherGameSession.State == LauncherGameSession.SessionState.Loading) || (bootScene))
            {
                SetVisible(true);
                ShowScreen("loading");
                SetStatus("계정과 게임을 준비하는 중입니다.");
                return;
            }
            if (LauncherGameSession.State == LauncherGameSession.SessionState.Failed)
            {
                SetVisible(true);
                ShowScreen("failed");
                SetStatus("계정 연결을 확인할 수 없습니다. 런처에서 다시 시작해 주세요.");
                return;
            }

            if (titleScene)
            {
                // Title 씬의 메뉴가 첫 화면이다. 이 화면은 투명하게 두고 상태 문구만 넘긴다.
                SetVisible(false);
                if (_screen != "title")
                {
                    bool arrived = (_screen == "hidden") || (_screen == "loading");
                    ShowScreen("title");
                    if (arrived)
                    {
                        SetStatus(_currentRoom == null
                            ? "원정대를 찾거나 새로 만들어 출발하세요."
                            : "참가 중인 원정대가 있습니다. 원정대 찾기에서 확인하세요.");
                    }
                }
            }
            else
            {
                SetVisible(true);
                if (_pendingIntent != TitleIntent.None)
                {
                    if (CanNavigate())
                    {
                        RunPendingIntent();
                    }
                }
                else if ((_screen == "hidden") || (_screen == "loading") || (_screen == "connecting") ||
                    (_screen == "failed") || (_screen == "title"))
                {
                    ShowScreen(_currentRoom == null ? "rooms" : "party");
                    if ((_currentRoom == null) && (_roomList == null))
                    {
                        RefreshRooms();
                    }
                }
            }
            if ((_currentRoom != null) && (!_busy) && (!_pulseInFlight) &&
                (Time.unscaledTime >= _nextPulse))
            {
                _nextPulse = Time.unscaledTime + PARTY_PULSE_SECONDS;
                StartPulse();
            }
        }

        private void StartPulse()
        {
            string roomId = _currentRoom.room_id;
            int revision = ++_pulseRevision;
            _pulseInFlight = true;
            _pulseCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            PulseAsync(roomId, revision, _pulseCancellation);
        }

        private async void PulseAsync(string roomId, int revision, CancellationTokenSource cancellation)
        {
            try
            {
                AccountRoom updated = await LauncherGameSession.LobbyAsync(roomId, "pulse",
                    ct: cancellation.Token);
                ApplyPulseIfCurrent(updated, roomId, revision, cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (AccountRoomException exception)
            {
                if (IsPulseCurrent(roomId, revision, cancellation.Token))
                {
                    SetStatus(exception.Message);
                }
            }
            catch (Exception)
            {
                if (IsPulseCurrent(roomId, revision, cancellation.Token))
                {
                    SetStatus("원정대 상태를 확인하지 못했습니다. 잠시 후 다시 시도해 주세요.");
                }
            }
            finally
            {
                if (_pulseCancellation == cancellation)
                {
                    _pulseCancellation = null;
                }
                _pulseInFlight = false;
                cancellation.Dispose();
            }
        }

        private bool IsPulseCurrent(string roomId, int revision, CancellationToken token)
        {
            return (!token.IsCancellationRequested) && (this != null) && (!_busy) &&
                (_pulseRevision == revision) && (_currentRoom?.room_id == roomId);
        }

        private bool ApplyPulseIfCurrent(AccountRoom updated, string roomId, int revision,
            CancellationToken token)
        {
            if ((!IsPulseCurrent(roomId, revision, token)) || (updated?.room_id != roomId))
            {
                return false;
            }
            _currentRoom = updated;
            RenderParty();
            TryConnectCurrentRoom();
            return true;
        }

        private void InvalidatePulse()
        {
            ++_pulseRevision;
            _pulseCancellation?.Cancel();
        }

        /// <summary>방 목록에서 Title 첫 화면으로 돌아간다. 참가 중인 파티는 유지한다.</summary>
        public bool ShowHome()
        {
            if (!CanNavigate())
            {
                return false;
            }
            ShowScreen("title");
            return LauncherGameSession.OpenFrontendScene(Constants.Scene.TITLE);
        }

        /// <summary>Title 메뉴의 원정대 찾기. Lobby 씬으로 넘어간 뒤 목록을 새로 받는다.</summary>
        public bool OpenRoomsFromTitle()
        {
            if (!CanLeaveTitle())
            {
                return false;
            }
            _pendingIntent = TitleIntent.Rooms;
            _pendingRoomName = null;
            return OpenLobbyForIntent();
        }

        /// <summary>Title 메뉴의 원정대 만들기. 이름을 먼저 검사하고 Lobby 씬에서 생성한다.</summary>
        public bool CreateRoomFromTitle(string name)
        {
            if (!CanLeaveTitle())
            {
                return false;
            }
            if (!TryNormalizeRoomName(name, out string roomName))
            {
                return false;
            }
            _pendingIntent = TitleIntent.Create;
            _pendingRoomName = roomName;
            return OpenLobbyForIntent();
        }

        /// <summary>검증 도구가 실제 포인터 경로로 누를 조작 요소. 화면에 없으면 null.</summary>
        public Selectable FindControl(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }
            const string ROW = "rooms.row:";
            const string PREFERENCE = "party.pref:";
            if (id.StartsWith(ROW, StringComparison.Ordinal))
            {
                string roomId = id.Substring(ROW.Length);
                for (int i = 0; i < _rowRooms.Count; ++i)
                {
                    if ((_rowRooms[i] != null) && (_rowRooms[i].room_id == roomId))
                    {
                        return _roomRows[i];
                    }
                }
                return null;
            }
            if (id.StartsWith(PREFERENCE, StringComparison.Ordinal))
            {
                return (int.TryParse(id.Substring(PREFERENCE.Length), out int index)) &&
                    (index >= 0) && (index < _preferenceButtons.Length) ? _preferenceButtons[index] : null;
            }
            switch (id)
            {
                case "rooms.refresh": return _roomsRefreshButton;
                case "rooms.create": return _roomsCreateButton;
                case "rooms.back": return _roomsBackButton;
                case "rooms.party": return _roomsPartyButton;
                case "rooms.input": return _roomNameInput;
                case "party.browse": return _partyBrowseButton;
                case "party.leave": return _partyLeaveButton;
                case "party.ready": return _partyReadyButton;
                case "party.bots": return _partyBotsButton;
                case "party.start": return _partyStartButton;
                case "party.retry": return _partyRetryButton;
                default: return null;
            }
        }

        private bool OpenLobbyForIntent()
        {
            if (LauncherGameSession.OpenFrontendScene(Constants.Scene.LOBBY))
            {
                return true;
            }
            _pendingIntent = TitleIntent.None;
            _pendingRoomName = null;
            return false;
        }

        private void RunPendingIntent()
        {
            TitleIntent intent = _pendingIntent;
            string roomName = _pendingRoomName;
            _pendingIntent = TitleIntent.None;
            _pendingRoomName = null;
            ShowScreen("rooms");
            if (intent == TitleIntent.Create)
            {
                _roomNameInput.text = roomName;
                CreateRoom(roomName);
            }
            else
            {
                RefreshRooms();
            }
        }

        public bool ShowRooms()
        {
            if (!CanNavigate())
            {
                return false;
            }
            ShowScreen("rooms");
            if (_roomList == null)
            {
                RefreshRooms();
            }
            return true;
        }

        public bool ShowParty()
        {
            if ((!CanNavigate()) || (_currentRoom == null))
            {
                return false;
            }
            ShowScreen("party");
            RenderParty();
            TryConnectCurrentRoom();
            return true;
        }

        public bool RefreshRooms()
        {
            return RunOperation("목록 새로고침", async token =>
            {
                _roomList = await LauncherGameSession.ListRoomsAsync(token);
                // Recover a still-waiting membership after returning from gameplay.
                if (_currentRoom == null)
                {
                    foreach (AccountRoom room in Rooms)
                    {
                        if ((room != null) && (room.state == "waiting") && (IsCurrentAccountMember(room)))
                        {
                            _currentRoom = room;
                            _nextPulse = Time.unscaledTime + PARTY_PULSE_SECONDS;
                            break;
                        }
                    }
                }
                RenderRooms();
                SetStatus(RoomCount == 0 ? "지금 열린 원정대가 없습니다." : "원정대 목록을 새로고침했습니다.");
            });
        }

        public bool CreateRoom(string name)
        {
            if (!TryNormalizeRoomName(name, out string roomName))
            {
                return false;
            }
            return RunOperation("원정대 만들기", async token =>
            {
                AccountRoom created = await LauncherGameSession.CreateRoomAsync(roomName, token);
                _currentRoom = created;
                _connectionAttempted = false;
                _nextPulse = Time.unscaledTime + PARTY_PULSE_SECONDS;
                ShowScreen("party");
                RenderParty();
                SetStatus("원정대를 만들었습니다.");
            });
        }

        public bool JoinRoom(string roomId)
        {
            if (string.IsNullOrWhiteSpace(roomId))
            {
                return false;
            }
            if ((_currentRoom != null) && (_currentRoom.room_id == roomId))
            {
                return ShowParty();
            }
            AccountRoom listed = FindListedRoom(roomId);
            if (listed == null)
            {
                SetStatus("원정대 목록을 새로고침해 주세요.");
                return false;
            }
            if ((_roomList?.build_id != null) && (listed.build_id != _roomList.build_id))
            {
                SetStatus("이 원정대는 현재 게임 버전으로 입장할 수 없습니다. 런처에서 업데이트해 주세요.");
                return false;
            }
            if ((listed != null) && (IsCurrentAccountMember(listed)) &&
                ((listed.state == "ready") || (listed.state == "running")))
            {
                if (!CanNavigate())
                {
                    return false;
                }
                _currentRoom = listed;
                _connectionAttempted = false;
                ShowScreen("party");
                RenderParty();
                TryConnectCurrentRoom();
                return true;
            }
            if ((listed.state != "waiting") || ((_currentRoom != null) && (_currentRoom.state != "waiting")))
            {
                SetStatus("지금은 이 원정대에 참가할 수 없습니다.");
                return false;
            }
            return RunOperation("원정대 참가", async token =>
            {
                // The server moves membership atomically; retain the current party on failure.
                AccountRoom joined = await LauncherGameSession.LobbyAsync(roomId, "join", ct: token);
                _currentRoom = joined;
                _connectionAttempted = false;
                _nextPulse = Time.unscaledTime + PARTY_PULSE_SECONDS;
                ShowScreen("party");
                RenderParty();
                SetStatus("원정대에 참가했습니다.");
                TryConnectCurrentRoom();
            });
        }

        public bool SelectPreference(int index)
        {
            if ((index < 0) || (index >= WEAPON_PREFERENCE_COUNT) || (!CanChangeParty()))
            {
                return false;
            }
            return ChangeParty("preference", index, false, "선호 무기를 변경했습니다.");
        }

        public bool SetReady(bool ready)
        {
            if (!CanChangeParty())
            {
                return false;
            }
            return ChangeParty("ready", null, ready, ready ? "준비를 완료했습니다." : "준비를 취소했습니다.");
        }

        public bool SetBots(bool enabled)
        {
            if ((!CanChangeParty()) || (!IsOwner()))
            {
                return false;
            }
            return ChangeParty("bots", null, enabled, enabled ? "빈자리를 봇으로 채웁니다." : "봇 충원을 취소했습니다.");
        }

        public bool StartRoom()
        {
            if ((!CanChangeParty()) || (!IsOwner()) || (!AllMembersReady()))
            {
                return false;
            }
            return RunOperation("원정 출발", async token =>
            {
                _currentRoom = await LauncherGameSession.LobbyAsync(_currentRoom.room_id, "start", ct: token);
                RenderParty();
                SetStatus("원정을 준비하는 중입니다.");
                TryConnectCurrentRoom();
            });
        }

        public bool LeaveRoom()
        {
            if (!CanChangeParty())
            {
                return false;
            }
            return RunOperation("원정대 나가기", async token =>
            {
                await LauncherGameSession.LobbyAsync(_currentRoom.room_id, "leave", ct: token);
                _currentRoom = null;
                _connectionAttempted = false;
                ShowScreen("rooms");
                RenderRooms();
                SetStatus("원정대에서 나왔습니다.");
            });
        }

        public bool ReturnToLobby()
        {
            if (!LauncherGameSession.IsFrontendMode)
            {
                return false;
            }
            LauncherGameSession.ReturnToLobby();
            return true;
        }

        public bool RetryConnection()
        {
            if ((!CanNavigate()) || (_busy) || (!_connectionAttempted) || (_currentRoom == null))
            {
                return false;
            }
            _connectionAttempted = false;
            TryConnectCurrentRoom();
            return _connectionAttempted;
        }

        private bool ChangeParty(string action, int? preference, bool enabled, string success)
        {
            return RunOperation("파티 변경", async token =>
            {
                _currentRoom = await LauncherGameSession.LobbyAsync(_currentRoom.room_id,
                    action, preference, enabled, token);
                RenderParty();
                SetStatus(success);
                TryConnectCurrentRoom();
            });
        }

        private bool RunOperation(string operation, Func<CancellationToken, Task> work)
        {
            if ((_busy) || (_lifetime == null) || (!LauncherGameSession.IsFrontendMode) ||
                (LauncherGameSession.State != LauncherGameSession.SessionState.Ready) ||
                (LauncherGameSession.ConnectionPending) || (NetworkClient.active) || (NetworkServer.active))
            {
                return false;
            }
            // 사용자 조작이 시작되면 늦게 도착한 pulse 응답은 현재 방을 덮어쓰지 못한다.
            InvalidatePulse();
            _busy = true;
            SetStatus(operation + " 중입니다.");
            RenderControls();
            CompleteOperation(work, operation, _lifetime.Token);
            return true;
        }

        private async void CompleteOperation(Func<CancellationToken, Task> work,
            string operation, CancellationToken token)
        {
            try
            {
                await work(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (AccountRoomException exception)
            {
                if (!token.IsCancellationRequested)
                {
                    SetStatus(exception.Message);
                }
            }
            catch (Exception)
            {
                if (!token.IsCancellationRequested)
                {
                    SetStatus(operation + "에 실패했습니다. 잠시 후 다시 시도해 주세요.");
                }
            }
            finally
            {
                if ((!token.IsCancellationRequested) && (this != null))
                {
                    _busy = false;
                    RenderControls();
                }
            }
        }

        private void TryConnectCurrentRoom()
        {
            if ((_connectionAttempted) || (_currentRoom == null) || (LauncherGameSession.ConnectionPending))
            {
                return;
            }
            bool launchable = (_currentRoom.state == "ready") || (_currentRoom.state == "running") ||
                ((_currentRoom.state == "hosting") && (_currentRoom.hosting == "listen") && (IsOwner()));
            if ((!launchable) || (!IsMember()))
            {
                return;
            }
            _connectionAttempted = true;
            RunConnection(_currentRoom, _lifetime.Token);
        }

        private async void RunConnection(AccountRoom room, CancellationToken token)
        {
            ShowScreen("connecting");
            SetStatus("원정에 입장하는 중입니다.");
            try
            {
                await LauncherGameSession.ConnectRoomAsync(room, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (AccountRoomException exception)
            {
                if (!token.IsCancellationRequested)
                {
                    SetStatus(exception.Message);
                }
            }
            catch (Exception)
            {
                if (!token.IsCancellationRequested)
                {
                    SetStatus("원정에 입장하지 못했습니다. 다시 시도해 주세요.");
                }
            }
            finally
            {
                // 연결 실패로 Title에 돌아갔다면 파티가 비어 있으므로 파티 화면을 다시 열지 않는다.
                if ((!token.IsCancellationRequested) && (this != null) && (_currentRoom != null) &&
                    (!LauncherGameSession.ConnectionPending) && (!NetworkClient.active) && (!NetworkServer.active))
                {
                    ShowScreen("party");
                    RenderParty();
                }
            }
        }

        private void OnReturnedToLobby(string message)
        {
            InvalidatePulse();
            _currentRoom = null;
            _roomList = null;
            _busy = false;
            _connectionAttempted = false;
            _nextPulse = 0f;
            _pendingIntent = TitleIntent.None;
            _pendingRoomName = null;
            ShowScreen("title");
            SetStatus(string.IsNullOrWhiteSpace(message) ? "로비로 돌아왔습니다." : message);
        }

        private bool CanLeaveTitle()
        {
            return (!_busy) && (!LauncherGameSession.ConnectionPending) &&
                (LauncherGameSession.IsFrontendMode) &&
                (LauncherGameSession.State == LauncherGameSession.SessionState.Ready) &&
                (SceneManager.GetActiveScene().name == Constants.Scene.TITLE) &&
                (!NetworkClient.active) && (!NetworkServer.active);
        }

        /// <summary>Title 메뉴 버튼 활성 여부. 다른 요청이나 연결이 진행 중이면 막는다.</summary>
        public bool CanUseTitleMenu => CanLeaveTitle();

        private bool TryNormalizeRoomName(string name, out string roomName)
        {
            roomName = string.IsNullOrWhiteSpace(name) ? "새 원정대" : name.Trim();
            if ((roomName.Length >= MIN_ROOM_NAME_LENGTH) && (roomName.Length <= MAX_ROOM_NAME_LENGTH))
            {
                return true;
            }
            SetStatus($"원정대 이름은 {MIN_ROOM_NAME_LENGTH}~{MAX_ROOM_NAME_LENGTH}자로 입력해 주세요.");
            _nameErrorShown = true;
            return false;
        }

        /// <summary>새 이름 입력을 시작할 때 이전 이름 오류만 지운다. 세션, 연결 오류 문구는 유지한다.</summary>
        public void ClearRoomNameError()
        {
            if (_nameErrorShown)
            {
                SetStatus("원정대 이름을 정하고 만들기를 누르세요.");
            }
        }

        /// <summary>
        /// 오프라인 계정 화면에서만 자체 EventSystem을 켠다. Title 씬이나 복구된 RunEventSystem처럼
        /// 다른 EventSystem이 이미 켜져 있으면 양보한다. 씬, 연결 상태가 바뀔 때만 다시 검사한다.
        /// </summary>
        private void UpdateFrontendInput(bool wanted)
        {
            Scene scene = SceneManager.GetActiveScene();
            if ((wanted == _inputWanted) && (scene == _inputScene))
            {
                return;
            }
            _inputWanted = wanted;
            _inputScene = scene;
            bool enable = (wanted) && (!HasOtherEventSystem());
            if (_frontendEventSystem.activeSelf != enable)
            {
                _frontendEventSystem.SetActive(enable);
            }
        }

        private bool HasOtherEventSystem()
        {
            EventSystem[] systems = FindObjectsByType<EventSystem>();
            for (int i = 0; i < systems.Length; ++i)
            {
                if ((systems[i] != null) && (systems[i].gameObject != _frontendEventSystem) &&
                    (systems[i].isActiveAndEnabled))
                {
                    return true;
                }
            }
            return false;
        }

        private bool CanNavigate()
        {
            return (!_busy) && (!LauncherGameSession.ConnectionPending) &&
                (LauncherGameSession.IsFrontendMode) &&
                (LauncherGameSession.State == LauncherGameSession.SessionState.Ready) &&
                (SceneManager.GetActiveScene().name == Constants.Scene.LOBBY) &&
                (!NetworkClient.active) && (!NetworkServer.active);
        }

        private bool CanChangeParty()
        {
            return (CanNavigate()) && (_currentRoom != null) && (_currentRoom.state == "waiting") && (IsMember());
        }

        private bool IsOwner()
        {
            return (_currentRoom != null) && (_currentRoom.owner_id == LauncherGameSession.AccountId);
        }

        private bool IsMember()
        {
            return IsCurrentAccountMember(_currentRoom);
        }

        private static bool IsCurrentAccountMember(AccountRoom room)
        {
            AccountRoomMember[] members = room?.members;
            if (members == null)
            {
                return false;
            }
            for (int i = 0; i < members.Length; ++i)
            {
                if ((members[i] != null) && (members[i].account_id == LauncherGameSession.AccountId))
                {
                    return true;
                }
            }
            return false;
        }

        private AccountRoom FindListedRoom(string roomId)
        {
            AccountRoom[] rooms = Rooms;
            for (int i = 0; i < rooms.Length; ++i)
            {
                if ((rooms[i] != null) && (rooms[i].room_id == roomId))
                {
                    return rooms[i];
                }
            }
            return null;
        }

        private bool LocalMemberReady()
        {
            AccountRoomMember[] members = _currentRoom?.members;
            if (members == null)
            {
                return false;
            }
            for (int i = 0; i < members.Length; ++i)
            {
                if ((members[i] != null) && (members[i].account_id == LauncherGameSession.AccountId))
                {
                    return members[i].ready;
                }
            }
            return false;
        }

        private bool AllMembersReady()
        {
            AccountRoomMember[] members = _currentRoom?.members;
            if ((members == null) || (members.Length == 0))
            {
                return false;
            }
            if ((!_currentRoom.fill_with_bots) && (members.Length != _currentRoom.max_players))
            {
                return false;
            }
            for (int i = 0; i < members.Length; ++i)
            {
                if ((members[i] == null) || (!members[i].ready) || (!members[i].preference.HasValue))
                {
                    return false;
                }
            }
            return true;
        }

        private int LocalPreference()
        {
            AccountRoomMember[] members = _currentRoom?.members;
            if (members == null)
            {
                return -1;
            }
            for (int i = 0; i < members.Length; ++i)
            {
                if ((members[i] != null) && (members[i].account_id == LauncherGameSession.AccountId))
                {
                    return members[i].preference ?? -1;
                }
            }
            return -1;
        }

        private void RenderRooms()
        {
            if (_roomListContent == null)
            {
                return;
            }
            _roomRows.Clear();
            _rowRooms.Clear();
            for (int i = _roomListContent.childCount - 1; i >= 0; --i)
            {
                Transform child = _roomListContent.GetChild(i);
                if (child.gameObject != _roomRowTemplate.gameObject)
                {
                    Destroy(child.gameObject);
                }
            }
            AccountRoom[] rooms = Rooms;
            _roomSummaryText.text = rooms.Length == 0 ? "열린 원정대가 없습니다." : $"원정대 {rooms.Length}개";
            for (int i = 0; i < rooms.Length; ++i)
            {
                AccountRoom room = rooms[i];
                if (room == null)
                {
                    continue;
                }
                Button row = Instantiate(_roomRowTemplate, _roomListContent);
                row.name = "RoomRow_" + i;
                row.gameObject.SetActive(true);
                Transform titleObject = row.transform.Find("RoomName");
                Transform detailsObject = row.transform.Find("RoomDetails");
                TMP_Text title = titleObject == null ? null : titleObject.GetComponent<TMP_Text>();
                TMP_Text details = detailsObject == null ? null : detailsObject.GetComponent<TMP_Text>();
                if (title != null)
                {
                    title.text = string.IsNullOrWhiteSpace(room.name) ? "이름 없는 원정대" : room.name;
                }
                int members = room.members?.Length ?? 0;
                if (details != null)
                {
                    details.text = $"{members}/{room.max_players}명  |  " +
                        ((_roomList?.build_id != null) && (room.build_id != _roomList.build_id)
                            ? "업데이트 필요" : RoomStateLabel(room.state));
                }
                _roomRows.Add(row);
                _rowRooms.Add(room);
                string roomId = room.room_id;
                row.onClick.AddListener(() => JoinRoom(roomId));
            }
            RenderControls();
        }

        private void RenderParty()
        {
            if (_currentRoom == null)
            {
                return;
            }
            _partyTitleText.text = string.IsNullOrWhiteSpace(_currentRoom.name)
                ? "원정대" : _currentRoom.name;
            AccountRoomMember[] members = _currentRoom.members ?? Array.Empty<AccountRoomMember>();
            var lines = new System.Text.StringBuilder();
            int slots = Mathf.Clamp(_currentRoom.max_players, MIN_PARTY_SLOTS, MAX_PARTY_SLOTS);
            for (int i = 0; i < slots; ++i)
            {
                if ((i < members.Length) && (members[i] != null))
                {
                    AccountRoomMember member = members[i];
                    string nickname = string.IsNullOrWhiteSpace(member.nickname) ? "참가자" : member.nickname;
                    lines.Append(i + 1).Append("  ").Append(nickname);
                    if (member.is_owner)
                    {
                        lines.Append(", 방장");
                    }
                    lines.Append("  /  ").Append(PreferenceLabel(member.preference));
                    lines.Append(member.ready ? ", 준비 완료" : ", 준비 중");
                }
                else
                {
                    lines.Append(i + 1).Append(_currentRoom.fill_with_bots ? "  봇 참가 예정" : "  빈자리");
                }
                if (i + 1 < slots)
                {
                    lines.AppendLine();
                }
            }
            _partyMembersText.text = lines.ToString();
            _partyHintText.text = _currentRoom.state == "waiting"
                ? "모두 무기를 고르고 준비하세요. 빈자리는 봇으로 채우거나 3명이 모이면 출발할 수 있습니다."
                : "원정을 준비하는 중입니다.";
            RenderControls();
        }

        private void RenderControls()
        {
            bool navigation = CanNavigate();
            bool changing = (CanChangeParty()) && (!_busy);
            _roomsRefreshButton.interactable = navigation;
            _roomsCreateButton.interactable = navigation;
            _roomsBackButton.interactable = navigation;
            _roomsPartyButton.gameObject.SetActive(_currentRoom != null);
            _roomsPartyButton.interactable = navigation;
            _roomNameInput.interactable = navigation;
            _partyBrowseButton.interactable = navigation;
            _partyLeaveButton.interactable = (navigation) && (_currentRoom != null) && (_currentRoom.state == "waiting");
            _partyReadyButton.interactable = changing;
            _partyBotsButton.interactable = (changing) && (IsOwner());
            _partyStartButton.interactable = (changing) && (IsOwner()) && (AllMembersReady());
            _partyRetryButton.gameObject.SetActive((_connectionAttempted) &&
                (!LauncherGameSession.ConnectionPending) && (!NetworkClient.active) && (!NetworkServer.active));
            _partyRetryButton.interactable = (navigation) && (!_busy);
            _readyLabel.text = LocalMemberReady() ? "준비 취소" : "준비 완료";
            _botsLabel.text = (_currentRoom?.fill_with_bots ?? false)
                ? "봇 충원 취소" : "빈자리 봇 추가";
            int selected = LocalPreference();
            for (int i = 0; i < _preferenceButtons.Length; ++i)
            {
                _preferenceButtons[i].interactable = changing;
                _preferenceButtons[i].image.color = selected == i
                    ? SELECTED_PREFERENCE_COLOR : UNSELECTED_PREFERENCE_COLOR;
            }
            for (int i = 0; i < _roomRows.Count; ++i)
            {
                AccountRoom room = _rowRooms[i];
                bool reconnect = (IsCurrentAccountMember(room)) &&
                    ((room.state == "ready") || (room.state == "running"));
                bool canSwitch = (_currentRoom == null) || (_currentRoom.state == "waiting") ||
                    (room.room_id == _currentRoom.room_id);
                bool matchingBuild = (_roomList == null) || (_roomList.build_id == null) ||
                    (room.build_id == _roomList.build_id);
                _roomRows[i].interactable = (navigation) && (canSwitch) && (matchingBuild) &&
                    ((room.state == "waiting") || (reconnect));
            }
        }

        private void ShowScreen(string screen)
        {
            _screen = screen;
            _loadingPage.SetActive((screen == "loading") || (screen == "connecting") || (screen == "failed"));
            _roomsPage.SetActive(screen == "rooms");
            _partyPage.SetActive(screen == "party");
            _identityText.text = string.IsNullOrWhiteSpace(LauncherGameSession.Nickname)
                ? "원정대" : LauncherGameSession.Nickname + " 님";
            RenderControls();
        }

        private void SetStatus(string message)
        {
            _status = message ?? string.Empty;
            _statusText.text = _status;
            _nameErrorShown = false;
        }

        private void SetVisible(bool visible)
        {
            _canvasGroup.alpha = visible ? 1f : 0f;
            _canvasGroup.interactable = visible;
            _canvasGroup.blocksRaycasts = visible;
        }

        private static string PreferenceLabel(int? preference)
        {
            switch (preference)
            {
                case 0: return "검, 차사";
                case 1: return "활, 신궁";
                case 2: return "방울, 무녀";
                default: return "무기 미선택";
            }
        }

        private static string RoomStateLabel(string state)
        {
            switch (state)
            {
                case "waiting": return "모집 중";
                case "queued": return "출발 대기";
                case "starting": return "출발 준비";
                case "hosting": return "호스트 준비";
                case "ready": return "입장 가능";
                case "running": return "진행 중";
                default: return "상태 확인 중";
            }
        }

        private bool HasRequiredReferences()
        {
            bool complete = (_canvas != null) && (_canvasGroup != null) && (_frontendEventSystem != null) &&
                (_loadingPage != null) && (_roomsPage != null) && (_partyPage != null) &&
                (_statusText != null) && (_identityText != null) && (_roomSummaryText != null) &&
                (_partyTitleText != null) && (_partyMembersText != null) && (_partyHintText != null) &&
                (_roomNameInput != null) && (_roomListContent != null) && (_roomRowTemplate != null) &&
                (_roomsRefreshButton != null) && (_roomsCreateButton != null) && (_roomsBackButton != null) &&
                (_roomsPartyButton != null) && (_partyBrowseButton != null) && (_partyLeaveButton != null) &&
                (_partyReadyButton != null) && (_partyBotsButton != null) && (_partyStartButton != null) &&
                (_partyRetryButton != null) &&
                (_preferenceButtons != null) && (_preferenceButtons.Length == WEAPON_PREFERENCE_COUNT);
            if (!complete)
            {
                Debug.LogError("[AccountLobbyPanel] 화면 참조가 빠졌다", this);
            }
            return complete;
        }
    }
}
