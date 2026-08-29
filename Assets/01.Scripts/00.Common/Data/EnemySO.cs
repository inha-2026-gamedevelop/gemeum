// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Data
{
    /// <summary> 잡귀 3종 스탯과 결계 단계별 강화 배수 </summary>
    [CreateAssetMenu(fileName = "EnemySO", menuName = "Geumeum/EnemySO")]
    public class EnemySO : ScriptableObject
    {
        /****************************************
        *                Types
        ****************************************/

        /// <summary> 강화 미적용 기준 스탯 </summary>
        [Serializable]
        public struct StatEntry
        {
            public EnemyKind Kind;
            public string    DisplayName;
            public float     MaxHealth;
            public float     AttackPower;
            public int       Experience;  // 처치 시 파티 전원 획득
            public int       CurrencyMin; // 냥
            public int       CurrencyMax; // 냥

            public float AggroRange;     // 추적 시작 거리
            public float AttackRange;    // 공격 판정 시작 거리
            public float WindupDuration; // 공격 전 선딜 시간(초)
            public float AttackCooldown; // 공격 후 재공격까지 대기 시간(초)
        }

        /// <summary> 필드 레벨별 강화 배수 </summary>
        [Serializable]
        public struct EnhanceEntry
        {
            // 스폰 시점 값으로 확정한다, 교전 중 최대 HP가 변하면 체력바가 어긋난다
            public int    FieldLevel;
            public string PrefixName;
            public float  HealthMultiplier;
            public float  AttackMultiplier;
            public float  RewardMultiplier; // 경험치 · 노잣돈 공통
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private StatEntry[]    _stats    = new StatEntry[Constants.Combat.ENEMY_KIND_COUNT];
        [SerializeField] private EnhanceEntry[] _enhances = new EnhanceEntry[Constants.Combat.FIELD_LEVEL_COUNT];

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 종류별 기본 스탯을 찾는다, 없으면 false </summary>
        public bool TryGetStat(EnemyKind kind, out StatEntry stat)
        {
            for (int i = 0; i < _stats.Length; ++i)
            {
                if (_stats[i].Kind == kind)
                {
                    stat = _stats[i];
                    return true;
                }
            }

            stat = default;
            return false;
        }

        /// <summary> 필드 레벨의 강화 배수를 찾는다, 없으면 false </summary>
        public bool TryGetEnhance(int fieldLevel, out EnhanceEntry enhance)
        {
            for (int i = 0; i < _enhances.Length; ++i)
            {
                if (_enhances[i].FieldLevel == fieldLevel)
                {
                    enhance = _enhances[i];
                    return true;
                }
            }

            enhance = default;
            return false;
        }
    }
}
