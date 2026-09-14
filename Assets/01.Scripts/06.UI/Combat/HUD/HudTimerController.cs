// Unity
using UnityEngine;

// 서드파티
using TMPro;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.UI
{
    /// <summary>
    /// 우상단 시간 슬롯의 숫자를 적는다.
    ///
    /// 서버가 보낸 시계 하나에서 각 클라이언트가 계산한다 - 매 초 값을 받지도, 로컬로 세지도 않는다.
    /// 그래서 런 도중 들어온 사람도 같은 숫자를 본다.
    ///
    /// 적는 것은 런 전체의 남은 시간이다. 축소까지 남은 시간이 아니다 -
    /// 그 숫자는 생존만 다루고 "거점 하나 더 칠 시간이 있나"에 답하지 못한다.
    /// 파밍이 길어질수록 점수의 시간 보너스가 줄어드는데, 그 비용이 보이는 곳은 여기뿐이다.
    ///
    /// 기준 시간을 넘기면 00:00에서 멈춘다. 보너스가 사라졌다는 뜻이라 고장이 아니다.
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
        /// 기준 시간에서 게임 진행 시간을 뺀다. 기준은 필드 상한과 보스 시계 상한의 합이고,
        /// RunManager가 정산 때 쓰는 조합과 같다 - 한쪽 수치를 고치면 여기도 따라간다
        /// </summary>
        private bool TryResolveRemainingSeconds(out int seconds)
        {
            seconds = 0;

            // 기준 시간은 GameDB에 있다. 부트 전에 읽으면 예외가 난다.
            // 이때 00:00을 적으면 "보너스가 사라졌다"로 읽히므로 아무것도 적지 않는다
            if (!GameDB.IsInitialized)
            {
                return false;
            }

            double limit = GameDB.Barrier.FieldTimeLimitSeconds + GameDB.Boss.BossTimeLimitSeconds;

            // 필드와 보스를 합한 시간이다. 보스 씬으로 넘어가도 같은 숫자가 이어서 줄어든다
            double elapsed = RunTimeLocator.Current.GameplayElapsedSeconds;

            seconds = ToRemainingSeconds(limit - elapsed);
            return true;
        }

        // 카운트다운은 올림이다. 0.4초 남았는데 00:00으로 보이면 이미 끝난 것으로 읽힌다
        private static int ToRemainingSeconds(double remaining)
        {
            return Mathf.Max(0, Mathf.CeilToInt((float) remaining));
        }
    }
}