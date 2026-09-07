// Unity
using UnityEngine;

namespace Geumeum.Data
{
    /// <summary> 점수 항목별 배점 </summary>
    [CreateAssetMenu(fileName = "ScoreRuleSO", menuName = "Geumeum/ScoreRuleSO")]
    public class ScoreRuleSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("배점")]
        [Tooltip("보스를 처치했을 때 한 번")]
        [SerializeField] private int _bossKillScore = 10000;

        [Tooltip("잡귀 1마리당. 파밍 손익분기가 마리당 7~10초가 되도록 잡은 값이다")]
        [SerializeField] private int _enemyKillScore = 30;

        [Tooltip("남은 시간 1분당. 초당으로 환산하면 약 6.67점이다")]
        [SerializeField] private int _remainingTimeScorePerMinute = 400;

        [Tooltip("남은 노잣돈 1냥당")]
        [SerializeField] private int _remainingCurrencyScore = 1;

        [Tooltip("파티 남은 HP 총합 1당. 물약과 치유를 쓰는 쪽을 받쳐 아무것도 안 쓰는 빌드로 쏠리는 것을 막는다")]
        [SerializeField] private int _remainingHealthScore = 1;

        [Header("사망")]
        [Tooltip("사망 1회당 감점")]
        [SerializeField] private int _deathPenalty = 1000;

        [Tooltip("사망 횟수별 보너스. 0회부터 순서대로")]
        [SerializeField] private int[] _deathlessBonusByDeathCount = { 5000, 2000, 500, 0 };

        /****************************************
        *              Properties
        ****************************************/

        public int BossKillScore               => _bossKillScore;
        public int EnemyKillScore              => _enemyKillScore;
        public int RemainingTimeScorePerMinute => _remainingTimeScorePerMinute;
        public int RemainingCurrencyScore      => _remainingCurrencyScore;
        public int RemainingHealthScore        => _remainingHealthScore;
        public int DeathPenalty                => _deathPenalty;

        /// <summary> 남은 시간 1초당 점수. 표시용이고 계산은 분당 값으로 한다 </summary>
        public float RemainingTimeScorePerSecond => (_remainingTimeScorePerMinute / 60f);

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 사망 횟수에 해당하는 무사망 보너스. 배열이 비어 있으면 0이다
        /// </summary>
        public int GetDeathlessBonus(int deathCount)
        {
            if ((_deathlessBonusByDeathCount == null) || (_deathlessBonusByDeathCount.Length == 0))
            {
                return 0;
            }

            int index = Mathf.Clamp(deathCount, 0, _deathlessBonusByDeathCount.Length - 1);
            return _deathlessBonusByDeathCount[index];
        }

        /// <summary> 최종 점수를 계산한다. 항목을 모두 더한 뒤 한 번만 내리고 0에서 자른다 </summary>
        public int CalculateFinalScore(
            bool  bossKilled,
            int   enemyKills,
            float remainingSeconds,
            int   remainingCurrency,
            float remainingHealthSum,
            int   deathCount)
        {
            double total = 0.0;

            if (bossKilled)
            {
                total += _bossKillScore;
            }

            total += (double) Mathf.Max(0, enemyKills)          * _enemyKillScore;
            total += (double) Mathf.Max(0f, remainingSeconds)   * _remainingTimeScorePerMinute / 60.0;
            total += (double) Mathf.Max(0, remainingCurrency)   * _remainingCurrencyScore;
            total += (double) Mathf.Max(0f, remainingHealthSum) * _remainingHealthScore;

            int deaths = Mathf.Max(0, deathCount);
            total += GetDeathlessBonus(deaths);
            total -= (double) deaths * _deathPenalty;

            if (total <= 0.0)
            {
                return 0;
            }

            return (int) System.Math.Floor(total);
        }
    }
}
