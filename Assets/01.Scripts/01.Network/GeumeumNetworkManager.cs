// System
using System.Collections;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary> 호스트 생성 · 접속 · 해제를 관리하는 Mirror NetworkManager. 최대 3인, 플레이어 프리팹 등록과 연결 수명만 담당 </summary>
    public class GeumeumNetworkManager : NetworkManager
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly List<string> _bootFailures = new List<string>();

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 런이 시작되어 신규 접속과 재접속을 받지 않는 상태 </summary>
        public bool IsAdmissionLocked { get; private set; } = false;

        /****************************************
        *              Unity Event
        ****************************************/

        public override void Awake()
        {
            maxConnections = Constants.Network.MAX_PLAYERS; // NetworkServer.Listen이 이 값으로 접속 정원을 강제한다
            base.Awake();
        }

        /****************************************
        *              Server Lifecycle
        ****************************************/

        // Mirror의 StartServer · StartHost는 virtual이 아니라 가로챌 수 없다.
        // 대신 서버가 열린 직후 여기서 검사하고, 실패하면 로비를 열지 않고 곧바로 닫는다
        public override void OnStartServer()
        {
            base.OnStartServer();

            if (ServerBootGate.TryValidate(this, _bootFailures))
            {
                return;
            }

            // TODO(민성): UI 파트가 붙으면 호스트 화면에도 원인을 띄운다
            Debug.LogError($"[GeumeumNetworkManager] 부트 검증에 실패해 서버를 닫는다: {string.Join(" / ", _bootFailures)}", this);
            StartCoroutine(CoStopAfterFailedBoot());
        }

        public override void OnStopServer()
        {
            IsAdmissionLocked = false;
            base.OnStopServer();
        }

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            // 런이 시작된 뒤의 신규 접속과 재접속은 받지 않는다
            if (IsAdmissionLocked)
            {
                Debug.Log($"[GeumeumNetworkManager] 런 진행 중이라 접속을 거부한다: connectionId={conn.connectionId}");
                conn.Disconnect();
                return;
            }

            base.OnServerConnect(conn);
            Debug.Log($"[GeumeumNetworkManager] 접속: connectionId={conn.connectionId}, 현재 인원={numPlayers}");
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            base.OnServerAddPlayer(conn);

            if (LobbyController.ServerInstance == null)
            {
                Debug.LogError("[GeumeumNetworkManager] LobbyController가 없어 로비 슬롯을 만들지 못했다");
                return;
            }

            LobbyController.ServerInstance.ServerAddSlot(conn);
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            Debug.Log($"[GeumeumNetworkManager] 해제: connectionId={conn.connectionId}");

            if (LobbyController.ServerInstance != null)
            {
                LobbyController.ServerInstance.ServerRemoveSlot(conn);
            }

            AbortRunOnLeave(conn);

            base.OnServerDisconnect(conn);
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 런 시작 순간 입장을 잠근다. LobbyController만 호출한다 </summary>
        public void LockAdmission()
        {
            IsAdmissionLocked = true;
        }

        /****************************************
        *                Methods
        ****************************************/

        // OnStartServer 안에서 바로 닫으면 Mirror의 호스트 시작 절차 중간에 끼어든다, 한 프레임 뒤에 닫는다
        private IEnumerator CoStopAfterFailedBoot()
        {
            yield return null;

            if (NetworkClient.active)
            {
                StopHost();
                yield break;
            }

            StopServer();
        }

        // 런 도중 누가 빠지면 남은 런을 중단하고 리더보드에 기록하지 않는다
        private void AbortRunOnLeave(NetworkConnectionToClient conn)
        {
            if (RunManager.ServerInstance == null)
            {
                return;
            }

            RunManager.ServerInstance.AbortRun($"connectionId={conn.connectionId} 이탈");
        }
    }
}
