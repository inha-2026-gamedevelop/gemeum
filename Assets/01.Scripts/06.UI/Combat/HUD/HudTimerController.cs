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
    /// 우상단 시간 슬롯의 숫자를 적는다.
    ///
    /// 서버가 보낸 시계 하나에서 각 클라이언트가 계산한다 - 매 초 값을 받지도, 로컬로 세지도 않는다.
    /// 그래서 런 도중 들어온 사람도 같은 숫자를 본다.
    ///
    /// 필드에서 적는 것은 지금까지 쓴 시간이다. 축소까지 남은 시간이 아니다 -
    /// 그 숫자는 생존만 다루고 "거점 하나 더 칠 시간이 있나"에 답하지 못한다.
    /// 파밍이 길어질수록 점수의 시간 보너스가 줄어드는데, 그 비용이 보이는 곳은 여기뿐이다.
    ///
    /// 보스 구간에 들어가면 같은 슬롯이 보스 잔여 시간으로 바뀐다. 슬롯을 늘리지 않는 이유는
    /// 필드 시계가 그 시점부터 멈춰 있어 두 줄로 두면 죽은 숫자가 하나 남기 때문이다.
    /// 숫자가 붉어지는 조건도 구간마다 다르다. 필드는 결계 축소, 보스는 남은 시간이다
    /// </summary>
    public class HudTimerController : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int SECONDS_PER_MINUTE = 60;

        // 아직 한 번도 적지 않았다는 표시. 어떤 경과 시간과도 같지 않아야 한다
        private const int NOT_SHOWN = -1;

        [Header("표시")]
        [SerializeField] private TMP_Text _label = null;

        // 초가 그대로인데 다시 적으면 TMP가 메시를 매 프레임 다시 만든다
        private int _shownSeconds = NOT_SHOWN;

        [Header("색")]
        [Tooltip("평시 숫자 색. 한지 백색")]
        [SerializeField] private Color _normalColor  = Color.white;

        [Tooltip("결계 경고와 축소가 진행되는 동안의 숫자 색. 주묵 붉은색")]
        [SerializeField] private Color _warningColor = new Color(0.78f, 0.15f, 0.15f, 1f);

        [Tooltip("보스 남은 시간이 이 값(초) 이하면 숫자를 붉게 한다. HUD_DESIGN 2.2 상태 5")]
        [SerializeField] private float _bossWarningSeconds = 180f;

        // 색이 그대로인데 다시 넣으면 TMP가 메시를 매 프레임 다시 만든다. _shownSeconds와 같은 이유다
        private Color _shownColor;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            _shownSeconds = NOT_SHOWN; // 다시 켜졌을 때 이전 런의 숫자가 남지 않게 한 번은 적는다
        }

        private void Update()
        {
            if (_label == null)
            {
                return;
            }

            ApplyColor();

            if (!TryResolveDisplaySeconds(out int seconds))
            {
                return;
            }

            if (seconds == _shownSeconds)
            {
                return;
            }

            _shownSeconds = seconds;

            _label.text = $"{(seconds / SECONDS_PER_MINUTE):00}:{(seconds % SECONDS_PER_MINUTE):00}";
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 두 구간이 세는 방향이 다르다.
        ///
        /// 필드는 0:00에서 올라간다. 게이트가 상시 열려 있어 필드에는 강제 종료가 없고,
        /// 시간은 점수로만 비용이 된다. 내려가는 숫자는 없는 마감을 있는 것처럼 보이게 한다.
        /// 보스는 14:00에서 내려간다. 여기는 실제로 시간이 다하면 런이 끝난다.
        ///
        /// 두 시계를 합쳐 세지 않는다. 합치면 필드에서만 도는 런에서 숫자가 14분 크게 시작한다
        /// </summary>
        private bool TryResolveDisplaySeconds(out int seconds)
        {
            seconds = 0;

            // 상한은 GameDB에 있다. 부트 전에 읽으면 예외가 난다.
            // 이때 00:00을 적으면 보스 시간이 다 됐다는 뜻으로 읽히므로 아무것도 적지 않는다
            if (!GameDB.IsInitialized)
            {
                return false;
            }

            IRunTimeSource time = RunTimeLocator.Current;

            if (IsBossPhase())
            {
                seconds = ToRemainingSeconds(
                    GameDB.Boss.BossTimeLimitSeconds - time.BossElapsedSeconds);
                return true;
            }

            seconds = ToElapsedSeconds(
                time.FieldElapsedSeconds, GameDB.Barrier.FieldTimeLimitSeconds);
            return true;
        }

        /// <summary>
        /// 보스 구간인지. phase는 SyncVar라 세 클라이언트가 같은 시점에 같이 바뀐다.
        ///
        /// 보스 경과가 0보다 큰지로 판별하지 않는다 - 보스 시계가 막 시작한 첫 프레임에
        /// 0이라서, 그 한 프레임 동안 필드 시계의 00:00이 튀어 보인다
        /// </summary>
        private static bool IsBossPhase()
        {
            RunManager run = RunManager.LocalInstance;
            return ((run != null) && (run.Phase == RunPhase.Boss));
        }

        // 카운트다운은 올림이다. 0.4초 남았는데 00:00으로 보이면 이미 끝난 것으로 읽힌다
        private static int ToRemainingSeconds(double remaining)
        {
            return Mathf.Max(0, Mathf.CeilToInt((float) remaining));
        }

        /// <summary>
        /// 카운트업은 내림이다. 런이 시작하자마자 00:01로 보이면 1초를 건너뛴 것으로 읽힌다.
        ///
        /// 상한에서 멈춘다. 서버가 이미 필드 경과를 상한에서 고정하지만,
        /// 보스 전환이 늦어지는 동안 표시가 30:00을 넘어가면 없는 시간이 생긴 것처럼 보인다
        /// </summary>
        private static int ToElapsedSeconds(double elapsed, double limit)
        {
            double clamped = (elapsed > limit) ? limit : elapsed;
            return Mathf.Max(0, Mathf.FloorToInt((float) clamped));
        }

        /// <summary>
        /// 구간에 맞는 색을 고른다. 필드는 결계 축소를, 보스는 남은 시간을 본다.
        ///
        /// 필드에서는 축소 시각을 직접 세지 않는다. 서버가 확정해 보낸 구간을 배너와
        /// 똑같이 읽으므로 배너가 뜨는 순간과 숫자가 붉어지는 순간이 어긋나지 않는다
        /// </summary>
        private void ApplyColor()
        {
            // 보스 상한을 GameDB에서 읽는다. 부트 전에 읽으면 예외가 나므로 색도 건드리지 않는다
            if (!GameDB.IsInitialized)
            {
                return;
            }

            bool isWarning = IsBossPhase() ? IsBossTimeCritical() : IsBarrierWarning();

            Color next = isWarning ? _warningColor : _normalColor;
            if (next == _shownColor)
            {
                return;
            }

            _shownColor  = next;
            _label.color = next;
        }

        /// <summary>
        /// 결계 경고 또는 축소가 진행 중인지. 경고 시작부터 축소 종료까지를 한 구간으로 본다.
        ///
        /// Revision 0은 서버가 아직 원을 세우지 않은 것이라 시각 필드가 전부 0이다.
        /// 거르지 않으면 런이 시작하자마자 숫자가 한 번 붉어진다
        /// </summary>
        private static bool IsBarrierWarning()
        {
            BarrierTransitionState snapshot = BarrierStateLocator.TransitionSource.Snapshot;

            if ((!snapshot.IsActive) || (snapshot.Revision == 0u))
            {
                return false;
            }

            // 결계 타임라인은 필드 시계 기준이다. 보스전에서 멈추는 그 시계와 같은 것을 봐야 한다
            double now = RunTimeLocator.Current.FieldElapsedSeconds;
            return ((now >= snapshot.WarningGameplayTime) && (now < snapshot.EndGameplayTime));
        }

        /// <summary>
        /// 보스 제한 시간이 임계 이하로 남았는지.
        ///
        /// 결계 스냅샷을 보지 않는다 - 결계 타임라인은 필드 시계 기준인데 그 시계는
        /// 보스 진입 시점에 멈춘다. 축소 구간 중에 입장하면 보스전 내내 참이 된다.
        ///
        /// 숫자를 다시 계산하지 않고 상한에서 경과를 뺀다. TryResolveDisplaySeconds와
        /// 같은 값을 쓰므로 03:00이 찍히는 순간과 붉어지는 순간이 어긋나지 않는다
        /// </summary>
        private bool IsBossTimeCritical()
        {
            double remaining = GameDB.Boss.BossTimeLimitSeconds
                             - RunTimeLocator.Current.BossElapsedSeconds;
            return (remaining <= _bossWarningSeconds);
        }
    }
}
