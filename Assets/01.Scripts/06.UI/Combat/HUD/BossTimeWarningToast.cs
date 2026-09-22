// Unity
using UnityEngine;

// 서드파티
using TMPro;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.UI
{
    /// <summary>
    /// 보스 제한 시간이 임계 아래로 내려가는 순간을 한 번만 알린다.
    ///
    /// 결계 배너와 달리 조건이 참인 동안 계속 떠 있지 않는다. 남은 3분 내내 문구가 붙어 있으면
    /// 보스 패턴을 가리고, 정작 알려야 할 "지금 막 3분이 됐다"는 사건이 묻힌다.
    /// 지속 상태를 보여주는 일은 우상단 시계가 이미 맡고 있다 - 그쪽은 같은 임계에서 붉어진다.
    ///
    /// 임계를 넘는 순간을 직접 보지 못했으면 띄우지 않는다. 런 도중 들어온 사람이
    /// 남은 30초에 "3분" 문구를 보면 거짓말이 된다.
    ///
    /// 판정은 없다. 시간 초과는 서버가 이미 처리하고 여기는 알리기만 한다
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class BossTimeWarningToast : MonoBehaviour
    {
        /****************************************
        *                 Types
        ****************************************/

        /// <summary> 직전 프레임이 임계의 어느 쪽이었는지. 첫 관측은 어느 쪽도 아니다 </summary>
        private enum ThresholdSide
        {
            NotObserved = 0,
            Above       = 1,
            Below       = 2,
        }

        /****************************************
        *                Fields
        ****************************************/

        private const int SECONDS_PER_MINUTE = 60;

        // 어떤 알파와도 같지 않아야 한다. 처음 한 번은 반드시 적게 하는 표시다
        private const float NOT_SHOWN = -1f;

        [Header("표시")]
        [SerializeField] private CanvasGroup _canvasGroup = null;
        [SerializeField] private TMP_Text    _messageText = null;

        [Header("문구")]
        [Tooltip("{0}은 임계 시간이 mm:ss로 치환된다")]
        [SerializeField] private string _warningFormat = "보스 처치까지 {0}";

        [Header("색")]
        [Tooltip("주묵 붉은색. HudTimerController의 경고색과 같은 값을 쓴다")]
        [SerializeField] private Color _warningColor = new Color(0.78f, 0.15f, 0.15f, 1f);

        [Header("시간")]
        [Tooltip("남은 시간이 이 값(초) 이하로 내려가면 띄운다. "
               + "HudTimerController의 같은 값과 맞춰야 숫자가 붉어지는 순간과 어긋나지 않는다")]
        [SerializeField] private float _warningSeconds = 180f;

        [Tooltip("또렷하게 떠 있는 시간(초)")]
        [SerializeField] private float _holdSeconds = 3f;

        [Tooltip("사라지는 데 걸리는 시간(초)")]
        [SerializeField] private float _fadeSeconds = 0.8f;

        private ThresholdSide _side = ThresholdSide.NotObserved;

        // 0보다 크면 떠 있다. 남은 값이 _fadeSeconds 아래로 내려가면 그때부터 흐려진다
        private float _remainingSeconds = 0f;

        // 알파가 그대로인데 다시 넣으면 캔버스가 매 프레임 다시 그려진다
        private float _shownAlpha = NOT_SHOWN;

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

            HideNow();
        }

        private void OnEnable()
        {
            // 다시 켜졌을 때 이전 런의 토스트가 남지 않게 한다
            HideNow();
        }

        private void OnDisable()
        {
            // Awake의 참조 검사에서 꺼진 경우에도 여기로 들어오므로 참조를 다시 본다
            if (_canvasGroup == null)
            {
                return;
            }

            HideNow();
        }

        private void Update()
        {
            TrackThreshold();
            TickToast();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 임계를 위에서 아래로 넘는 순간만 잡는다.
        ///
        /// 아래에 있다는 사실이 아니라 넘었다는 사건이 조건이다. 사실로 잡으면
        /// 매 프레임 참이라 토스트가 끝나는 즉시 다시 뜬다.
        /// </summary>
        private void TrackThreshold()
        {
            if (!IsBossPhase())
            {
                // 보스 구간을 벗어나면 되돌린다. 다음 보스전에서 다시 한 번 뜬다
                _side = ThresholdSide.NotObserved;
                return;
            }

            // 상한은 GameDB에 있다. 부트 전에 읽으면 예외가 난다
            if (!GameDB.IsInitialized)
            {
                return;
            }

            double remaining = GameDB.Boss.BossTimeLimitSeconds
                             - RunTimeLocator.Current.BossElapsedSeconds;

            ThresholdSide now = (remaining <= _warningSeconds)
                ? ThresholdSide.Below
                : ThresholdSide.Above;

            // NotObserved에서 바로 Below로 들어온 것은 넘은 것이 아니라 이미 아래였던 것이다
            if ((now == ThresholdSide.Below) && (_side == ThresholdSide.Above))
            {
                Show();
            }

            _side = now;
        }

        /// <summary>
        /// 떠 있는 동안 남은 시간을 줄이고 마지막 _fadeSeconds 구간에서 흐려진다.
        ///
        /// 코루틴을 쓰지 않는다 - 런 도중 다시 뜨는 경우에 중복 시작과 취소를 관리해야 하는데,
        /// 값 하나로 세면 그 문제 자체가 생기지 않는다.
        ///
        /// deltaTime으로 센다. 배속이나 정지가 걸린 화면에서 토스트만 제 속도로 흐르면
        /// 무엇이 멈춘 것인지 읽히지 않는다.
        /// </summary>
        private void TickToast()
        {
            if (_remainingSeconds <= 0f)
            {
                ApplyAlpha(0f);
                return;
            }

            _remainingSeconds -= Time.deltaTime;

            float alpha = (_fadeSeconds > 0f)
                ? Mathf.Clamp01(_remainingSeconds / _fadeSeconds)
                : ((_remainingSeconds > 0f) ? 1f : 0f);

            ApplyAlpha(alpha);
        }

        /// <summary>
        /// 문구와 색은 뜰 때 한 번만 넣는다. 매 프레임 넣으면 TMP가 메시를 다시 만든다
        /// </summary>
        private void Show()
        {
            int threshold = Mathf.CeilToInt(_warningSeconds);

            _messageText.color = _warningColor;
            _messageText.text  = string.Format(
                _warningFormat,
                $"{(threshold / SECONDS_PER_MINUTE):00}:{(threshold % SECONDS_PER_MINUTE):00}");

            _remainingSeconds = (_holdSeconds + _fadeSeconds);
        }

        private void ApplyAlpha(float alpha)
        {
            if (Mathf.Approximately(alpha, _shownAlpha))
            {
                return;
            }

            _shownAlpha        = alpha;
            _canvasGroup.alpha = alpha;
        }

        private void HideNow()
        {
            _side             = ThresholdSide.NotObserved;
            _remainingSeconds = 0f;
            _shownAlpha       = NOT_SHOWN; // 다음 ApplyAlpha가 반드시 한 번은 적는다

            ApplyAlpha(0f);
        }

        /// <summary>
        /// 보스 구간인지. phase는 SyncVar라 세 클라이언트가 같은 시점에 같이 바뀐다
        /// </summary>
        private static bool IsBossPhase()
        {
            RunManager run = RunManager.LocalInstance;
            return ((run != null) && (run.Phase == RunPhase.Boss));
        }

        private bool HasMissingReference()
        {
            if (_canvasGroup == null)
            {
                Debug.LogError("[BossTimeWarningToast] CanvasGroup 참조가 비어 있다", this);
                return true;
            }

            if (_messageText == null)
            {
                Debug.LogError("[BossTimeWarningToast] 문구 Text 참조가 비어 있다", this);
                return true;
            }

            return false;
        }
    }
}
