// Unity
using UnityEngine;

namespace Geumeum.Data
{
    /// <summary>
    /// 점수 항목별 배점.
    /// 네 항목 모두 런이 끝났을 때 남아 있는 것에 점수를 준다
    /// </summary>
    [CreateAssetMenu(fileName = "ScoreRuleSO", menuName = "Geumeum/ScoreRuleSO")]
    public class ScoreRuleSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("배점")]
        [SerializeField] private int _remainingHealthScore    = 1;  // 파티 남은 HP 총합 1당
        [SerializeField] private int _remainingTimeScore      = 10; // 남은 시간 1초당
        [SerializeField] private int _unspentTalentPointScore = 20; // 찍지 않은 특성 포인트 1개당
        [SerializeField] private int _remainingCurrencyScore  = 10; // 남은 노잣돈 1냥당

        /****************************************
        *              Properties
        ****************************************/

        public int RemainingHealthScore    => _remainingHealthScore;
        public int RemainingTimeScore      => _remainingTimeScore;
        public int UnspentTalentPointScore => _unspentTalentPointScore;
        public int RemainingCurrencyScore  => _remainingCurrencyScore;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 남은 HP, 남은 시간, 미사용 특성 포인트, 남은 노잣돈으로 최종 점수를 낸다 </summary>
        public int CalculateFinalScore(float remainingHealthSum, float remainingSeconds, int unspentTalentPoints, int remainingCurrency)
        {
            // 음수 입력이 점수를 깎지 않도록 각 항을 0에서 자른다
            int healthTerm   = Mathf.RoundToInt(Mathf.Max(0f, remainingHealthSum)) * _remainingHealthScore;
            int timeTerm     = Mathf.RoundToInt(Mathf.Max(0f, remainingSeconds)) * _remainingTimeScore;
            int talentTerm   = Mathf.Max(0, unspentTalentPoints) * _unspentTalentPointScore;
            int currencyTerm = Mathf.Max(0, remainingCurrency) * _remainingCurrencyScore;

            return healthTerm + timeTerm + talentTerm + currencyTerm;
        }
    }
}
