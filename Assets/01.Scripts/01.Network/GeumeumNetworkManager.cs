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
            maxConnections = Constants.Network.MAX_PLAYERS;
            base.Awake();
        }

        public override void Update()
        {
            base.Update();

            TryAbandonPausedRun();

            if ((_devStartHostKey == KeyCode.None) || (isNetworkActive))
            {
                return;
            }

            if (Input.GetKeyDown(_devStartHostKey))
            {
                Debug.Log($"[GeumeumNetworkManager] Dev 단축키({_devStartHostKey})로 호스트를 시작한다");
                StartHost();
            }
        }

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

        public override void OnStartServer()
        {
            base.OnStartServer();

            if (ServerBootGate.TryValidate(this, _bootFailures))
            {
                return;
            }

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

        public override void OnClientConnect()
        {
            // onlineScene을 비워 두고 ServerChangeScene을 쓰는 프로젝트라 Mirror의
            // clientLoadedScene만으로는 초기 SceneMessage를 예측할 수 없다. 원격은
            // 인증 응답에 기록된 scene sync를 기다려 OnClientSceneChanged에서 한 번만
            // Ready/AddPlayer를 보낸다. Host는 scene load callback이 생략될 수 있어 즉시 처리한다.
            if (NetworkServer.active || !GeumeumAuthenticator.ClientSceneSyncExpected)
            {
                base.OnClientConnect();
            }

            ClientConnected?.Invoke();
        }

        /// <summary>
        /// Mirror가 씬 전환을 끝내고 NetworkClient.Ready()까지 부른 뒤에 불린다.
        /// 도착 보고는 반드시 여기서 한다 - SceneManager.sceneLoaded 시점은 아직 ready가 아니라
        /// Command가 버려지고 서버가 이 클라를 계속 기다린다
        /// </summary>
        public override void OnClientSceneChanged()
        {
            base.OnClientSceneChanged();

            RunSceneDirector director = RunSceneDirector.LocalInstance;
            if (director == null)
            {
                Debug.LogWarning("[GeumeumNetworkManager] RunSceneDirector가 없어 씬 도착을 보고하지 못했다");
                return;
            }

            director.ClientReportArrived();
        }

        public override void OnClientDisconnect()
        {
            base.OnClientDisconnect();
            ClientDisconnected?.Invoke();
        }

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            base.OnServerConnect(conn);

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

            if (GeumeumAuthenticator.ServerInstance != null)
            {
                GeumeumAuthenticator.ServerInstance.ServerForget(conn.connectionId);
            }

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

        private void PauseRunOnLeave(NetworkConnectionToClient conn)
        {
            if (RunManager.ServerInstance == null)
            {
                return;
            }

            RunManager.ServerInstance.PauseRun($"connectionId={conn.connectionId} 이탈");
        }
    }
}
