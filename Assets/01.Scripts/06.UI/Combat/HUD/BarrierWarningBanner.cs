// Unity
using TMPro;
using UnityEngine;

// 프로젝트 내부
using Geumeum.Combat;
using Geumeum.Common;

namespace Geumeum.UI
{
    /// <summary>
    /// 결계의 축소 예고, 축소 중, 결계 밖을 로컬 화면에 배너로 알린다
    /// 시각은 전부 서버가 확정한 snapshot에서 읽으므로 세 화면이 같은 문구를 같은 시점에 본다
    /// 판정은 하지 않는다. 결계 밖 피해는 서버가 이미 주고 있고 여기는 그것을 알리기만 한다
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class BarrierWarningBanner : MonoBehaviour
    {
        /****************************************
        *                 Types
        ****************************************/

        private enum BannerState
        {
            None      = 0,
            Warning   = 1,
            Shrinking = 2,
            Outside   = 3,
        }

        /****************************************
        *                Fields
        ****************************************/

        private const int NO_COUNTDOWN = -1; // 카운트다운을 그리지 않는 상태

        [Header("배너")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private TMP_Text    _messageText;

        [Header("문구")]
        [Tooltip("{0}은 남은 시간(초)으로 치환된다")]
        [SerializeField] private string _warningFormat  = "결계가 곧 좁아진다  {0}초";
        [SerializeField] private string _shrinkingText  = "결계가 좁아지고 있다";
        [SerializeField] private string _outsideText    = "결계 밖이다  안으로 들어가라";

        [Header("색")]
        [SerializeField] private Color _warningColor   = new Color(1f, 0.82f, 0.35f, 1f);
        [SerializeField] private Color _shrinkingColor = new Color(1f, 0.55f, 0.2f, 1f);
        [SerializeField] private Color _outsideColor   = new Color(1f, 0.3f, 0.3f, 1f);

        private BannerState _state             = BannerState.None;
        private int         _shownRemainingSec = NO_COUNTDOWN;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (HasMissingReference())
            {
                enabled = false;
                return;
            }

            Apply(BannerState.None);
        }

        private void OnDisable()
        {
            // Awake의 참조 검사에서 꺼진 경우에도 여기로 들어오므로 참조를 다시 본다
            if (_canvasGroup == null)
            {
                return;
            }

            Apply(BannerState.None);
        }

        private void Update()
        {
            IBarrierTransitionSource source   = BarrierStateLocator.TransitionSource;
            BarrierTransitionState   snapshot = source.Snapshot;

            // Revision 0은 서버가 아직 원을 세우지 않은 것이라 시각 필드가 전부 0이다
            if (!snapshot.IsActive || (snapshot.Revision == 0u))
            {
                Apply(BannerState.None);
                return;
            }

            // 결계 타임라인은 필드 시계 기준이다. 보스전에서 멈추는 그 시계와 같은 것을 봐야 어긋나지 않는다
            double now = RunTimeLocator.Current.FieldElapsedSeconds;

            if (IsLocalPlayerOutside(source))
            {
                Apply(BannerState.Outside);
                return;
            }

            if ((now >= snapshot.StartGameplayTime) && (now < snapshot.EndGameplayTime))
            {
                Apply(BannerState.Shrinking);
                return;
            }

            if ((now >= snapshot.WarningGameplayTime) && (now < snapshot.StartGameplayTime))
            {
                Apply(BannerState.Warning, snapshot.StartGameplayTime - now);
                return;
            }

            Apply(BannerState.None);
        }

        /****************************************
        *                Methods
        ****************************************/

        private bool IsLocalPlayerOutside(IBarrierState state)
        {
            PlayerStatus local = PlayerStatus.Local;

            // 쓰러졌거나 관전 중이면 위치가 의미 없다. 들어가라고 띄워봐야 할 수 있는 것이 없다
            if ((local == null) || local.IsDead)
            {
                return false;
            }

            return state.IsOutside(local.transform.position);
        }

        private void Apply(BannerState state, double remainingSeconds = 0d)
        {
            int remainingSec = (state == BannerState.Warning)
                ? Mathf.Max(0, Mathf.CeilToInt((float)remainingSeconds))
                : NO_COUNTDOWN;

            if ((state == _state) && (remainingSec == _shownRemainingSec))
            {
                return;
            }

            bool changedState = (state != _state);

            _state             = state;
            _shownRemainingSec = remainingSec;

            _canvasGroup.alpha = (state == BannerState.None) ? 0f : 1f;

            if (state == BannerState.None)
            {
                return;
            }

            if (changedState)
            {
                _messageText.color = GetColor(state);
            }

            if (state == BannerState.Warning)
            {
                _messageText.text = string.Format(_warningFormat, remainingSec);
                return;
            }

            _messageText.text = (state == BannerState.Outside) ? _outsideText : _shrinkingText;
        }

        private Color GetColor(BannerState state)
        {
            switch (state)
            {
                case BannerState.Outside:
                    return _outsideColor;
                case BannerState.Shrinking:
                    return _shrinkingColor;
                default:
                    return _warningColor;
            }
        }

        private bool HasMissingReference()
        {
            if (_canvasGroup == null)
            {
                Debug.LogError("[BarrierWarningBanner] CanvasGroup 참조가 비어 있다", this);
                return true;
            }

            if (_messageText == null)
            {
                Debug.LogError("[BarrierWarningBanner] 문구 Text 참조가 비어 있다", this);
                return true;
            }

            return false;
        }
    }
}
