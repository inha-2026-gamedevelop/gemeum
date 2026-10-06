using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;
using Newtonsoft.Json;
using Geumeum.Art;
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary> 런처 세션 수신/검증/갱신. 게임 서버의 신원 검증을 대신하지 않는다 </summary>
    public sealed class LauncherGameSession : MonoBehaviour
    {
        private const int    MIN_ROOM_NAME_LENGTH          = 2;
        private const int    MAX_ROOM_NAME_LENGTH          = 16;
        private const int    MAX_LISTED_ROOMS              = 50;     // 서버 목록 응답 상한
        private const int    MAX_WEAPON_PREFERENCE         = 2;      // 검, 활, 방울 인덱스 0..2
        private const int    ROOM_JSON_MAX_DEPTH           = 12;
        private const int    MIN_ROOM_PORT                 = 1024;
        private const int    MAX_ROOM_PORT                 = 65535;
        private const int    MAX_RESPONSE_BYTES            = 131072; // 방 목록 응답까지 담는 상한
        private const double ACCESS_REFRESH_MARGIN_SECONDS = 40;
        private const double CONNECT_TIMEOUT_SECONDS       = 40;
        private const int    CONNECT_POLL_MILLISECONDS     = 100;
        private const int    CLOSE_ROOM_ATTEMPTS           = 3;
        private const int    CLOSE_RETRY_BASE_MILLISECONDS = 250;
        private const float  DISCONNECT_WAIT_SECONDS       = 10f;

        public enum SessionState { Disabled = 0, Loading = 1, Ready = 2, Failed = 3 }
        public static SessionState State { get; private set; }
        public static string AccountId { get; private set; }
        public static string Nickname { get; private set; }
        public static string RoomId { get; private set; }
        public static bool IsFrontendMode { get; private set; }
        public static bool ConnectionPending => (_instance != null) && (_instance._connectionPending);
        public static bool PendingRoomClosure => (_instance != null) && (_instance._pendingCloseRoomId != null);
        public static event Action<string> ReturnedToLobby;
        private static LauncherGameSession _instance;
        private readonly SemaphoreSlim _requests = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _lifetime;
        private HttpClient _http;
        private Envelope _session;
        private DateTimeOffset _accessExpires;
        private DateTimeOffset _sessionExpires;
        private bool _connectionPending;
        private bool _returningToLobby;
        private int _connectionRevision;
        private bool _connectedAsListenHost;
        private string _pendingCloseRoomId;

        [Serializable] private sealed class Envelope
        {
            public int version;
            public string api_origin;
            public string access_token;
            public string refresh_token;
            public string account_id;
            public int expires_in;
            public string session_expires_at;
            public string room_id;
            public string address;
            public int port;
            public bool is_host;
        }
        [Serializable] private sealed class Profile { public string account_id; public string nickname; public string status; }
        [Serializable] private sealed class Tokens
        {
            public string access_token;
            public string refresh_token;
            public string session_kind;
            public string token_type;
            public int expires_in;
            public string session_expires_at;
            public Profile account;
        }
        [Serializable] private sealed class Refresh { public string refresh_token; }
        [Serializable] private sealed class TicketInput { public string challenge; public string build_id; }
        [Serializable] private sealed class TicketReply { public string ticket; }
        [Serializable] private sealed class HostReply { public string server_key; public int port; public AccountRelayCredentials relay; }
        [Serializable] private sealed class LobbyInput
        {
            public string action;
            public int? preference;
            public bool enabled;
        }
        [Serializable] private sealed class CreateRoomInput
        {
            public string name;
            public string build_id;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            State = SessionState.Disabled;
            AccountId = null;
            Nickname = null;
            RoomId = null;
            IsFrontendMode = false;
            ReturnedToLobby = null;
            _instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-account-pipe") < 0)
            {
                return;
            }

            State = SessionState.Loading;
            var owner = new GameObject("LauncherGameSession");
            DontDestroyOnLoad(owner);
            _instance = owner.AddComponent<LauncherGameSession>();
        }

        private async void Start()
        {
            _lifetime = new CancellationTokenSource();
            GeumeumNetworkManager.ClientDisconnected += OnClientDisconnected;
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                string pipe = Value(args, "-account-pipe");
                if (!int.TryParse(Value(args, "-launcher-pid"), out int launcherPid))
                {
                    throw new IOException();
                }

                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(120));
                    string json = await GameSessionPipeClient.ReceiveAsync(pipe, launcherPid, deadline.Token);
                    _session = JsonUtility.FromJson<Envelope>(json);
                    // 방 없는 런처 세션은 검증 실패도 게임 안의 실패 화면에 표시한다.
                    IsFrontendMode = (_session != null) && (string.IsNullOrEmpty(_session.room_id));
                }
                if (_session == null || _session.version != 1 || !ValidToken(_session.access_token)
                    || !ValidToken(_session.refresh_token) || !ulong.TryParse(_session.account_id, out ulong id) || id == 0
                    || _session.expires_in < 1 || _session.expires_in > 3600
                    || !DateTimeOffset.TryParse(_session.session_expires_at, out _sessionExpires)
                    || _sessionExpires <= DateTimeOffset.UtcNow)
                {
                    throw new IOException();
                }

                Uri origin = ValidateOrigin(_session.api_origin, Debug.isDebugBuild);
                _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = !origin.IsLoopback })
                    { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(10) };
                _accessExpires = DateTimeOffset.UtcNow.AddSeconds(_session.expires_in);
                await VerifyAsync();
                if (!string.IsNullOrEmpty(_session.room_id))
                {
                    if (!Guid.TryParse(_session.room_id, out _) || !System.Net.IPAddress.TryParse(_session.address, out _)
                        || _session.port < 1024 || _session.port > 65535)
                    {
                        throw new IOException();
                    }

                    RoomId = _session.room_id;
                }
                IsFrontendMode = RoomId == null;
                State = SessionState.Ready;
                Debug.Log("[LauncherGameSession] 계정 세션 확인: accountId=" + AccountId);
                if (IsFrontendMode)
                {
                    // Boot의 영속 세션 오브젝트와 풀을 준비한 뒤 오프라인 방 화면으로 간다.
                    await PresentationPoolPrewarmer.WaitForBootAsync(_lifetime.Token);
                    EnterLobbyScene();
                }
                else
                {
                    // 기존 런처가 방을 지정한 실행도 그대로 유지한다.
                    await StartConnectionAsync(RoomId, _session.address, _session.port,
                        _session.is_host, origin, _connectionRevision, _lifetime.Token);
                }
                while (!_lifetime.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), _lifetime.Token);
                    if ((RoomId != null) && (!NetworkClient.isConnected)
                        && (!_connectionPending) && (!_returningToLobby))
                    {
                        if (IsFrontendMode)
                        {
                            ReturnToLobby("게임 연결이 끊어졌습니다. 방을 다시 선택해 주세요.");
                        }
                        else
                        {
                            throw new IOException();
                        }
                    }

                    if (_sessionExpires <= DateTimeOffset.UtcNow)
                    {
                        throw new IOException();
                    }

                    await _requests.WaitAsync(_lifetime.Token);
                    try
                    {
                        if (_accessExpires <= DateTimeOffset.UtcNow.AddSeconds(40))
                        {
                            await RefreshAsync();
                        }

                        await VerifyAsync();
                    }
                    finally { _requests.Release(); }
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception)
            {
                // Neither exception text nor HTTP bodies are safe to log here
                State = SessionState.Failed;
                AccountId = null;
                Nickname = null;
                _session = null;
                if (IsFrontendMode)
                {
                    Debug.LogError("[LauncherGameSession] 계정 세션이 만료되거나 확인되지 않았습니다. 런처에서 다시 로그인해 주세요.");
                    ReturnToLobby("계정 세션이 종료되었습니다. 런처에서 다시 로그인해 주세요.");
                }
                else
                {
                    Debug.LogError("[LauncherGameSession] 계정 세션을 확인할 수 없어 게임을 종료합니다. 런처에서 다시 로그인해 주세요.");
                    if (NetworkClient.active)
                    {
                        NetworkClient.Disconnect();
                    }
                    Application.Quit(12);
                }
            }
        }

        private async Task VerifyAsync()
        {
            Profile profile = JsonUtility.FromJson<Profile>(await RequestAsync(HttpMethod.Get, "v1/me", null));
            if (profile == null || profile.account_id != _session.account_id || profile.status != "active"
                || string.IsNullOrWhiteSpace(profile.nickname))
            {
                throw new IOException();
            }

            AccountId = profile.account_id;
            Nickname = profile.nickname;
        }

        private static LauncherGameSession ReadyFrontend()
        {
            if ((_instance == null) || (!IsFrontendMode) || (State != SessionState.Ready) || (_instance._lifetime == null))
            {
                throw new AccountRoomException("session_unavailable", "게임 계정 세션을 사용할 수 없습니다. 런처에서 다시 로그인해 주세요.");
            }
            return _instance;
        }

        public static async Task<AccountRoomList> ListRoomsAsync(CancellationToken ct = default)
        {
            LauncherGameSession self = ReadyFrontend();
            string json = await self.AuthorizedRequestAsync(HttpMethod.Get, "v1/rooms", null, ct);
            AccountRoomList list = ParseRoomJson<AccountRoomList>(json);
            if ((list.rooms == null) || (list.rooms.Length > MAX_LISTED_ROOMS) || (string.IsNullOrWhiteSpace(list.build_id)))
            {
                throw new AccountRoomException("invalid_response", "방 목록을 확인할 수 없습니다. 다시 시도해 주세요.");
            }
            if (list.build_id != BuildIdentity.ReleaseId)
            {
                throw BuildMismatch();
            }
            return list;
        }

        public static async Task<AccountRoom> CreateRoomAsync(string name, CancellationToken ct = default)
        {
            LauncherGameSession self = ReadyFrontend();
            if (self._pendingCloseRoomId != null)
            {
                await RetryCloseRoomAsync(ct);
            }
            if ((string.IsNullOrWhiteSpace(name)) || (name.Trim().Length < MIN_ROOM_NAME_LENGTH)
                || (name.Trim().Length > MAX_ROOM_NAME_LENGTH))
            {
                throw new AccountRoomException("invalid_name",
                    $"방 이름을 {MIN_ROOM_NAME_LENGTH}~{MAX_ROOM_NAME_LENGTH}자로 입력해 주세요.");
            }
            var input = new CreateRoomInput { name = name.Trim(), build_id = BuildIdentity.ReleaseId };
            string json;
            try
            {
                json = await self.AuthorizedRequestAsync(HttpMethod.Post, "v1/rooms",
                    JsonConvert.SerializeObject(input), ct);
            }
            catch (AccountRoomException exception) when (exception.Code == "room_conflict")
            {
                // 생성 충돌에는 빌드 불일치와 방 수용량 초과가 모두 포함된다.
                // 서버의 목록 빌드를 확인해 업데이트가 필요한 경우 정확한 안내를 전달한다.
                await ListRoomsAsync(ct);
                throw;
            }
            AccountRoom room = ParseRoomJson<AccountRoom>(json);
            ValidateRoomIdentity(room);
            return room;
        }

        public static async Task<AccountRoom> LobbyAsync(string roomId, string action,
            int? preference = null, bool enabled = false, CancellationToken ct = default)
        {
            LauncherGameSession self = ReadyFrontend();
            if ((!Guid.TryParse(roomId, out Guid id)) || (!ValidLobbyAction(action))
                || ((preference.HasValue) && ((preference.Value < 0) || (preference.Value > MAX_WEAPON_PREFERENCE))))
            {
                throw new AccountRoomException("invalid_request", "방 요청을 확인할 수 없습니다.");
            }
            string body = JsonConvert.SerializeObject(new LobbyInput
            {
                action = action,
                preference = preference,
                enabled = enabled,
            });
            string json = await self.AuthorizedRequestAsync(HttpMethod.Post,
                "v1/rooms/" + id.ToString() + "/lobby", body, ct);
            AccountRoom room = ParseRoomJson<AccountRoom>(json);
            ValidateRoomIdentity(room);
            return room;
        }

        private static bool ValidLobbyAction(string action)
        {
            return (action == "join") || (action == "leave") || (action == "pulse")
                || (action == "preference") || (action == "ready") || (action == "bots") || (action == "start");
        }

        private static T ParseRoomJson<T>(string json) where T : class
        {
            try
            {
                T value = JsonConvert.DeserializeObject<T>(json,
                    new JsonSerializerSettings { MaxDepth = ROOM_JSON_MAX_DEPTH });
                if (value != null)
                {
                    return value;
                }
            }
            catch (JsonException)
            {
            }
            throw new AccountRoomException("invalid_response", "방 정보를 확인할 수 없습니다. 다시 시도해 주세요.");
        }

        private static void ValidateRoomIdentity(AccountRoom room)
        {
            if ((room == null) || (!Guid.TryParse(room.room_id, out _)) || (string.IsNullOrWhiteSpace(room.name))
                || (room.members == null) || (room.members.Length > Constants.Network.MAX_PLAYERS)
                || (room.max_players != Constants.Network.MAX_PLAYERS))
            {
                throw new AccountRoomException("invalid_response", "방 정보를 확인할 수 없습니다. 다시 시도해 주세요.");
            }
            if (room.build_id != BuildIdentity.ReleaseId)
            {
                throw BuildMismatch();
            }
        }

        private static AccountRoomException BuildMismatch()
        {
            return new AccountRoomException("build_mismatch",
                "설치된 게임과 서버 빌드가 다릅니다. 런처에서 업데이트해 주세요.");
        }

        public static async Task ConnectRoomAsync(AccountRoom room, CancellationToken ct = default)
        {
            LauncherGameSession self = ReadyFrontend();
            ValidateRoomIdentity(room);
            bool owner = room.owner_id == AccountId;
            bool listenOwner = (room.hosting == "listen") && (owner) && (room.state == "hosting");
            bool joiner = (room.state == "ready") || (room.state == "running");
            if ((!listenOwner) && (!joiner))
            {
                throw new AccountRoomException("room_not_ready", "방이 아직 출발 준비 중입니다.");
            }
            if (!Array.Exists(room.members, member => (member != null) && (member.account_id == AccountId)))
            {
                throw new AccountRoomException("not_member", "이 방에 참가한 계정을 확인할 수 없습니다.");
            }
            if ((!System.Net.IPAddress.TryParse(room.address, out _)) || (room.port < MIN_ROOM_PORT) || (room.port > MAX_ROOM_PORT))
            {
                throw new AccountRoomException("invalid_response", "방 접속 정보를 확인할 수 없습니다.");
            }
            if ((self._connectionPending) || (RoomId != null) || (NetworkClient.active) || (NetworkServer.active))
            {
                throw new AccountRoomException("connection_pending", "이미 게임 연결을 진행 중입니다.");
            }

            self._connectionPending = true;
            self._connectedAsListenHost = listenOwner;
            int revision = ++self._connectionRevision;
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(self._lifetime.Token, ct))
            {
                try
                {
                    // 연결할 방은 GPU Boot 완료 뒤에만 묶는다. 티켓 발급은 이 값으로 방을 검증한다.
                    await PresentationPoolPrewarmer.WaitForBootAsync(linked.Token);
                    self.EnsureCurrentConnection(revision, linked.Token);
                    RoomId = room.room_id;
                    await self.StartConnectionAsync(room.room_id, room.address, room.port,
                        listenOwner, self._http.BaseAddress, revision, linked.Token);
                    DateTime deadline = DateTime.UtcNow.AddSeconds(CONNECT_TIMEOUT_SECONDS);
                    while (!NetworkClient.isConnected)
                    {
                        self.EnsureCurrentConnection(revision, linked.Token);
                        if (DateTime.UtcNow >= deadline)
                        {
                            throw new AccountRoomException("connection_timeout", "게임 서버 연결 시간이 초과되었습니다.");
                        }
                        await Task.Delay(CONNECT_POLL_MILLISECONDS, linked.Token);
                    }
                }
                catch
                {
                    if (revision == self._connectionRevision)
                    {
                        ReturnToLobby("게임 연결에 실패했습니다. 방 상태를 확인하고 다시 시도해 주세요.");
                    }
                    throw;
                }
                finally
                {
                    if (revision == self._connectionRevision)
                    {
                        self._connectionPending = false;
                    }
                }
            }
        }

        private void EnsureCurrentConnection(int revision, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((this == null) || (_lifetime.IsCancellationRequested) || (revision != _connectionRevision))
            {
                throw new OperationCanceledException();
            }
        }

        private async Task StartConnectionAsync(string room, string address, int port,
            bool isHost, Uri origin, int revision, CancellationToken cancellationToken)
        {
            // 릴레이 자격 발급과 호스트 권한 요청 전에 GPU 준비를 끝낸다.
            await PresentationPoolPrewarmer.WaitForBootAsync(cancellationToken);
            EnsureCurrentConnection(revision, cancellationToken);
            NetworkManager manager = NetworkManager.singleton;
            kcp2k.KcpTransport transport = manager == null ? null : manager.GetComponent<kcp2k.KcpTransport>();
            if ((transport == null) || (manager.isNetworkActive))
            {
                throw new IOException();
            }

            manager.transport = transport;
            transport.enabled = true;
            transport.Port = (ushort)port;
            Transport.active = transport;
            manager.networkAddress = address;
            if (isHost)
            {
                HostReply host = JsonUtility.FromJson<HostReply>(await AuthorizedRequestAsync(
                    HttpMethod.Post, "v1/rooms/" + room + "/host", "{}", cancellationToken));
                EnsureCurrentConnection(revision, cancellationToken);
                if ((host == null) || (host.port != port) || (!ValidToken(host.server_key)))
                {
                    throw new IOException();
                }
                AccountGameServer.StartListen(room, host.server_key, host.port, origin, host.relay);
            }
            else
            {
                AccountRelayCredentials relay = JsonUtility.FromJson<AccountRelayCredentials>(
                    await AuthorizedRequestAsync(HttpMethod.Post, "v1/rooms/" + room + "/relay",
                        "{}", cancellationToken));
                EnsureCurrentConnection(revision, cancellationToken);
                if ((relay != null) && (relay.enabled))
                {
                    AccountRelayTransport.Create(manager, relay);
                }
                manager.StartClient();
            }
        }

        private async Task<string> AuthorizedRequestAsync(HttpMethod method, string path, string json,
            CancellationToken cancellationToken)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken))
            {
                await _requests.WaitAsync(linked.Token);
                try
                {
                    if (_accessExpires <= DateTimeOffset.UtcNow.AddSeconds(ACCESS_REFRESH_MARGIN_SECONDS))
                    {
                        await RefreshAsync();
                    }
                    return await RequestAsync(method, path, json, linked.Token);
                }
                finally
                {
                    _requests.Release();
                }
            }
        }

        /// <summary>계정 첫 화면은 Title이다. Boot 준비 뒤와 한 판을 마친 뒤 모두 여기로 온다.</summary>
        private static void EnterLobbyScene()
        {
            if (SceneManager.GetActiveScene().name != Constants.Scene.TITLE)
            {
                SceneManager.LoadScene(Constants.Scene.TITLE);
            }
        }

        /// <summary>
        /// 오프라인 계정 화면 사이(Title, Lobby)만 오간다. 연결 중이거나 세션이 준비되지 않았으면 거절한다.
        /// </summary>
        public static bool OpenFrontendScene(string sceneName)
        {
            if ((_instance == null) || (!IsFrontendMode) || (State != SessionState.Ready) ||
                (_instance._connectionPending) || (_instance._returningToLobby) ||
                (NetworkClient.active) || (NetworkServer.active) ||
                ((sceneName != Constants.Scene.TITLE) && (sceneName != Constants.Scene.LOBBY)))
            {
                return false;
            }
            if (SceneManager.GetActiveScene().name != sceneName)
            {
                SceneManager.LoadScene(sceneName);
            }
            return true;
        }

        private void OnClientDisconnected()
        {
            if ((IsFrontendMode) && (!_returningToLobby) && (RoomId != null))
            {
                ReturnToLobby("게임 연결이 끊어졌습니다. 방을 다시 선택해 주세요.");
            }
        }

        /// <summary>인게임 계정 경로의 Mirror 연결만 정리하고 게임 세션은 유지한다.</summary>
        public static void ReturnToLobby(string message = null)
        {
            LauncherGameSession self = _instance;
            if ((self == null) || (!IsFrontendMode) || (self._returningToLobby))
            {
                return;
            }
            self._returningToLobby = true;
            ++self._connectionRevision;
            self._connectionPending = false;
            if ((self._connectedAsListenHost) && (RoomId != null))
            {
                self._pendingCloseRoomId = RoomId;
            }
            self.ReturnToLobbyAfterCloseAsync(message);
        }

        /// <summary>닫기 요청이 불확실했던 호스트 방을 같은 계정 세션으로 다시 닫는다.</summary>
        public static async Task RetryCloseRoomAsync(CancellationToken ct = default)
        {
            LauncherGameSession self = ReadyFrontend();
            string room = self._pendingCloseRoomId;
            if (room == null)
            {
                return;
            }
            await self.AuthorizedRequestAsync(HttpMethod.Delete, "v1/rooms/" + room, null, ct);
            if (self._pendingCloseRoomId == room)
            {
                self._pendingCloseRoomId = null;
            }
        }

        private async void ReturnToLobbyAfterCloseAsync(string message)
        {
            if ((_pendingCloseRoomId != null) && (State == SessionState.Ready))
            {
                bool closed = false;
                for (int attempt = 0; (attempt < CLOSE_ROOM_ATTEMPTS) && (!closed); ++attempt)
                {
                    try
                    {
                        await RetryCloseRoomAsync(_lifetime.Token);
                        closed = true;
                    }
                    catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception)
                    {
                        if (State == SessionState.Failed)
                        {
                            message = "계정 세션이 종료되었습니다. 런처에서 다시 로그인해 주세요.";
                            break;
                        }
                        if (attempt < CLOSE_ROOM_ATTEMPTS - 1)
                        {
                            try
                            {
                                await Task.Delay(CLOSE_RETRY_BASE_MILLISECONDS * (attempt + 1), _lifetime.Token);
                            }
                            catch (OperationCanceledException)
                            {
                                break;
                            }
                        }
                    }
                }
                if ((!closed) && (State == SessionState.Ready))
                {
                    message = "방 종료를 확인하지 못했습니다. 새 방을 만들기 전에 다시 시도해 주세요.";
                }
            }
            RoomId = null;
            _connectedAsListenHost = false;
            try
            {
                AccountGameServer.StopListenForLobby();
                NetworkManager manager = NetworkManager.singleton;
                if (manager != null)
                {
                    if ((NetworkServer.active) && (NetworkClient.active))
                    {
                        manager.StopHost();
                    }
                    else if (NetworkServer.active)
                    {
                        manager.StopServer();
                    }
                    else if (NetworkClient.active)
                    {
                        manager.StopClient();
                    }

                    AccountRelayTransport relay = manager.GetComponentInChildren<AccountRelayTransport>(true);
                    if (relay != null)
                    {
                        relay.enabled = false;
                        Destroy(relay.gameObject);
                    }
                    kcp2k.KcpTransport kcp = manager.GetComponent<kcp2k.KcpTransport>();
                    if (kcp != null)
                    {
                        kcp.enabled = true;
                        manager.transport = kcp;
                        Transport.active = kcp;
                    }
                }
            }
            catch (Exception)
            {
                Debug.LogWarning("[LauncherGameSession] 연결 정리 중 오류가 발생했습니다.");
            }
            if (this != null)
            {
                StartCoroutine(CoEnterLobbyAfterDisconnect(message));
            }
        }

        private System.Collections.IEnumerator CoEnterLobbyAfterDisconnect(string message)
        {
            float deadline = Time.realtimeSinceStartup + DISCONNECT_WAIT_SECONDS;
            while (((NetworkClient.active) || (NetworkServer.active)) && (Time.realtimeSinceStartup < deadline))
            {
                yield return null;
            }
            if (this == null)
            {
                yield break;
            }
            if ((NetworkClient.active) || (NetworkServer.active))
            {
                Debug.LogError("[LauncherGameSession] 이전 네트워크 연결이 종료되지 않아 방 재접속을 막습니다.");
                message = "이전 게임 연결을 정리하지 못했습니다. 게임을 다시 시작해 주세요.";
            }

            // Mirror는 씬 NetworkIdentity를 종료할 때 오브젝트 자체를 비활성화한다.
            // SessionManagers는 DDOL이므로 다시 켜야 다음 방의 ServerBootGate와 입력이 살아난다.
            SessionPersistence[] persistences = Resources.FindObjectsOfTypeAll<SessionPersistence>();
            for (int i = 0; i < persistences.Length; ++i)
            {
                SessionPersistence persistence = persistences[i];
                if ((persistence != null) && (persistence.gameObject.scene.name == "DontDestroyOnLoad")
                    && (persistence.GetComponent<RunSceneDirector>() != null) && (!persistence.gameObject.activeSelf))
                {
                    persistence.gameObject.SetActive(true);
                }
            }
            EnterLobbyScene();
            _returningToLobby = false;
            ReturnedToLobby?.Invoke(message);
        }

        public static async Task<string> JoinTicketAsync(string room, string challenge)
        {
            if (_instance == null || State != SessionState.Ready || RoomId == null || room != RoomId
                || challenge == null || challenge.Length != 64)
            {
                throw new IOException();
            }

            foreach (char c in challenge)
            {
                if (!((c >= 'a' && c <= 'f') || (c >= '0' && c <= '9')))
                {
                    throw new IOException();
                }
            }

            var self = _instance;
            await self._requests.WaitAsync(self._lifetime.Token);
            try
            {
                if (self._accessExpires <= DateTimeOffset.UtcNow.AddSeconds(40))
                {
                    await self.RefreshAsync();
                }

                string body = JsonUtility.ToJson(new TicketInput { challenge = challenge, build_id = BuildIdentity.ReleaseId });
                TicketReply result = JsonUtility.FromJson<TicketReply>(await self.RequestAsync(HttpMethod.Post, "v1/rooms/" + room + "/tickets", body));
                if (result == null || !ValidToken(result.ticket))
                {
                    throw new IOException();
                }

                return result.ticket;
            }
            finally { self._requests.Release(); }
        }

        private async Task RefreshAsync()
        {
            string refresh = _session.refresh_token;
            _session.refresh_token = null; // never retry a one-use token after an ambiguous result
            string json = JsonUtility.ToJson(new Refresh { refresh_token = refresh });
            Tokens tokens = JsonUtility.FromJson<Tokens>(await RequestAsync(HttpMethod.Post, "v1/auth/refresh", json));
            if (tokens == null || tokens.session_kind != "game" || tokens.token_type != "Bearer"
                || !ValidToken(tokens.access_token) || !ValidToken(tokens.refresh_token)
                || tokens.expires_in < 1 || tokens.expires_in > 3600 || tokens.account == null
                || tokens.account.account_id != AccountId || tokens.account.status != "active"
                || !DateTimeOffset.TryParse(tokens.session_expires_at, out DateTimeOffset expiry)
                || expiry > _sessionExpires || expiry <= DateTimeOffset.UtcNow)
            {
                throw new IOException();
            }

            _session.access_token = tokens.access_token;
            _session.refresh_token = tokens.refresh_token;
            _accessExpires = DateTimeOffset.UtcNow.AddSeconds(tokens.expires_in);
            Debug.Log("[LauncherGameSession] 게임 세션 갱신 확인");
        }

        private async Task<string> RequestAsync(HttpMethod method, string path, string json,
            CancellationToken cancellationToken = default)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken))
            using (var request = new HttpRequestMessage(method, path))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                if (json != null)
                {
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                if (path != "v1/auth/refresh")
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.access_token);
                }

                using (HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        if (IsFrontendMode)
                        {
                            if (response.StatusCode == HttpStatusCode.Unauthorized)
                            {
                                State = SessionState.Failed;
                                ReturnToLobby("계정 세션이 종료되었습니다. 런처에서 다시 로그인해 주세요.");
                                throw new AccountRoomException("session_expired",
                                    "계정 세션이 종료되었습니다. 런처에서 다시 로그인해 주세요.");
                            }
                            throw RoomRequestError(response.StatusCode);
                        }
                        throw new IOException();
                    }

                    using (Stream stream = await response.Content.ReadAsStreamAsync())
                    using (var buffer = new MemoryStream())
                    {
                        byte[] chunk = new byte[4096];
                        int read;
                        while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, timeout.Token)) > 0)
                        {
                            if (buffer.Length + read > MAX_RESPONSE_BYTES)
                            {
                                throw new IOException();
                            }

                            buffer.Write(chunk, 0, read);
                        }
                        return Encoding.UTF8.GetString(buffer.ToArray());
                    }
                }
            }
        }

        private static AccountRoomException RoomRequestError(HttpStatusCode status)
        {
            switch (status)
            {
                case HttpStatusCode.Forbidden:
                    return new AccountRoomException("forbidden", "이 계정으로 방 요청을 처리할 수 없습니다.");
                case HttpStatusCode.NotFound:
                    return new AccountRoomException("not_found", "방을 찾을 수 없습니다. 목록을 새로고침해 주세요.");
                case HttpStatusCode.Conflict:
                    return new AccountRoomException("room_conflict", "방 상태가 바뀌었습니다. 목록을 새로고침해 주세요.");
                case HttpStatusCode.UnprocessableEntity:
                    return new AccountRoomException("invalid_request", "방 이름이나 요청 값을 확인해 주세요.");
                case HttpStatusCode.TooManyRequests:
                    return new AccountRoomException("rate_limited", "요청이 많습니다. 잠시 후 다시 시도해 주세요.");
                default:
                    return new AccountRoomException("server_error", "계정 서버에서 방 요청을 처리하지 못했습니다.");
            }
        }

        public static Uri ValidateOrigin(string value, bool local)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) || uri.UserInfo.Length != 0
                || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/"
                || (uri.Scheme != "https" && !(local && uri.Scheme == "http"
                    && (uri.Host == "127.0.0.1" || uri.Host == "localhost" || uri.Host == "[::1]"))))
            {
                throw new IOException("Invalid account origin");
            }

            return uri;
        }

        public static bool ValidToken(string value)
        {
            if (value == null || value.Length < 40 || value.Length > 128)
            {
                return false;
            }

            foreach (char c in value)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-'))
                {
                    return false;
                }
            }

            return true;
        }

        private static string Value(string[] args, string key)
        {
            int index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private void OnDestroy()
        {
            GeumeumNetworkManager.ClientDisconnected -= OnClientDisconnected;
            _lifetime?.Cancel();
            _http?.Dispose();
            _session = null;
            State = SessionState.Failed;
            RoomId = null;
            _instance = null;
            AccountId = null;
            Nickname = null;
        }
    }
}
