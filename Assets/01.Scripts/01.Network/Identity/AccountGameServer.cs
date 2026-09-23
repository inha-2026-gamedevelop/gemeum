using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Geumeum.Common;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Geumeum.Network
{
    public struct AccountChallengeMessage : NetworkMessage { public string Challenge; public string RoomId; }
    public struct AccountProofMessage : NetworkMessage { public string Ticket; public string Build; }

    /// <summary> 로컬 전용 서버. 접속 신원은 서버 키로 교환한 API 응답에서만 얻는다 </summary>
    public sealed class AccountGameServer : MonoBehaviour
    {
        private static bool _listenHost;
        public static bool Enabled => _listenHost || Array.IndexOf(Environment.GetCommandLineArgs(), "-account-server") >= 0;
        public static bool IsAccountProcess => Enabled || Array.IndexOf(Environment.GetCommandLineArgs(), "-account-pipe") >= 0;
        public static AccountGameServer Instance { get; private set; }
        private readonly Dictionary<int, string> _pending = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _names = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _sessions = new Dictionary<int, string>();
        private readonly HashSet<int> _owners = new HashSet<int>();
        private CancellationTokenSource _lifetime;
        private HttpClient _http;
        private string _room;
        private GeumeumNetworkManager _manager;
        private AccountRelayCredentials _relay;

        [Serializable] private sealed class Proof { public string ticket; public string challenge; public string build_id; }
        [Serializable] private sealed class Identity { public string account_id; public string nickname; public string session_id; public bool is_owner; }
        [Serializable] private sealed class SessionClaim { public string account_id; public string session_id; }
        [Serializable] private sealed class Beat { public string state; public string[] accounts; public SessionClaim[] sessions; }
        [Serializable] public sealed class LaunchMember { public string account_id; public int preference; }
        [Serializable] private sealed class BeatReply { public bool closed; public string[] revoked; public LaunchMember[] launch_members; public bool fill_with_bots; }
        private bool _partyApplied;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { Instance = null; _listenHost = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (!Enabled)
            {
                return;
            }

            var owner = new GameObject("AccountGameServer");
            DontDestroyOnLoad(owner);
            Instance = owner.AddComponent<AccountGameServer>();
        }

        public static void StartListen(string room, string key, int port, Uri origin, AccountRelayCredentials relay = null)
        {
            if (Instance != null || port < 1024 || port > 65535)
            {
                throw new InvalidOperationException();
            }

            _listenHost = true;
            var owner = new GameObject("AccountGameServer");
            DontDestroyOnLoad(owner);
            Instance = owner.AddComponent<AccountGameServer>();
            Instance._relay = relay;
            Instance.Run(room, key, (ushort)port, origin);
        }

        /// <summary>인게임 방 찾기로 돌아갈 때 listen host만 정리한다. 전용 서버에는 적용하지 않는다.</summary>
        public static void StopListenForLobby()
        {
            AccountGameServer server = Instance;
            if ((!_listenHost) || (server == null))
            {
                return;
            }
            server._lifetime?.Cancel();
            if ((server._manager != null) && (NetworkServer.active))
            {
                if (NetworkClient.active)
                {
                    server._manager.StopHost();
                }
                else
                {
                    server._manager.StopServer();
                }
            }
            Instance = null;
            _listenHost = false;
            Destroy(server.gameObject);
        }

        private void Start()
        {
            if (_listenHost)
            {
                return;
            }

            string room = Environment.GetEnvironmentVariable("GEUMEUM_ROOM_ID");
            string key = Environment.GetEnvironmentVariable("GEUMEUM_SERVER_KEY");
            ushort.TryParse(Environment.GetEnvironmentVariable("GEUMEUM_GAME_PORT"), out ushort port);
            Uri origin;
            try { origin = LauncherGameSession.ValidateOrigin(Environment.GetEnvironmentVariable("GEUMEUM_ACCOUNT_ORIGIN"), Debug.isDebugBuild); }
            catch { Application.Quit(13); return; }
            Environment.SetEnvironmentVariable("GEUMEUM_SERVER_KEY", null);
            Run(room, key, port, origin);
        }

        private async void Run(string room, string key, ushort port, Uri origin)
        {
            _lifetime = new CancellationTokenSource();
            try
            {
                _room = room;
                if (!Guid.TryParse(_room, out _) || !LauncherGameSession.ValidToken(key) || port < 1024)
                {
                    throw new InvalidOperationException();
                }

                _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = !origin.IsLoopback, UseCookies = false })
                    { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(10) };
                _http.DefaultRequestHeaders.Add("X-Server-Key", key);
                await Task.Yield();
                _manager = NetworkManager.singleton as GeumeumNetworkManager;
                var transport = _manager == null ? null : _manager.GetComponent<kcp2k.KcpTransport>();
                if (transport == null)
                {
                    throw new InvalidOperationException();
                }

                if (_relay != null && _relay.enabled)
                {
                    transport = AccountRelayTransport.Create(_manager, _relay);
                }

                _manager.transport = transport;
                transport.enabled = true;
                transport.Port = port;
                Transport.active = transport;
                if (_listenHost)
                {
                    _manager.StartHost();
                }
                else
                {
                    _manager.StartServer();
                }

                if (transport is AccountRelayTransport relayTransport)
                {
                    DateTime relayDeadline = DateTime.UtcNow.AddSeconds(15);
                    while (!relayTransport.RelayReady)
                    {
                        if (DateTime.UtcNow > relayDeadline)
                        {
                            throw new TimeoutException();
                        }

                        await Task.Delay(50, _lifetime.Token);
                    }
                    Debug.Log("[AccountGameServer] AWS 릴레이 연결 확인 (포트포워딩 불필요)");
                }
                await Task.Delay(100, _lifetime.Token);
                if (!NetworkServer.active || RunSceneDirector.ServerInstance == null)
                {
                    throw new InvalidOperationException();
                }

                if (SceneManager.GetActiveScene().name != "Lobby")
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(40);
                    while (SceneManager.GetActiveScene().name != "Lobby" || RunSceneDirector.ServerInstance.State != RunTransitionState.Idle)
                    {
                        if (DateTime.UtcNow > deadline)
                        {
                            throw new TimeoutException();
                        }

                        if (RunSceneDirector.ServerInstance.State == RunTransitionState.Idle
                            && SceneManager.GetActiveScene().name != "Lobby"
                            && !RunSceneDirector.ServerInstance.ServerBeginTransition(RunSceneId.Lobby))
                        {
                            throw new InvalidOperationException();
                        }

                        await Task.Delay(100, _lifetime.Token);
                    }
                }
                Debug.Log("[AccountGameServer] 계정 게임 서버 준비 완료");
                while (!_lifetime.IsCancellationRequested)
                {
                    var auth = GeumeumAuthenticator.ServerInstance;
                    string[] accounts = auth == null ? Array.Empty<string>() : auth.AccountConnections().Values.Select(v => v.ToString()).ToArray();
                    var claims = auth == null ? Array.Empty<SessionClaim>() : auth.AccountConnections()
                        .Where(p => _sessions.ContainsKey(p.Key))
                        .Select(p => new SessionClaim { account_id = p.Value.ToString(), session_id = _sessions[p.Key] }).ToArray();
                    var beat = new Beat { state = _manager.IsAdmissionLocked ? "running" : "ready", accounts = accounts, sessions = claims };
                    BeatReply reply = JsonUtility.FromJson<BeatReply>(await PostAsync("heartbeat", JsonUtility.ToJson(beat)));
                    if (reply == null || reply.revoked == null || reply.closed)
                    {
                        throw new InvalidOperationException();
                    }

                    if (auth != null)
                    {
                        foreach (var pair in auth.AccountConnections())
                        {
                            if (reply.revoked.Contains(pair.Value.ToString()) && NetworkServer.connections.TryGetValue(pair.Key, out var connection))
                            {
                                connection.Disconnect();
                            }
                        }
                    }

                    if (!_partyApplied && reply.launch_members != null && LobbyController.ServerInstance != null)
                    {
                        _partyApplied = LobbyController.ServerInstance.ServerApplyLauncherParty(reply.launch_members, reply.fill_with_bots);
                    }

                    if (_partyApplied && RunManager.ServerInstance != null && RunManager.ServerInstance.Phase == RunPhase.Lobby)
                    {
                        if ((_listenHost) && (LauncherGameSession.IsFrontendMode))
                        {
                            LauncherGameSession.ReturnToLobby("원정이 끝났습니다. 새 방을 선택해 주세요.");
                            return;
                        }
                        throw new InvalidOperationException();
                    }

                    await Task.Delay(_partyApplied ? 10000 : 1000, _lifetime.Token);
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception)
            {
                if ((_listenHost) && (LauncherGameSession.IsFrontendMode))
                {
                    Debug.LogWarning("[AccountGameServer] 방 호스트 임대가 종료되어 방 찾기로 돌아갑니다.");
                    LauncherGameSession.ReturnToLobby("방 호스트 연결이 종료되었습니다. 새 방을 선택해 주세요.");
                    return;
                }
                Debug.LogWarning("[AccountGameServer] 서버 인증/임대가 종료되어 프로세스를 닫습니다.");
                if (_manager != null && NetworkServer.active)
                { if (_listenHost)
                    {
                        _manager.StopHost();
                    }
                    else
                    {
                        _manager.StopServer();
                    }
                }
                Application.Quit(13);
            }
        }

        public async void Begin(NetworkConnectionToClient connection)
        {
            if (!_listenHost && (!IPAddress.TryParse(connection.address, out IPAddress address)
                || !IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address)))
            { connection.Disconnect(); return; }
            byte[] bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            string challenge = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            _pending[connection.connectionId] = challenge;
            connection.Send(new AccountChallengeMessage { Challenge = challenge, RoomId = _room });
            try { await Task.Delay(25000, _lifetime.Token); }
            catch (OperationCanceledException) { return; }
            if (_pending.TryGetValue(connection.connectionId, out string remaining) && remaining == challenge)
            { _pending.Remove(connection.connectionId); connection.Disconnect(); }
        }

        public async void Redeem(NetworkConnectionToClient connection, AccountProofMessage message)
        {
            if (!_pending.TryGetValue(connection.connectionId, out string challenge)) { connection.Disconnect(); return; }
            _pending.Remove(connection.connectionId);
            if (!LauncherGameSession.ValidToken(message.Ticket) || message.Build != BuildIdentity.ReleaseId)
            { connection.Disconnect(); return; }
            try
            {
                var proof = new Proof { ticket = message.Ticket, challenge = challenge, build_id = message.Build };
                Identity identity = JsonUtility.FromJson<Identity>(await PostAsync("redeem", JsonUtility.ToJson(proof)));
                if (!NetworkServer.connections.TryGetValue(connection.connectionId, out var current) || !ReferenceEquals(current, connection))
                {
                    return;
                }

                if (identity == null || !ulong.TryParse(identity.account_id, out ulong id) || id == 0)
                {
                    throw new InvalidOperationException();
                }

                if (string.IsNullOrWhiteSpace(identity.nickname) || identity.nickname.Length > 64)
                {
                    throw new InvalidOperationException();
                }

                if (!Guid.TryParse(identity.session_id, out _))
                {
                    throw new InvalidOperationException();
                }

                _names[connection.connectionId] = identity.nickname;
                _sessions[connection.connectionId] = identity.session_id;
                if (identity.is_owner)
                {
                    _owners.Add(connection.connectionId);
                }
                else
                {
                    _owners.Remove(connection.connectionId);
                }

                GeumeumAuthenticator.ServerInstance.AcceptAccount(connection, id);
            }
            catch (Exception) { connection.Disconnect(); }
        }

        private async Task<string> PostAsync(string path, string json)
        {
            using (var body = new StringContent(json, Encoding.UTF8, "application/json"))
            using (var response = await _http.PostAsync("internal/rooms/" + _room + "/" + path, body, _lifetime.Token))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException();
                }

                string value = await response.Content.ReadAsStringAsync();
                if (value.Length > 16384)
                {
                    throw new InvalidOperationException();
                }

                return value;
            }
        }

        public bool TryGetNickname(int connectionId, out string nickname) => _names.TryGetValue(connectionId, out nickname);
        public bool IsRoomOwner(int connectionId) => _owners.Contains(connectionId);
        public void Forget(int connectionId) { _pending.Remove(connectionId); _names.Remove(connectionId); _sessions.Remove(connectionId); _owners.Remove(connectionId); }
        private void OnDestroy()
        {
            _lifetime?.Cancel();
            _http?.Dispose();
            if (Instance == this)
            {
                Instance = null;
                _listenHost = false;
            }
        }
    }
}
