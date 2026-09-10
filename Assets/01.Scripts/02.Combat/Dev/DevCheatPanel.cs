// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Dev;

namespace Geumeum.Combat
{
    /// <summary>
    /// 내 캐릭터에만 거는 개발용 치트.
    ///
    /// 자원이 바닥나서 다시 채우기를 기다리는 시간이 테스트마다 붙으면
    /// 정작 보려던 것(패링 타이밍, 보스 패턴)을 보기 전에 판이 끝난다.
    ///
    /// 켜고 끄는 것은 여기서 하지만 판정은 전부 서버가 한다 -
    /// 값을 클라이언트에서 직접 바꾸면 사람마다 다른 결과가 나온다
    /// </summary>
    public class DevCheatPanel : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS

        /****************************************
        *                Preload
        ****************************************/

        /// <summary> 씬에 배치하지 않아도 실행 시작에 스스로 붙는다 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Preload()
        {
            DevToolHost.Attach<DevCheatPanel>();
        }

        /****************************************
        *              Constants
        ****************************************/

        private const string PANEL_ID    = "cheat";
        private const string PANEL_TITLE = "치트";

        private const string CONTROL_INFINITE = "infinite";

        /****************************************
        *                Fields
        ****************************************/

        // 매 프레임 찾지 않는다. 부활·재접속으로 오브젝트가 갈릴 수 있어 죽었을 때만 다시 찾는다
        private PlayerStatus _localStatus = null;

        private string _message = string.Empty;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            DevPanelHost.Register(PANEL_ID, PANEL_TITLE, Build, Handle);
        }

        private void OnDestroy()
        {
            DevPanelHost.Unregister(PANEL_ID);
        }

        /****************************************
        *                Panel
        ****************************************/

        private void Build(DevPanelBuilder builder)
        {
            PlayerStatus status = ResolveLocalStatus();

            if (status == null)
            {
                builder.Label("내 캐릭터가 아직 없다. 런에 들어간 뒤 다시 보라");
                return;
            }

            builder.Header("자원");
            builder.Toggle(CONTROL_INFINITE, "무한 자원 (체력 · 스태미나 · 신력)",
                           status.DevInfiniteResources);
            builder.Label("켜면 피해를 받지 않고, 스태미나와 신력이 줄지 않는다. 내 캐릭터에만 걸린다");

            builder.Label($"체력 {status.Health:F0} / {status.MaxHealth:F0}   "
                        + $"스태미나 {status.Stamina:F0}   신력 {status.Spirit:F0}");

            if (!string.IsNullOrEmpty(_message))
            {
                builder.Label(_message);
            }
        }

        private void Handle(DevCommand command)
        {
            if (command.Control != CONTROL_INFINITE)
            {
                return;
            }

            PlayerStatus status = ResolveLocalStatus();
            if (status == null)
            {
                _message = "내 캐릭터가 없어 적용하지 못했다";
                return;
            }

            status.CmdDevSetInfiniteResources(command.IsOn);

            // 서버가 확정할 때까지 한 틱 걸린다. 눌렀는데 아무 반응이 없어 보이지 않게 남긴다
            _message = (command.IsOn) ? "무한 자원을 켜 달라고 서버에 보냈다"
                                      : "무한 자원을 꺼 달라고 서버에 보냈다";
        }

        /****************************************
        *                Methods
        ****************************************/

        private PlayerStatus ResolveLocalStatus()
        {
            if (_localStatus != null)
            {
                return _localStatus;
            }

            NetworkIdentity local = NetworkClient.localPlayer;
            if (local == null)
            {
                return null;
            }

            local.TryGetComponent(out _localStatus);
            return _localStatus;
        }

#endif
    }
}
