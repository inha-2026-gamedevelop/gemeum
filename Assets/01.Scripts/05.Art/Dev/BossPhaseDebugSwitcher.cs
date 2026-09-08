// Unity
using UnityEngine;
using UnityEngine.SceneManagement;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Art
{
    /// <summary> 보스 아레나의 페이즈를 키로 바꾼다. 개발 확인용이고 게임 규칙에 관여하지 않는다 </summary>
    public class BossPhaseDebugSwitcher : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD

        /****************************************
        *                Fields
        ****************************************/

        private const float LABEL_MARGIN = 10f;
        private const float LABEL_WIDTH  = 320f;
        private const float LABEL_HEIGHT = 22f;

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

        [Header("화면에 현재 페이즈를 표시한다")]
        [SerializeField] private bool _showLabel = true;

        private GUIStyle _labelStyle;

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
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
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

        private void OnGUI()
        {
            if ((!_showLabel) || (_phaseView == null))
            {
                return;
            }

            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label);
                _labelStyle.normal.textColor = Color.white;
            }

            bool isShrine = (_phaseView.CurrentPhase == BossArenaPhase.Shrine);
            string phaseName = isShrine ? "1페이즈 산속 사당" : "2페이즈 저승 물길";
            GUI.Label(new Rect(LABEL_MARGIN, LABEL_MARGIN, LABEL_WIDTH, LABEL_HEIGHT),
                phaseName + "   [" + _shrineKeyCode + "] 1P  [" + _underworldKeyCode + "] 2P"
                + ((_toggleKeyCode == KeyCode.None) ? "" : "  [" + _toggleKeyCode + "] 전환"), _labelStyle);
        }

#endif
    }
}
