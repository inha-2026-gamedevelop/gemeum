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
    /// <summary> 호스트 생성, 접속, 해제를 관리하는 Mirror NetworkManager. 최대 3인, 플레이어 프리팹 등록과 연결 수명만 담당 </summary>
    public class GeumeumNetworkManager : NetworkManager
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly List<string> _bootFailures = new List<string>();

        /// <summary> 로컬 클라이언트가 서버에 붙었다 </summary>
        public static event System.Action ClientConnected;

        /// <summary> 로컬 클라이언트의 연결이 끊겼다 </summary>
        public static event System.Action ClientDisconnected;

        [Tooltip("이 키로 호스트를 시작한다. NetworkManagerHUD 없이 붙기 위한 Dev 씬 전용이고, Main.unity에서는 None으로 둔다")]
        [SerializeField] private KeyCode _devStartHostKey = KeyCode.None;

        [Tooltip("일시정지된 런을 포기한다. UI가 붙기 전까지 호스트가 대기를 끊는 유일한 수단이다")]
        [SerializeField] private KeyCode _devAbandonRunKey = KeyCode.None;

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

        public override void Update()
        {
            base.Update();

            TryAbandonPausedRun();

            if ((_devStartHostKey == KeyCode.None) || isNetworkActive)
            {
                return;
            }

            if (Input.GetKeyDown(_devStartHostKey))
            {
                Debug.Log($"[GeumeumNetworkManager] Dev 단축키({_devStartHostKey})로 호스트를 시작한다");
                StartHost();
            }
        }

        // UI가 붙기 전까지 호스트가 대기를 끊는 유일한 수단이다
        private void TryAbandonPausedRun()
        {
            if ((_devAbandonRunKey == KeyCode.None) || (!NetworkServer.active))
            {
                return;
            }

            if (!Input.GetKeyDown(_devAbandonRunKey))
            {
                return;
            }

            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (!run.IsPaused))
            {
                return;
            }

            Debug.Log($"[GeumeumNetworkManager] Dev 단축키({_devAbandonRunKey})로 대기 중인 런을 포기한다");
            run.AbortRun("호스트가 대기를 끊었다");
        }

        /****************************************
        *              Server Lifecycle
        ****************************************/

        // Mirror의 StartServer, StartHost는 virtual이 아니라 가로챌 수 없다.
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

        /****************************************
        *              Client Side
        ****************************************/

        // Mirror의 NetworkClient.OnConnectedEvent는 NetworkManager가 클라이언트를 시작할 때
        // += 가 아니라 = 로 덮어쓴다. 밖에서 구독하면 조용히 지워지므로 여기서 다시 알린다
        public override void OnClientConnect()
        {
            base.OnClientConnect();
            ClientConnected?.Invoke();
        }

        public override void OnClientDisconnect()
        {
            base.OnClientDisconnect();
            ClientDisconnected?.Invoke();
        }

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            // 입장 잠금과 신원 검증은 GeumeumAuthenticator가 한다.
            // Mirror 96은 인증에 성공한 연결만 여기로 보내므로 여기서 다시 막지 않는다
            base.OnServerConnect(conn);

            // TODO(민성): 0단계 스파이크 관측용이다. Steam 경로에서 address가 SteamID64로 오는지 확인한 뒤 정리한다.
            // 이 값이 기대와 다르면 플랫폼 ID 설계 전체를 다시 잡아야 한다
            Debug.Log($"[GeumeumNetworkManager] 접속: connectionId={conn.connectionId}, address={conn.address}, 현재 인원={numPlayers}");
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

            GeumeumAuthenticator.ServerInstance?.ServerForget(conn.connectionId);

            PauseRunOnLeave(conn);

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

        /// <summary> 런이 끝나 로비로 돌아갈 때 입장을 다시 연다. RunManager만 호출한다 </summary>
        public void UnlockAdmission()
        {
            IsAdmissionLocked = false;
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

        // 런 도중 누가 빠지면 끝내지 않고 얼린다. 보스전 30분을 진행하다 한 명이 튕겼다고
        // 런을 버리면 복구가 아니라 손실이다. 돌아올 수 있도록 입장도 다시 연다
        private void PauseRunOnLeave(NetworkConnectionToClient conn)
        {
            if (RunManager.ServerInstance == null)
            {
                return;
            }

            // 잠금은 그대로 둔다. 풀면 일시정지 중에 로스터에 없는 사람도 들어와 자리를 가져간다.
            // 돌아올 사람은 인증기가 두고 간 자리로 알아본다
            RunManager.ServerInstance.PauseRun($"connectionId={conn.connectionId} 이탈");
        }
    }
}
