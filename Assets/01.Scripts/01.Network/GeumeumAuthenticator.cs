// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

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

        [Tooltip("신원을 어디서 얻을지. KCP 경로는 Dev, Steam 경로는 Steam 소스를 꽂는다")]
        [SerializeField] private MonoBehaviour _identitySourceBehaviour;

        // 승인된 연결의 신원. OnServerConnect 이후 로비가 읽는다
        private readonly Dictionary<int, PlayerPlatformId> _acceptedIds = new Dictionary<int, PlayerPlatformId>();

        private IPlayerIdentitySource _identitySource;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버 프로세스의 인스턴스. 로비가 연결의 신원을 물을 때 쓴다 </summary>
        public static GeumeumAuthenticator ServerInstance { get; private set; }

        /// <summary> 짝 검사를 위한 신원 소스 이름. 없으면 빈 문자열 </summary>
        public string IdentityLabel => (_identitySource != null) ? _identitySource.Label : string.Empty;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _identitySource = _identitySourceBehaviour as IPlayerIdentitySource;
            if ((_identitySourceBehaviour != null) && (_identitySource == null))
            {
                Debug.LogError($"[GeumeumAuthenticator] {_identitySourceBehaviour.GetType().Name}은 IPlayerIdentitySource가 아니다", this);
            }
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

            if (_identitySource == null)
            {
                Reject(conn, AuthResultCode.InvalidIdentity, "서버에 신원 소스가 없다");
                return;
            }

            if (!_identitySource.TryResolve(conn, message.PlatformId, out PlayerPlatformId id, out string reason))
            {
                Reject(conn, AuthResultCode.InvalidIdentity, reason ?? "신원을 정할 수 없다");
                return;
            }

            // 런이 진행 중이면 새 사람을 받지 않는다. 일시정지 중에는 잠금이 풀려 있어 돌아올 수 있다
            var manager = NetworkManager.singleton as GeumeumNetworkManager;
            if ((manager != null) && (manager.IsAdmissionLocked))
            {
                Reject(conn, AuthResultCode.AdmissionLocked, "런이 진행 중이다");
                return;
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

            Debug.Log($"[GeumeumAuthenticator] 승인: connectionId={conn.connectionId}, platformId={id}, source={_identitySource.Label}");
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
                PlatformId = NetworkLaunchArgs.RequestedPlatformId,
                Build      = _buildId,
                Nonce      = string.Empty, // 3단계에서 Steam 로비 데이터로 채운다
                Epoch      = 0U,           // 4단계에서 선출 회차로 채운다
            });
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
