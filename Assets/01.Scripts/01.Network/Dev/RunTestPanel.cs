// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Combat;
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary>
    /// 브라우저 개발 패널에서 테스트 조건을 넣고 런을 시작한다.
    ///
    /// 지금까지 1인 실행은 씬의 LobbyController 값(_devMinPlayers·_devAutoReady)을 고치거나
    /// -minplayers 인자로 다시 띄워야 했다. 씬은 머지가 안 되고 인자는 에디터에서 못 쓴다.
    /// 그래서 값을 런타임 오버라이드로 두고 별도 창에서 고른다
    /// </summary>
    public class RunTestPanel : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD

        /****************************************
        *                Preload
        ****************************************/

        /// <summary> 씬에 배치하지 않아도 실행 시작에 스스로 붙는다 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Preload()
        {
            DevToolHost.Attach<RunTestPanel>();
        }

        /****************************************
        *              Constants
        ****************************************/

        private const string PANEL_ID    = "runtest";
        private const string PANEL_TITLE = "테스트 실행";

        private const string SCENE_VALUE_OPTION = "씬 값";

        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("런이 시작되면 바로 넘어갈 씬")]
        [SerializeField] private RunSceneId _jumpScene = RunSceneId.Boss;

        private string _message = string.Empty;

        // 런이 시작되면 곧바로 보스 아레나로 넘긴다. 필드를 지나며 확인할 것이 없을 때 쓴다
        private bool _isJumpAfterStart = false;
        private bool _isJumpDone       = false;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            DevPanelHost.Register(PANEL_ID, PANEL_TITLE, Build, Handle);
        }

        private void Start()
        {
            // 게임 화면에는 조작을 남기지 않는다. 필요하면 패널에서 다시 켠다
            NetworkManagerHUD hud = ResolveMirrorHud();
            if (hud != null)
            {
                hud.enabled = false;
            }
        }

        private void OnDestroy()
        {
            DevPanelHost.Unregister(PANEL_ID);
        }

        private void Update()
        {
            TryJumpAfterStart();
        }

        /// <summary>
        /// 런이 시작된 뒤 한 번만 씬을 넘긴다.
        /// 시작과 같은 프레임에 넘기면 RunSceneDirector가 아직 Idle이 아니어서 조용히 무시된다
        /// </summary>
        private void TryJumpAfterStart()
        {
            if ((!_isJumpAfterStart) || (!NetworkServer.active))
            {
                return;
            }

            RunManager       run      = RunManager.ServerInstance;
            RunSceneDirector director = RunSceneDirector.ServerInstance;
            if ((run == null) || (director == null))
            {
                return;
            }

            // 로비로 돌아오면 다음 런에서 한 번 더 넘길 수 있게 되돌린다
            if (run.Phase == RunPhase.Lobby)
            {
                _isJumpDone = false;
                return;
            }

            if ((_isJumpDone) || (director.State != RunTransitionState.Idle) ||
                (director.CurrentScene == _jumpScene))
            {
                return;
            }

            _isJumpDone = true;

            if (director.ServerBeginTransition(_jumpScene))
            {
                _message = $"런 시작 뒤 {_jumpScene}로 넘겼다";
                return;
            }

            _message = $"{_jumpScene} 전환을 시작하지 못했다";
        }

        /****************************************
        *                Panel
        ****************************************/

        private void Build(DevPanelBuilder builder)
        {
            BuildStatus(builder);

            builder.Header("시작 인원");
            builder.Choice("players", "런을 시작할 인원", DescribeRequiredPlayers(),
                SCENE_VALUE_OPTION, "1인", "2인", "3인");

            builder.Toggle("autoready", "자동 준비 (접속하면 바로 준비 상태)",
                LobbyController.DevForceAutoReady);

            builder.Header("런 모드");
            builder.Choice("mode", "등록 대상 여부가 달라진다", DescribeRunMode(),
                "정식", "보스 트라이", "개발");

            builder.Header("내 캐릭터 희망");
            builder.Choice("character", "확정은 서버가 한다", string.Empty, "차사", "신궁", "무녀");

            builder.Header("실행");
            builder.Toggle("jump", $"런이 시작되면 바로 {_jumpScene}로", _isJumpAfterStart);
            builder.Toggle("mirrorhud", "게임 화면에 Mirror 기본 HUD 표시", IsMirrorHudVisible());

            builder.Button("solo",      "1인 실행");
            builder.Button("starthost", "호스트 시작");
            builder.Button("stophost",  "호스트 중지");
            builder.Button("startrun",  "런 시작 시도");
            builder.Button("ready",     "준비");
            builder.Button("unready",   "준비 해제");
            builder.Button("jumpnow",   $"{_jumpScene}로 전환");
            builder.Button("revive",    "전체 부활");
            builder.Button("abandon",   "런 포기");

            if (!string.IsNullOrEmpty(_message))
            {
                builder.Label(_message);
            }
        }

        private static void BuildStatus(DevPanelBuilder builder)
        {
            builder.Label($"연결: {DescribeNetwork()}   전송: {DescribeTransport()}");

            RunManager       run      = RunManager.ServerInstance;
            RunSceneDirector director = RunSceneDirector.ServerInstance;
            LobbyRoster      roster   = LobbyRoster.ServerInstance;

            if (run == null)
            {
                builder.Label("런 상태는 호스트에서만 보인다");
                return;
            }

            string scene = (director != null) ? $"{director.CurrentScene}/{director.State}" : "씬 감독 없음";
            string slots = (roster != null) ? roster.SlotCount.ToString() : "-";

            builder.Label($"단계: {run.Phase}   모드: {run.Mode}   씬: {scene}");
            builder.Label($"인원: {slots}명 접속 · {DescribeRequiredPlayers()} 필요");
        }

        private void Handle(DevCommand command)
        {
            switch (command.Control)
            {
                case "players":
                    SetRequiredPlayers(command);
                    return;

                case "autoready":
                    LobbyController.DevForceAutoReady = command.IsOn;
                    _message = command.IsOn ? "자동 준비를 켰다" : "자동 준비를 껐다. 준비는 손으로 누른다";
                    return;

                case "mode":
                    SetRunMode(command);
                    return;

                case "character":
                    SelectCharacter(command);
                    return;

                case "jump":
                    _isJumpAfterStart = command.IsOn;
                    _isJumpDone       = false;
                    return;

                case "solo":
                    StartSolo();
                    return;

                case "starthost":
                    StartHost();
                    return;

                case "stophost":
                    StopHost();
                    return;

                case "mirrorhud":
                    SetMirrorHudVisible(command.IsOn);
                    return;

                case "startrun":
                    TryStartRun();
                    return;

                case "ready":
                    SetReady(true);
                    return;

                case "unready":
                    SetReady(false);
                    return;

                case "jumpnow":
                    JumpNow();
                    return;

                case "revive":
                    ReviveAll();
                    return;

                case "abandon":
                    AbandonRun();
                    return;
            }
        }

        /****************************************
        *                Actions
        ****************************************/

        /// <summary> 선택 순번은 Build가 적은 항목 순서와 같다. 0번이 씬 값, 그 뒤가 1인·2인·3인이다 </summary>
        private void SetRequiredPlayers(in DevCommand command)
        {
            if (!command.TryGetInt(out int index))
            {
                return;
            }

            if (index <= 0)
            {
                LobbyController.DevRequiredPlayers = 0;
                _message = "인원을 씬 값과 실행 인자에 맡긴다";
                return;
            }

            int count = Mathf.Min(index, Constants.Network.MAX_PLAYERS);

            LobbyController.DevRequiredPlayers = count;

            if (count < Constants.Network.MAX_PLAYERS)
            {
                // 정원보다 적게 두면 남이 준비를 눌러 줄 수 없다. 자동 준비를 같이 켜서
                // 게임 쪽 [시작] 한 번으로 런까지 이어지게 한다
                LobbyController.DevForceAutoReady = true;
                _message = $"{count}인으로 런을 시작한다. 자동 준비도 켰다";
                return;
            }

            _message = $"{count}인으로 런을 시작한다";
        }

        private void SetRunMode(in DevCommand command)
        {
            if (!command.TryGetInt(out int index))
            {
                return;
            }

            switch (index)
            {
                case 0: LobbyController.DevRunMode = RunMode.Standard;  break;
                case 1: LobbyController.DevRunMode = RunMode.BossTrial; break;
                case 2: LobbyController.DevRunMode = RunMode.Dev;       break;
                default: return;
            }

            _message = $"런 모드를 {DescribeRunMode()}으로 둔다";
        }

        private void SelectCharacter(in DevCommand command)
        {
            if (!command.TryGetInt(out int index))
            {
                return;
            }

            if ((index < 0) || (index > (int) CharacterKind.Munyeo))
            {
                return;
            }

            var character = (CharacterKind) index;

            LobbyController lobby = FindClientLobbyController();
            if (lobby == null)
            {
                _message = "로비 창구가 없다. 먼저 호스트나 클라이언트로 붙는다";
                return;
            }

            // 희망만 보내고 확정은 서버가 한다. 남과 겹쳐도 거부되지 않는다
            lobby.CmdSelectCharacter(character);
            _message = $"{character}를 희망으로 보냈다";
        }

        /// <summary> 인원을 1로 낮추고 자동 준비를 켠 뒤, 아직 안 붙었으면 호스트까지 연다 </summary>
        private void StartSolo()
        {
            LobbyController.DevRequiredPlayers = 1;
            LobbyController.DevForceAutoReady  = true;

            if (!NetworkServer.active)
            {
                StartHost();
                return;
            }

            TryStartRun();
        }

        private void StartHost()
        {
            if (NetworkServer.active)
            {
                _message = "이미 호스트가 열려 있다";
                return;
            }

            if (SteamTransportSelector.IsSteamTransport(Transport.active))
            {
                StartSteamHost();
                return;
            }

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                _message = "NetworkManager가 없다";
                return;
            }

            manager.StartHost();
            _message = "호스트를 열었다";
        }

        private void StopHost()
        {
            if (!NetworkServer.active)
            {
                _message = "열려 있는 호스트가 없다";
                return;
            }

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                _message = "NetworkManager가 없다";
                return;
            }

            manager.StopHost();
            _message = "호스트를 닫았다";
        }

        /// <summary> Steam 경로는 방이 먼저 있어야 호스트가 열린다 </summary>
        private void StartSteamHost()
        {
            SteamSessionCoordinator session = SteamSessionCoordinator.Instance;
            if (session == null)
            {
                _message = "Steam 세션이 없다";
                return;
            }

            if (!session.CanStartHost)
            {
                session.CreateLobby();
                _message = $"Steam 방을 만든다. 단계가 바뀌면 다시 누른다 (지금 {session.State})";
                return;
            }

            session.StartHost();
            _message = "Steam 호스트를 열었다";
        }

        private void TryStartRun()
        {
            LobbyController lobby = LobbyController.ServerInstance;
            if (lobby == null)
            {
                _message = "런 시작은 호스트에서만 누른다";
                return;
            }

            RunManager run = RunManager.ServerInstance;
            if ((run != null) && (run.Phase != RunPhase.Lobby))
            {
                _message = $"이미 런이 돌고 있다: {run.Phase}";
                return;
            }

            lobby.DevTryStartRunNow();
            _message = "지금 설정으로 런 시작을 시도했다. 안 되면 위 인원 줄을 본다";
        }

        private void SetReady(bool isReady)
        {
            LobbyController lobby = FindClientLobbyController();
            if (lobby == null)
            {
                _message = "로비 창구가 없다. 먼저 호스트나 클라이언트로 붙는다";
                return;
            }

            lobby.CmdSetReady(isReady);
            _message = isReady ? "준비를 보냈다" : "준비 해제를 보냈다";
        }

        private void JumpNow()
        {
            RunSceneDirector director = RunSceneDirector.ServerInstance;
            if (director == null)
            {
                _message = "씬 전환은 호스트만 건다";
                return;
            }

            if (!director.ServerBeginTransition(_jumpScene))
            {
                _message = $"{_jumpScene} 전환을 시작하지 못했다: {director.CurrentScene}/{director.State}";
                return;
            }

            _message = $"{_jumpScene}로 전환한다";
        }

        private void ReviveAll()
        {
            if (!NetworkServer.active)
            {
                _message = "부활은 호스트만 건다";
                return;
            }

            ReviveController.ServerReviveAll();
            _message = "전체 부활을 걸었다";
        }

        private void AbandonRun()
        {
            RunManager run = RunManager.ServerInstance;
            if (run == null)
            {
                _message = "런 포기는 호스트만 누른다";
                return;
            }

            run.AbortRun("테스트 실행 패널에서 포기했다");
            _message = "런을 포기했다";
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// Mirror가 얹는 기본 HUD. 호스트 시작·중지를 이 패널이 대신하므로 기본으로 끈다.
        /// 씬 파일을 고치지 않고 런타임에만 끈다 - 씬은 머지가 안 되고, 끄고 싶을 사람만 다르다
        /// </summary>
        private static bool IsMirrorHudVisible()
        {
            NetworkManagerHUD hud = ResolveMirrorHud();
            return ((hud != null) && (hud.enabled));
        }

        private void SetMirrorHudVisible(bool isVisible)
        {
            NetworkManagerHUD hud = ResolveMirrorHud();
            if (hud == null)
            {
                _message = "Mirror 기본 HUD가 씬에 없다";
                return;
            }

            hud.enabled = isVisible;
            _message    = isVisible ? "Mirror 기본 HUD를 켰다" : "Mirror 기본 HUD를 껐다";
        }

        private static NetworkManagerHUD ResolveMirrorHud()
        {
            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                return null;
            }

            // NetworkManager와 같은 오브젝트에 붙는다. Find 계열을 매 프레임 돌리지 않기 위한 것이다
            return manager.GetComponent<NetworkManagerHUD>();
        }

        /// <summary> 로비 요청 창구. Command는 클라이언트 쪽 인스턴스에서 불러야 서버로 간다 </summary>
        private static LobbyController FindClientLobbyController()
        {
            foreach (NetworkIdentity identity in NetworkClient.spawned.Values)
            {
                if (identity == null)
                {
                    continue;
                }

                LobbyController lobby = identity.GetComponent<LobbyController>();
                if ((lobby != null) && (lobby.isActiveAndEnabled))
                {
                    return lobby;
                }
            }

            return null;
        }

        private static string DescribeNetwork()
        {
            if ((NetworkServer.active) && (NetworkClient.active))
            {
                return "호스트";
            }

            if (NetworkServer.active)
            {
                return "서버";
            }

            if (NetworkClient.active)
            {
                return "클라이언트";
            }

            return "끊김";
        }

        private static string DescribeTransport()
        {
            Transport transport = Transport.active;
            if (transport == null)
            {
                return "없음";
            }

            return SteamTransportSelector.IsSteamTransport(transport) ? "Steam" : "KCP";
        }

        private static string DescribeRequiredPlayers()
        {
            int chosen = LobbyController.DevRequiredPlayers;

            if ((chosen > 0) && (chosen <= Constants.Network.MAX_PLAYERS))
            {
                return $"{chosen}인";
            }

            return SCENE_VALUE_OPTION;
        }

        private static string DescribeRunMode()
        {
            switch (LobbyController.DevRunMode)
            {
                case RunMode.BossTrial: return "보스 트라이";
                case RunMode.Dev:       return "개발";
                default:                return "정식";
            }
        }

#endif
    }
}
