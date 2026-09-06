// Unity
using UnityEngine;

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

        // F1 은 호스트 시작, F2 는 ParryTestDummy, F3 은 런 포기, F5 는 씬 전환,
        // F9/F10 은 프레임 기록기가 이미 쓴다. 겹치면 페이즈를 바꾸려다 런이 끝난다
        [Header("키")]
        [SerializeField] private KeyCode _shrineKeyCode     = KeyCode.F6;
        [SerializeField] private KeyCode _underworldKeyCode = KeyCode.F7;
        [SerializeField] private KeyCode _toggleKeyCode     = KeyCode.F8;

        [Header("화면에 현재 페이즈를 표시한다")]
        [SerializeField] private bool _showLabel = true;

        private GUIStyle _labelStyle;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_phaseView != null)
            {
                return;
            }

            // 개발 도구라 인스펙터 연결을 잊어도 동작해야 한다. 초기화 1회 폴백이고 결과를 캐싱한다
            _phaseView = FindAnyObjectByType<BossArenaPhaseView>();
            if (_phaseView == null)
            {
                Debug.LogError("[BossPhaseDebugSwitcher] BossArenaPhaseView가 씬에 없다", this);
            }
        }

        private void Start()
        {
            if (_phaseView == null)
            {
                return;
            }

            // 에디터에서 2페이즈를 켜 둔 채 저장했어도 재생은 항상 같은 곳에서 시작한다
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
                phaseName + "   [" + _shrineKeyCode + "] 1P  [" + _underworldKeyCode + "] 2P  ["
                + _toggleKeyCode + "] 전환", _labelStyle);
        }

#endif
    }
}
