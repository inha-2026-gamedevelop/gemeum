// Unity
using UnityEngine;
using UnityEngine.SceneManagement;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Dev;

namespace Geumeum.Art
{
    /// <summary> 보스 아레나의 페이즈를 키로 바꾼다. 개발 확인용이고 게임 규칙에 관여하지 않는다 </summary>
    public class BossPhaseDebugSwitcher : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS

        /****************************************
        *                Fields
        ****************************************/

        private const string PANEL_ID    = "bossphase";
        private const string PANEL_TITLE = "보스 페이즈";

        [Header("대상")]
        [SerializeField] private BossArenaPhaseView _phaseView;

        [Header("재생 시작 시 적용할 페이즈")]
        [SerializeField] private BossArenaPhase _startPhase = BossArenaPhase.Shrine;

        // 페이즈 전환 키
        [Header("키")]
        [SerializeField] private KeyCode _shrineKeyCode     = KeyCode.F6;
        [SerializeField] private KeyCode _underworldKeyCode = KeyCode.F7;
        // 토글은 기본으로 끈다. F8은 GDebugOverlay 토글, F3은 런 포기가 이미 쓰고
        // F6/F7이 두 페이즈를 직접 주므로 키를 하나 더 차지할 이유가 없다
        [SerializeField] private KeyCode _toggleKeyCode     = KeyCode.None;


        /****************************************
        *              Unity Event
        ****************************************/

        /// <summary>
        /// 씬에 배치하지 않아도 실행 시작에 스스로 붙는다.
        /// 보스 아레나는 런 도중에 로드되므로, 아레나 씬에 심어 두면 그 씬을 직접 열었을 때만 동작한다
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Preload()
        {
            DevToolHost.Attach<BossPhaseDebugSwitcher>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            ResolveView();

            DevPanelHost.Register(PANEL_ID, PANEL_TITLE, Build, Handle);
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;

            DevPanelHost.Unregister(PANEL_ID);
        }

        private void HandleSceneLoaded(Scene loaded, LoadSceneMode mode)
        {
            ResolveView();
        }

        /// <summary>
        /// 아레나 뷰를 찾는다. 아직 없으면 조용히 넘긴다 - 보스 아레나 밖에서는 없는 게 정상이다.
        /// Find 계열이라 매 프레임 돌리지 않고 씬이 바뀔 때만 부른다
        /// </summary>
        private void ResolveView()
        {
            if (_phaseView != null)
            {
                return;
            }

            _phaseView = FindAnyObjectByType<BossArenaPhaseView>();
            if (_phaseView == null)
            {
                return;
            }

            _phaseView.ShowPhase(_startPhase);
        }

        private void Update()
        {
            if (_phaseView == null)
            {
                return;
            }

            if (Input.GetKeyDown(_shrineKeyCode))
            {
                _phaseView.ShowPhase(BossArenaPhase.Shrine);
                return;
            }

            if (Input.GetKeyDown(_underworldKeyCode))
            {
                _phaseView.ShowPhase(BossArenaPhase.Underworld);
                return;
            }

            if (!Input.GetKeyDown(_toggleKeyCode))
            {
                return;
            }

            bool isShrine = (_phaseView.CurrentPhase == BossArenaPhase.Shrine);
            _phaseView.ShowPhase(isShrine ? BossArenaPhase.Underworld : BossArenaPhase.Shrine);
        }

        /// <summary>
        /// 페이즈 전환은 브라우저 개발 패널에서 누른다.
        /// 화면 왼쪽 위 고정 라벨은 다른 개발 표시와 같은 자리를 써서 서로를 덮었다
        /// </summary>
        private void Build(DevPanelBuilder builder)
        {
            if (_phaseView == null)
            {
                builder.Label("보스 아레나 밖이라 전환할 것이 없다");
                return;
            }

            bool isShrine = (_phaseView.CurrentPhase == BossArenaPhase.Shrine);

            builder.Label(isShrine ? "지금 1페이즈 산속 사당" : "지금 2페이즈 저승 물길");
            builder.Button("shrine",     $"1페이즈로 ({_shrineKeyCode})");
            builder.Button("underworld", $"2페이즈로 ({_underworldKeyCode})");
        }

        private void Handle(DevCommand command)
        {
            if (_phaseView == null)
            {
                return;
            }

            if (command.Control == "shrine")
            {
                _phaseView.ShowPhase(BossArenaPhase.Shrine);
                return;
            }

            if (command.Control == "underworld")
            {
                _phaseView.ShowPhase(BossArenaPhase.Underworld);
            }
        }

#endif
    }
}
