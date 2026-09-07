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
    /// <summary> 접속 승인 게이트. 빌드, 신원, 입장 잠금, 중복 신원을 여기서 한 번에 본다 </summary>
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

        // 승인된 연결의 신원
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

            conn.Send(new AuthResponseMessage { Code = AuthResultCode.Accepted, Reason = string.Empty });
            ServerAccept(conn);

            Debug.Log($"[GeumeumAuthenticator] 승인: connectionId={conn.connectionId}, platformId={id}, source={source.Label}");
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
                Epoch      = 0U,
            });
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
                Debug.LogError($"[GeumeumAuthenticator] 접속 거부됨({message.Code}): {message.Reason}");
                NetworkClient.Disconnect();
                return;
            }

            ClientAccept();
        }
    }
}
