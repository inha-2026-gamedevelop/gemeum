// System
using System;
using System.Collections;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.Serialization;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary> 접속 승인 게이트. 빌드, 신원, 입장 잠금, 중복 신원을 여기서 한 번에 본다 </summary>
    public class GeumeumAuthenticator : NetworkAuthenticator
    {
        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("KCP 경로에서 쓸 신원 소스. DevIdentitySource를 꽂는다")]
        [FormerlySerializedAs("_identitySourceBehaviour")]
        [SerializeField] private MonoBehaviour _devIdentitySourceBehaviour;

        [Tooltip("Steam 경로에서 쓸 신원 소스. SteamIdentitySource를 꽂는다, KCP 전용 씬은 비워 둔다")]
        [SerializeField] private MonoBehaviour _steamIdentitySourceBehaviour;

        // 승인된 연결의 신원
        private readonly Dictionary<int, PlayerPlatformId> _acceptedIds = new Dictionary<int, PlayerPlatformId>();

        private IPlayerIdentitySource _devIdentitySource;
        private IPlayerIdentitySource _steamIdentitySource;
        private Coroutine _accountWait;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버 프로세스의 인스턴스. 로비가 연결의 신원을 물을 때 쓴다 </summary>
        public static GeumeumAuthenticator ServerInstance { get; private set; }

        /// <summary> 짝 검사를 위한 신원 소스 이름. 없으면 빈 문자열 </summary>
        public string IdentityLabel => AccountGameServer.Enabled ? "Account" : (ActiveSource != null) ? ActiveSource.Label : string.Empty;

        /// <summary>
        /// 인증 직후 SceneMessage를 기다려야 하는지 로컬 NetworkManager가 읽는다
        /// AuthResponse가 ClientAccept보다 먼저 이 값을 확정한다
        /// </summary>
        public static bool ClientSceneSyncExpected { get; private set; }

        private IPlayerIdentitySource ActiveSource
        {
            get
            {
                return SteamTransportSelector.IsSteamTransport(Transport.active)
                    ? _steamIdentitySource
                    : _devIdentitySource;
            }
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _devIdentitySource   = Resolve(_devIdentitySourceBehaviour);
            _steamIdentitySource = Resolve(_steamIdentitySourceBehaviour);
        }

        private IPlayerIdentitySource Resolve(MonoBehaviour behaviour)
        {
            if (behaviour == null)
            {
                return null;
            }

            if (!(behaviour is IPlayerIdentitySource source))
            {
                Debug.LogError($"[GeumeumAuthenticator] {behaviour.GetType().Name}은 IPlayerIdentitySource가 아니다", this);
                return null;
            }

            return source;
        }

        /****************************************
        *              Server Side
        ****************************************/

        public override void OnStartServer()
        {
            ServerInstance = this;
            _acceptedIds.Clear();

            NetworkServer.RegisterHandler<AuthRequestMessage>(OnAuthRequest, false);
            NetworkServer.RegisterHandler<AccountProofMessage>((conn, message) =>
            {
                if (AccountGameServer.Enabled && AccountGameServer.Instance != null)
                {
                    AccountGameServer.Instance.Redeem(conn, message);
                }
                else
                {
                    conn.Disconnect();
                }
            }, false);
        }

        public override void OnStopServer()
        {
            if (ServerInstance == this)
            {
                ServerInstance = null;
            }

            _acceptedIds.Clear();
            NetworkServer.UnregisterHandler<AuthRequestMessage>();
            NetworkServer.UnregisterHandler<AccountProofMessage>();
        }

        public override void OnServerAuthenticate(NetworkConnectionToClient conn)
        {
            if (AccountGameServer.Enabled)
            {
                AccountGameServer.Instance.Begin(conn);
            }
        }

        /// <summary> 승인된 연결의 신원을 돌려준다. 승인 전이거나 끊긴 연결이면 false </summary>
        public bool TryGetPlatformId(int connectionId, out PlayerPlatformId id)
        {
            return _acceptedIds.TryGetValue(connectionId, out id);
        }

        /// <summary> 연결이 끊길 때 신원 기록을 지운다. GeumeumNetworkManager가 호출한다 </summary>
        public void ServerForget(int connectionId)
        {
            _acceptedIds.Remove(connectionId);
            AccountGameServer.Instance?.Forget(connectionId);
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnAuthRequest(NetworkConnectionToClient conn, AuthRequestMessage message)
        {
            if (AccountGameServer.Enabled) { conn.Disconnect(); return; }
            if (!BuildIdentity.IsCompatible(message.Build, "클라", out string buildReason))
            {
                Reject(conn, AuthResultCode.BuildMismatch, buildReason);
                return;
            }

            IPlayerIdentitySource source = ActiveSource;
            if (source == null)
            {
                Reject(conn, AuthResultCode.InvalidIdentity, "서버에 신원 소스가 없다");
                return;
            }

            if (!source.TryResolve(conn, message.PlatformId, out PlayerPlatformId id, out string reason))
            {
                Reject(conn, AuthResultCode.InvalidIdentity, reason ?? "신원을 정할 수 없다");
                return;
            }

            if (!TryValidateSteamSession(id, message, out AuthResultCode sessionCode, out string sessionReason))
            {
                Reject(conn, sessionCode, sessionReason);
                return;
            }

            AcceptIdentity(conn, id, source.Label);
        }

        public Dictionary<int, ulong> AccountConnections()
        {
            var result = new Dictionary<int, ulong>();
            foreach (var pair in _acceptedIds)
            {
                result[pair.Key] = pair.Value.Value;
            }

            return result;
        }

        public void AcceptAccount(NetworkConnectionToClient conn, ulong accountId)
        {
            if (!AccountGameServer.Enabled) { conn.Disconnect(); return; }
            AcceptIdentity(conn, new PlayerPlatformId(accountId), "Account");
        }

        private void AcceptIdentity(NetworkConnectionToClient conn, PlayerPlatformId id, string label)
        {
            var manager = NetworkManager.singleton as GeumeumNetworkManager;
            if ((manager != null) && (manager.IsAdmissionLocked))
            {
                LobbyRoster roster = LobbyRoster.ServerInstance;
                if ((roster == null) || (!roster.HasVacatedSeat(id.Value)))
                {
                    Reject(conn, AuthResultCode.AdmissionLocked, "런이 진행 중이다");
                    return;
                }

                Debug.Log($"[GeumeumAuthenticator] 입장 잠금 중이지만 두고 간 자리가 있어 받는다: platformId={id}");
            }

            foreach (KeyValuePair<int, PlayerPlatformId> pair in _acceptedIds)
            {
                if ((pair.Value == id) && (pair.Key != conn.connectionId))
                {
                    Reject(conn, AuthResultCode.DuplicateId, $"이미 접속한 신원이다: {id}");
                    return;
                }
            }

            _acceptedIds[conn.connectionId] = id;

            conn.Send(new AuthResponseMessage
            {
                Code = AuthResultCode.Accepted,
                Reason = string.Empty,
                SceneSyncRequired = RequiresInitialSceneSync(
                    NetworkManager.networkSceneName,
                    (manager == null) ? string.Empty : manager.offlineScene),
            });
            ServerAccept(conn);

            Debug.Log($"[GeumeumAuthenticator] 승인: {NetworkPeerLabel.Describe(conn)}, platformId={id}, source={label}");
        }

        /// <summary> Steam 경로에서만 도는 세션 검증 </summary>
        private static bool TryValidateSteamSession(PlayerPlatformId id, AuthRequestMessage message,
                                                    out AuthResultCode code, out string reason)
        {
            code   = AuthResultCode.Accepted;
            reason = null;

            if (!SteamTransportSelector.IsSteamTransport(Transport.active))
            {
                return true;
            }

            // 개발 빌드에서도 우회하지 않는다. 로비를 건너뛰고 호스트 SteamID로 바로 붙는 경로가
            // 열려 있으면 검증한 흐름과 실제로 쓰는 흐름이 갈라져, 로비 쪽 결함이 테스트에서 안 드러난다
            SteamLobbyService lobby = SteamLobbyService.Instance;
            if ((lobby == null) || (!lobby.IsInLobby))
            {
                code   = AuthResultCode.NotLobbyMember;
                reason = "서버가 Steam 로비에 들어가 있지 않다";
                return false;
            }

            if (!lobby.IsMember(id.Value))
            {
                code   = AuthResultCode.NotLobbyMember;
                reason = $"Steam 로비 멤버가 아니다: {id}";
                return false;
            }

            string expected = lobby.GetData(SteamLobbyKeys.NONCE);
            if (string.IsNullOrEmpty(expected))
            {
                code   = AuthResultCode.StaleSession;
                reason = "세션 nonce가 아직 게시되지 않았다";
                return false;
            }

            if (!string.Equals(message.Nonce, expected, StringComparison.Ordinal))
            {
                code   = AuthResultCode.StaleSession;
                reason = "세션 nonce가 다르다, 이전 호스트의 접속 정보로 보인다";
                return false;
            }

            return true;
        }

        private void Reject(NetworkConnectionToClient conn, AuthResultCode code, string reason)
        {
            Debug.LogWarning($"[GeumeumAuthenticator] 거부({code}): {NetworkPeerLabel.Describe(conn)}, {reason}", this);

            conn.Send(new AuthResponseMessage { Code = code, Reason = reason });
            ServerReject(conn);
        }

        /****************************************
        *              Client Side
        ****************************************/

        public override void OnStartClient()
        {
            ClientSceneSyncExpected = false;
            NetworkClient.RegisterHandler<AuthResponseMessage>(OnAuthResponse, false);
            NetworkClient.RegisterHandler<AccountChallengeMessage>(OnAccountChallenge, false);
        }

        public override void OnStopClient()
        {
            if (_accountWait != null) { StopCoroutine(_accountWait); _accountWait = null; }
            NetworkClient.UnregisterHandler<AuthResponseMessage>();
            NetworkClient.UnregisterHandler<AccountChallengeMessage>();
            ClientSceneSyncExpected = false;
        }

        public override void OnClientAuthenticate()
        {
            if (!string.IsNullOrEmpty(LauncherGameSession.RoomId))
            {
                return;
            }

            if (LauncherGameSession.State != LauncherGameSession.SessionState.Disabled)
            {
                if (_accountWait != null)
                {
                    StopCoroutine(_accountWait);
                }

                _accountWait = StartCoroutine(WaitForAccount());
                return;
            }
            SendClientAuthentication();
        }

        private IEnumerator WaitForAccount()
        {
            while (LauncherGameSession.State == LauncherGameSession.SessionState.Loading)
            {
                yield return null;
            }

            _accountWait = null;
            if (!NetworkClient.isConnected)
            {
                yield break;
            }

            if (LauncherGameSession.State != LauncherGameSession.SessionState.Ready)
            {
                NetworkClient.Disconnect();
                yield break;
            }
            SendClientAuthentication();
        }

        private void SendClientAuthentication()
        {
            NetworkClient.Send(new AuthRequestMessage
            {
                PlatformId = ResolveClaimedId(),
                Build      = BuildIdentity.ReleaseId,
                Nonce      = ResolveNonce(),
                Epoch      = 0U,
            });
        }

        private async void OnAccountChallenge(AccountChallengeMessage message)
        {
            var connection = NetworkClient.connection;
            try
            {
                string ticket = await LauncherGameSession.JoinTicketAsync(message.RoomId, message.Challenge);
                if (NetworkClient.isConnected && ReferenceEquals(connection, NetworkClient.connection))
                {
                    NetworkClient.Send(new AccountProofMessage { Ticket = ticket, Build = BuildIdentity.ReleaseId });
                }
            }
            catch (Exception)
            {
                Debug.LogWarning("[GeumeumAuthenticator] 계정 접속 티켓 검증에 실패했습니다.");
                if (ReferenceEquals(connection, NetworkClient.connection))
                {
                    NetworkClient.Disconnect();
                }
            }
        }

        private static string ResolveNonce()
        {
            if (!SteamTransportSelector.IsSteamTransport(Transport.active))
            {
                return string.Empty;
            }

            SteamLobbyService lobby = SteamLobbyService.Instance;
            return (lobby != null) ? lobby.GetData(SteamLobbyKeys.NONCE) : string.Empty;
        }

        private static ulong ResolveClaimedId()
        {
            if (!SteamTransportSelector.IsSteamTransport(Transport.active))
            {
                return NetworkLaunchArgs.RequestedPlatformId;
            }

            if (SteamLocalUser.TryGetSteamId(out ulong steamId))
            {
                return steamId;
            }

            Debug.LogError("[GeumeumAuthenticator] Steam 경로인데 자기 SteamID를 읽지 못했다, 서버가 거부한다");
            return 0UL;
        }

        private void OnAuthResponse(AuthResponseMessage message)
        {
            if (message.Code != AuthResultCode.Accepted)
            {
                ClientSceneSyncExpected = false;
                Debug.LogError($"[GeumeumAuthenticator] 접속 거부됨({message.Code}): {message.Reason}");
                NetworkClient.Disconnect();
                return;
            }

            ClientSceneSyncExpected = message.SceneSyncRequired;
            ClientAccept();
        }

        private static bool RequiresInitialSceneSync(string currentNetworkScene,
            string configuredOfflineScene)
        {
            return !string.IsNullOrEmpty(currentNetworkScene) &&
                !string.Equals(currentNetworkScene, configuredOfflineScene,
                    StringComparison.Ordinal);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetClientSceneSyncExpectation()
        {
            ClientSceneSyncExpected = false;
        }
    }
}
