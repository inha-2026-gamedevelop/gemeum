// System
using System;
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
    /// <summary>
    /// 접속 승인 게이트. 빌드, 신원, 입장 잠금, 중복 신원을 여기서 한 번에 본다.
    /// Mirror 96은 인증에 성공한 뒤에야 OnServerConnect를 부르므로, 입장 검증은 이쪽이 제자리다
    /// </summary>
    public class GeumeumAuthenticator : NetworkAuthenticator
    {
        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("붙는 쪽과 받는 쪽이 같아야 하는 값. 프로토콜이 바뀔 때만 올린다")]
        [SerializeField] private string _buildId = "dev";

        [Tooltip("KCP 경로에서 쓸 신원 소스. DevIdentitySource를 꽂는다")]
        [FormerlySerializedAs("_identitySourceBehaviour")]
        [SerializeField] private MonoBehaviour _devIdentitySourceBehaviour;

        [Tooltip("Steam 경로에서 쓸 신원 소스. SteamIdentitySource를 꽂는다, KCP 전용 씬은 비워 둔다")]
        [SerializeField] private MonoBehaviour _steamIdentitySourceBehaviour;

        // 승인된 연결의 신원. OnServerConnect 이후 로비가 읽는다
        private readonly Dictionary<int, PlayerPlatformId> _acceptedIds = new Dictionary<int, PlayerPlatformId>();

        private IPlayerIdentitySource _devIdentitySource;
        private IPlayerIdentitySource _steamIdentitySource;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버 프로세스의 인스턴스. 로비가 연결의 신원을 물을 때 쓴다 </summary>
        public static GeumeumAuthenticator ServerInstance { get; private set; }

        /// <summary> 짝 검사를 위한 신원 소스 이름. 없으면 빈 문자열 </summary>
        public string IdentityLabel => (ActiveSource != null) ? ActiveSource.Label : string.Empty;

        // 트랜스포트는 -steam 인자로 실행할 때마다 갈리는데 신원 소스는 씬에 직렬화되어 있다.
        // 고정 참조 하나만 두면 한 씬이 두 경로를 받지 못하므로 쓰는 시점에 트랜스포트를 보고 고른다
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
        }

        public override void OnStopServer()
        {
            if (ServerInstance == this)
            {
                ServerInstance = null;
            }

            _acceptedIds.Clear();
            NetworkServer.UnregisterHandler<AuthRequestMessage>();
        }

        public override void OnServerAuthenticate(NetworkConnectionToClient conn)
        {
            // 클라이언트가 AuthRequestMessage를 보낼 때까지 기다린다. Mirror가 시간 초과를 관리한다
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
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnAuthRequest(NetworkConnectionToClient conn, AuthRequestMessage message)
        {
            if (!string.Equals(message.Build, _buildId, StringComparison.Ordinal))
            {
                Reject(conn, AuthResultCode.BuildMismatch, $"빌드가 다르다: 서버={_buildId}, 클라={message.Build}");
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

            // 런이 진행 중이면 새 사람을 받지 않는다.
            // 다만 두고 간 자리가 있는 사람은 돌아올 수 있어야 한다 - 이게 일시정지 재접속 경로다
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

            // 같은 신원이 두 연결로 들어오면 슬롯 주인이 누구인지 알 수 없게 된다
            foreach (KeyValuePair<int, PlayerPlatformId> pair in _acceptedIds)
            {
                if ((pair.Value == id) && (pair.Key != conn.connectionId))
                {
                    Reject(conn, AuthResultCode.DuplicateId, $"이미 접속한 신원이다: {id}");
                    return;
                }
            }

            _acceptedIds[conn.connectionId] = id;

            conn.Send(new AuthResponseMessage { Code = AuthResultCode.Accepted, Reason = string.Empty });
            ServerAccept(conn);

            Debug.Log($"[GeumeumAuthenticator] 승인: connectionId={conn.connectionId}, platformId={id}, source={source.Label}");
        }

        /// <summary>
        /// Steam 경로에서만 도는 세션 검증. 로비 멤버가 아닌 사람과 이전 세션의 접속 정보를 거른다.
        /// KCP 개발 경로에는 로비가 없어 그대로 통과시킨다
        /// </summary>
        private static bool TryValidateSteamSession(PlayerPlatformId id, AuthRequestMessage message,
                                                    out AuthResultCode code, out string reason)
        {
            code   = AuthResultCode.Accepted;
            reason = null;

            if (!SteamTransportSelector.IsSteamTransport(Transport.active))
            {
                return true;
            }

            // 로비 없이 연 호스트는 -steam -host 직접 접속이다. 대조할 로비가 없으므로 검사를 건너뛰는데,
            // 출시 빌드에서 이 경로가 열리면 검증이 통째로 빠지므로 거기서는 막는다
            SteamLobbyService lobby = SteamLobbyService.Instance;
            if ((lobby == null) || (!lobby.IsInLobby))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning("[GeumeumAuthenticator] 서버가 Steam 로비에 없다, 로비 검증을 건너뛴다 (개발 직접 접속)");
                return true;
#else
                code   = AuthResultCode.NotLobbyMember;
                reason = "서버가 Steam 로비에 들어가 있지 않다";
                return false;
#endif
            }

            if (!lobby.IsMember(id.Value))
            {
                code   = AuthResultCode.NotLobbyMember;
                reason = $"Steam 로비 멤버가 아니다: {id}";
                return false;
            }

            // 호스트가 StartHost 직후 게시한다. 비어 있으면 게시 전에 접속이 들어온 것이다
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
            Debug.LogWarning($"[GeumeumAuthenticator] 거부({code}): connectionId={conn.connectionId}, {reason}", this);

            conn.Send(new AuthResponseMessage { Code = code, Reason = reason });
            ServerReject(conn);
        }

        /****************************************
        *              Client Side
        ****************************************/

        public override void OnStartClient()
        {
            NetworkClient.RegisterHandler<AuthResponseMessage>(OnAuthResponse, false);
        }

        public override void OnStopClient()
        {
            NetworkClient.UnregisterHandler<AuthResponseMessage>();
        }

        public override void OnClientAuthenticate()
        {
            NetworkClient.Send(new AuthRequestMessage
            {
                PlatformId = ResolveClaimedId(),
                Build      = _buildId,
                Nonce      = ResolveNonce(),
                Epoch      = 0U,           // 4단계에서 선출 회차로 채운다
            });
        }

        // 로비 공용 데이터의 값을 그대로 실어 보낸다. 서버가 같은 값을 읽어 대조하므로
        // 이전 세션에서 살아남은 클라이언트는 여기서 걸린다
        private static string ResolveNonce()
        {
            if (!SteamTransportSelector.IsSteamTransport(Transport.active))
            {
                return string.Empty;
            }

            SteamLobbyService lobby = SteamLobbyService.Instance;
            return (lobby != null) ? lobby.GetData(SteamLobbyKeys.NONCE) : string.Empty;
        }

        // Steam 경로에서 이 값은 신원이 아니라 대조값이다. 서버는 conn.address를 신원으로 쓰고
        // 이 값이 다르면 거부하므로, 실행 인자가 아니라 자기 SteamID를 보내야 한다
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
                Debug.LogError($"[GeumeumAuthenticator] 접속 거부됨({message.Code}): {message.Reason}");
                NetworkClient.Disconnect();
                return;
            }

            ClientAccept();
        }
    }
}
