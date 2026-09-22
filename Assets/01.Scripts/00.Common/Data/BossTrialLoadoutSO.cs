// Unity
using UnityEngine;

namespace Geumeum.Data
{
    /// <summary>
    /// 보스 트라이의 시작 자원. 30분을 걷지 않고 보스 앞에 서는 대신,
    /// 필드 30분치 기대 획득량을 편성 포인트로 나눠 쓴다
    /// 값은 실측 전 임시값이고 코드가 아니라 이 에셋에서 조정한다
    /// </summary>
    [CreateAssetMenu(fileName = "BossTrialLoadoutSO", menuName = "Geumeum/BossTrialLoadoutSO")]
    public class BossTrialLoadoutSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("시작 상태")]
        [SerializeField, Min(1)] private int _startLevel = 10;
        [SerializeField, Min(0)] private int _startingCurrency = 0;

        [Header("편성 예산 - 필드 30분 기대 획득량. 실측 전 임시값")]
        [SerializeField, Min(0)] private int _loadoutPoints = 100;

        [Tooltip("받을 장비의 필드 레벨별 비용. 음수면 그 레벨은 고를 수 없다")]
        [SerializeField] private int[] _equipmentTierCosts = { 10, 25, 45, 70 };

        [SerializeField, Min(0)] private int _potionCost = 10;
        [SerializeField, Min(0)] private int _maxPotions = 3;

        /****************************************
        *              Properties
        ****************************************/

        public int StartLevel       => Mathf.Max(1, _startLevel);
        public int StartingCurrency => Mathf.Max(0, _startingCurrency);
        public int LoadoutPoints    => Mathf.Max(0, _loadoutPoints);
        public int PotionCost       => Mathf.Max(0, _potionCost);
        public int MaxPotions       => Mathf.Max(0, _maxPotions);
        public int EquipmentTierCount => (_equipmentTierCosts == null) ? 0 : _equipmentTierCosts.Length;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 해당 필드 레벨 장비를 고를 수 있으면 비용을 돌려준다 </summary>
        public bool TryGetEquipmentCost(int tier, out int cost)
        {
            cost = 0;
            if ((_equipmentTierCosts == null) || (tier < 0) || (tier >= _equipmentTierCosts.Length))
            {
                return false;
            }

            cost = _equipmentTierCosts[tier];
            return (cost >= 0);
        }

        public bool ValidateDefinition(out string failure)
        {
            if (_startLevel < 1)
            {
                failure = "boss trial start level must be at least 1";
                return false;
            }

            if ((_equipmentTierCosts == null) || (_equipmentTierCosts.Length == 0))
            {
                failure = "boss trial equipment tier costs must not be empty";
                return false;
            }

            if ((_loadoutPoints < 0) || (_potionCost < 0) || (_maxPotions < 0) || (_startingCurrency < 0))
            {
                failure = "boss trial budget values must not be negative";
                return false;
            }

            failure = null;
            return true;
        }
    }
}
