// System
using System.Collections.Generic;

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
    /// 적는 것은 세 가지 중 하나다.
    /// 축소가 진행 중이면 축소가 끝날 때까지, 아니면 다음 축소가 시작할 때까지 남은 시간이다.
    /// 둘 다 없으면(런 시작 전, 마지막 단계, 보스전) 경과 시간으로 돌아간다.
    ///
    /// 단계별 색 전환은 아직 없다. 임계값을 코드에 박지 않으려면 HUD용 SO가 필요하다
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

            int seconds = ResolveDisplaySeconds();
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

        private int ResolveDisplaySeconds()
        {
            if (TryResolveRemaining(out int remaining))
            {
                return remaining;
            }

            // 결계 일정이 없는 구간이다. 숫자를 비우면 슬롯이 빈 액자로 남으므로 경과 시간을 적는다
            return Mathf.Max(0, Mathf.FloorToInt((float) RunTimeLocator.Current.GameplayElapsedSeconds));
        }

        /// <summary>
        /// 다음 위험까지 남은 시간. 축소가 진행 중이면 그것이 먼저다 -
        /// 지금 좁혀지고 있는 원이 나중에 올 축소보다 급하다
        /// </summary>
        private bool TryResolveRemaining(out int seconds)
        {
            seconds = 0;

            // 결계 일정은 GameDB에 있다. 부트 전에 읽으면 예외가 난다
            if (!GameDB.IsInitialized)
            {
                return false;
            }

            // 결계 판정이 쓰는 시계와 같은 것을 본다. 보스전에서는 이 시계가 더 이상 늘지 않는다
            double now = RunTimeLocator.Current.FieldElapsedSeconds;

            BarrierTransitionState snapshot = BarrierStateLocator.TransitionSource.Snapshot;
            if ((now >= snapshot.StartGameplayTime) && (now < snapshot.EndGameplayTime))
            {
                seconds = ToRemainingSeconds(snapshot.EndGameplayTime - now);
                return true;
            }

            if (!TryFindNextShrinkTime(now, out double nextShrinkTime))
            {
                return false;
            }

            seconds = ToRemainingSeconds(nextShrinkTime - now);
            return true;
        }

        /// <summary> 아직 오지 않은 첫 단계의 시작 시각. 단계는 시간순으로 정의되어 있다 </summary>
        private static bool TryFindNextShrinkTime(double now, out double startSeconds)
        {
            startSeconds = 0.0;

            IReadOnlyList<BarrierPhaseSO.PhaseEntry> phases = GameDB.Barrier.Phases;
            if (phases == null)
            {
                return false;
            }

            for (int i = 0; i < phases.Count; ++i)
            {
                if (phases[i].StartSeconds <= now)
                {
                    continue;
                }

                startSeconds = phases[i].StartSeconds;
                return true;
            }

            return false;
        }

        // 카운트다운은 올림이다. 0.4초 남았는데 00:00으로 보이면 이미 끝난 것으로 읽힌다
        private static int ToRemainingSeconds(double remaining)
        {
            return Mathf.Max(0, Mathf.CeilToInt((float) remaining));
        }
    }
}
