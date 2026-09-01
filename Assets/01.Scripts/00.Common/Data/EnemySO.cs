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
            public float AttackRange;    // 공격 판정 시작 거리(돌진형은 돌진 커밋 거리)
            public float WindupDuration; // 공격 전 선딜 시간(초)
            public float AttackCooldown; // 공격 후 재공격까지 대기 시간(초)

            // 탐색 모드 전용(스폰 지점 배회), 배회하지 않는 종류는 0으로 둔다
            public float PatrolRadius;       // 스폰 지점 기준 랜덤 이동 반경
            public float PatrolIdleDuration; // 탐색 모드 제자리 대기 시간(초)
            public float PatrolWalkDuration; // 탐색 모드 이동 지속 시간(초)

            // 돌진형 전용, 돌진하지 않는 종류는 0으로 둔다
            public float ChargeSpeed;      // 돌진 중 이동 속도
            public float ChargeDistance;   // 돌진 최대 거리
            public float ChargeHitRadius;  // 돌진 중 이 거리 안으로 들어오면 명중 판정
            public float ParryWindowDuration; // 패링 판정 창 길이(초), 패링 불가 공격은 0

            // 피격 반응 공통
            public float StaggerDuration;   // 비공격 상태에서 일반 피격 시 경직 시간(초)
            public int   HitsToKnockback;   // 이 횟수만큼 일반 피격이 누적되면 넉백
            public float KnockbackDuration; // 넉백(3회 누적 또는 패링 성공) 지속 시간(초)

            // 원거리형 전용, 원거리가 아닌 종류는 0으로 둔다
            public float ProjectileSpeed; // 저주 투사체 비행 속도
            public float RetreatRange;    // 이 거리보다 가까워지면 물러나 사거리를 유지한다
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
