// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Data
{
    /// <summary> 레벨별 필요 경험치와 최대 HP </summary>
    [CreateAssetMenu(fileName = "LevelCurveSO", menuName = "Geumeum/LevelCurveSO")]
    public class LevelCurveSO : ScriptableObject
    {
        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        public struct LevelEntry
        {
            public int   Level;
            public float MaxHealth;
            public int   RequiredExperience; // 다음 레벨까지, 만렙은 0
        }

        /****************************************
        *                Fields
        ****************************************/

        [Header("레벨")]
        [SerializeField] private int _talentPointPerLevel = 1;

        [Header("곡선")]
        [SerializeField] private LevelEntry[] _levels;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 만렙. 인스펙터에서 바꾸지 못하도록 코드 계약값으로 고정했다 </summary>
        public int MaxLevel => Constants.Combat.MAX_LEVEL;

        public int TalentPointPerLevel => _talentPointPerLevel;

        /// <summary> 만렙까지 얻는 특성 포인트 총합 </summary>
        public int TotalTalentPoints => (MaxLevel - 1) * _talentPointPerLevel;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 레벨 정보를 찾는다, 없으면 false </summary>
        public bool TryGetLevel(int level, out LevelEntry entry)
        {
            for (int i = 0; i < _levels.Length; ++i)
            {
                if (_levels[i].Level == level)
                {
                    entry = _levels[i];
                    return true;
                }
            }

            entry = default;
            return false;
        }

        /// <summary> 만렙의 최대 HP. 점수 정산과 HUD가 참조한다 </summary>
        public float GetMaxLevelHealth()
        {
            LevelEntry entry;
            return TryGetLevel(MaxLevel, out entry) ? entry.MaxHealth : 0f;
        }

        /// <summary> 만렙까지의 누적 필요 경험치. 스폰 표를 고쳤을 때 목표 처치량과 대조하는 용도 </summary>
        public int GetTotalExperienceToMaxLevel()
        {
            int total = 0;
            for (int i = 0; i < _levels.Length; ++i)
            {
                if (_levels[i].Level < MaxLevel)
                {
                    total += _levels[i].RequiredExperience;
                }
            }

            return total;
        }
    }
}
