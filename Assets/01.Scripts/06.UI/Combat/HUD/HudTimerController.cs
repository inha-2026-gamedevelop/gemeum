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
    /// 적는 것은 필드에 남은 시간이다. 축소까지 남은 시간이 아니다 -
    /// 그 숫자는 생존만 다루고 "거점 하나 더 칠 시간이 있나"에 답하지 못한다.
    /// 파밍이 길어질수록 점수의 시간 보너스가 줄어드는데, 그 비용이 보이는 곳은 여기뿐이다.
    ///
    /// 보스 구간에 들어가면 같은 슬롯이 보스 잔여 시간으로 바뀐다. 슬롯을 늘리지 않는 이유는
    /// 필드 시계가 그 시점부터 00:00에 멈춰 있어 두 줄로 두면 죽은 숫자가 하나 남기 때문이다.
    /// 단계별 색 전환은 아직 없다 - 임계값을 코드에 박지 않으려면 HUD용 SO가 필요하다
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

            if (!TryResolveRemainingSeconds(out int seconds))
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
        /// 지금 도는 시계의 상한에서 그 시계의 경과를 뺀다.
        /// 상한은 둘 다 GameDB가 갖고 있어 밸런스에서 고치면 여기도 따라간다.
        ///
        /// 두 시계를 합쳐 세지 않는다. 합치면 필드에서만 도는 런에서 숫자가 14분 크게 시작한다
        /// </summary>
        private bool TryResolveRemainingSeconds(out int seconds)
        {
            seconds = 0;

            // 상한은 GameDB에 있다. 부트 전에 읽으면 예외가 난다.
            // 이때 00:00을 적으면 시간이 다 됐다는 뜻으로 읽히므로 아무것도 적지 않는다
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

            seconds = ToRemainingSeconds(
                GameDB.Barrier.FieldTimeLimitSeconds - time.FieldElapsedSeconds);
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
    }
}