// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Data
{
    /// <summary>
    /// 보스 페이즈별 패턴, 쿨다운, 예고 정보와 그로기 게이지, 부위 파괴 게이트.
    /// 페이즈 클래스는 선택과 전환만 하고 수치는 여기서 읽는다
    /// </summary>
    [CreateAssetMenu(fileName = "BossPatternSO", menuName = "Geumeum/BossPatternSO")]
    public class BossPatternSO : ScriptableObject
    {
        /****************************************
        *                Types
        ****************************************/

        /// <summary> 직업 하나가 카운터 한 번에 깎는 그로기 게이지 양 </summary>
        [Serializable]
        public struct GaugeReductionEntry
        {
            public CharacterKind Character;
            public float         Amount;
        }

        [Serializable]
        public struct PatternEntry
        {
            public int    PatternId;
            public string DisplayName;
            public float  TelegraphDuration; // 예고가 뜨고 발동까지(초)
            public float  ActiveDuration;    // 판정이 살아있는 시간(초)
            public float  Cooldown;          // 초
            public float  Damage;
            public bool   IsParryable;       // 페이즈마다 최소 하나는 true여야 차사가 보스전에서 쓸모 있다
        }

        [Serializable]
        public struct PhaseEntry
        {
            public int   PhaseIndex;
            public float EnterHealthRatio; // 보스 HP가 이 비율 이하면 이 페이즈로 전환
            public int[] PatternIds;
        }

        /****************************************
        *                Fields
        ****************************************/

        // TODO(민성): 페이즈 수, 패턴 세부는 AI 파트(@oname36-creator)가 확정 후 채운다
        [Header("보스")]
        [SerializeField] private float _maxHealth = 0f;

        [Header("페이즈")]
        [SerializeField] private PhaseEntry[] _phases;

        [Header("패턴")]
        [SerializeField] private PatternEntry[] _patterns;

        [Header("보스 시계")]
        [Tooltip("보스전 하드 제한(초). 도달하면 실패로 끝난다")]
        [SerializeField] private float _bossTimeLimitSeconds = 840f;

        [Tooltip("광폭화 시작(초). 하드 제한 30초 전이라 마지막 30초가 다른 게임이 된다")]
        [SerializeField] private float _enrageStartSeconds = 810f;

        [Tooltip("광폭화 경고(초). 여우불이 붉게 돈다, 연출만이고 수치 변화는 없다")]
        [SerializeField] private float _enrageWarningSeconds = 720f;

        [Header("그로기 게이지")]
        [Tooltip("게이지 최대치. 셋이 함께 깎아 0에서 그로기가 열린다")]
        [SerializeField] private float _groggyGaugeMax = 100f;

        [Tooltip("직업별 카운터 1회당 감소량. 임시값 - 14분에 그로기 5.5회가 나오게 잡았다")]
        [SerializeField] private GaugeReductionEntry[] _gaugeReductions =
        {
            new GaugeReductionEntry { Character = CharacterKind.Chasa,   Amount = 10f },
            new GaugeReductionEntry { Character = CharacterKind.Singung, Amount = 8f  },
            new GaugeReductionEntry { Character = CharacterKind.Munyeo,  Amount = 13f },
        };

        [Header("그로기 진행")]
        [Tooltip("그로기 총 지속(초). 무저항 + 기상 예고")]
        [SerializeField] private float _groggyDuration = 10f;

        [Tooltip("그로기 끝의 기상 예고(초). 이 구간엔 보스가 꿈틀거리고 맞으면 맞는다 - 빼면 10초가 통째로 허수아비가 된다")]
        [SerializeField] private float _groggyWakeWarningDuration = 2f;

        [Tooltip("그로기 종료 후 재진입 면역(초). 게이지는 최대치로 리셋된다")]
        [SerializeField] private float _groggyImmunityDuration = 8f;

        [Tooltip("무녀 방울 낙인 쿨다운(초). 면역과 같은 값으로 두어 규칙을 하나 줄인다")]
        [SerializeField] private float _brandCooldown = 8f;

        [Tooltip("그로기 중 일반 부위 피해 배수")]
        [SerializeField] private float _groggyBodyDamageMultiplier = 1.5f;

        [Tooltip("그로기 중 꼬리 피해 배수")]
        [SerializeField] private float _groggyTailDamageMultiplier = 3f;

        [Header("꼬리 부위 파괴와 게이트")]
        [Tooltip("꼬리 덩이 수. 메시는 하나이고 사슬 위에 콜라이더를 나눠 붙인다")]
        [SerializeField] private int _tailSegmentCount = 3;

        [Tooltip("꼬리 한 덩이의 HP. 그로기 한 번에 한 덩이가 원래 의도다")]
        [SerializeField] private float _tailSegmentHealth = 1350f;

        [Tooltip("꼬리를 다 부수기 전에는 본체 HP가 이 비율 아래로 내려가지 않는다. 벌이 아니라 시계다")]
        [Range(0f, 1f)]
        [SerializeField] private float _healthGateRatio = 0.25f;

        [Tooltip("표식된 꼬리를 그로기 안에 못 부쉈을 때 보스 공격력 배수. 부술 때까지 유지되고 누적하지 않는다")]
        [SerializeField] private float _unbrokenTailAttackMultiplier = 1.2f;

        [Header("최종 구간 (원혼)")]
        [Tooltip("원혼 한 마리의 HP. 카운터 조건이 아니어도 때려서 없앨 수 있다 - 임시값, 팀 확인 후 조정")]
        [SerializeField] private float _wonhonHealth = 200f;

        [Tooltip("원혼 셋을 없애야 하는 제한시간(초). 못 넘기면 백귀야행이 발동한다 - 임시값")]
        [SerializeField] private float _finalGauntletWindowSeconds = 8f;

        /****************************************
        *              Properties
        ****************************************/

        public float MaxHealth  => _maxHealth;

        /// <summary> 보스전 하드 제한(초) </summary>
        public float BossTimeLimitSeconds => _bossTimeLimitSeconds;

        /// <summary> 광폭화 시작(초) </summary>
        public float EnrageStartSeconds => _enrageStartSeconds;

        /// <summary> 광폭화 경고(초), 연출 전용 </summary>
        public float EnrageWarningSeconds => _enrageWarningSeconds;
        public int   PhaseCount => (_phases == null) ? 0 : _phases.Length;

        public float GroggyGaugeMax => _groggyGaugeMax;

        /// <summary> 그로기 총 지속(초) </summary>
        public float GroggyDuration => _groggyDuration;

        /// <summary> 그로기 끝의 기상 예고(초) </summary>
        public float GroggyWakeWarningDuration => _groggyWakeWarningDuration;

        /// <summary> 저항이 전혀 없는 구간(초) </summary>
        public float GroggyHelplessDuration => Mathf.Max(_groggyDuration - _groggyWakeWarningDuration, 0f);

        public float GroggyImmunityDuration => _groggyImmunityDuration;
        public float BrandCooldown          => _brandCooldown;

        public float GroggyBodyDamageMultiplier => _groggyBodyDamageMultiplier;
        public float GroggyTailDamageMultiplier => _groggyTailDamageMultiplier;

        public int   TailSegmentCount  => _tailSegmentCount;
        public float TailSegmentHealth => _tailSegmentHealth;

        /// <summary> 꼬리를 다 부수기 전 본체 HP 하한 비율 </summary>
        public float HealthGateRatio => _healthGateRatio;

        public float UnbrokenTailAttackMultiplier => _unbrokenTailAttackMultiplier;

        /// <summary> 원혼 한 마리의 HP </summary>
        public float WonhonHealth => _wonhonHealth;

        /// <summary> 원혼 셋을 없애야 하는 제한시간(초), 넘기면 백귀야행 발동 </summary>
        public float FinalGauntletWindowSeconds => _finalGauntletWindowSeconds;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 페이즈 정보를 찾는다, 없으면 false </summary>
        public bool TryGetPhase(int phaseIndex, out PhaseEntry entry)
        {
            if (_phases == null)
            {
                entry = default;
                return false;
            }

            for (int i = 0; i < _phases.Length; ++i)
            {
                if (_phases[i].PhaseIndex == phaseIndex)
                {
                    entry = _phases[i];
                    return true;
                }
            }

            entry = default;
            return false;
        }

        /// <summary> 이 직업이 카운터 한 번에 깎는 게이지 양을 찾는다, 없으면 false </summary>
        public bool TryGetGaugeReduction(CharacterKind character, out float amount)
        {
            if (_gaugeReductions != null)
            {
                for (int i = 0; i < _gaugeReductions.Length; ++i)
                {
                    if (_gaugeReductions[i].Character == character)
                    {
                        amount = _gaugeReductions[i].Amount;
                        return true;
                    }
                }
            }

            amount = 0f;
            return false;
        }

        /// <summary> 패턴 정보를 찾는다, 없으면 false </summary>
        public bool TryGetPattern(int patternId, out PatternEntry entry)
        {
            if (_patterns == null)
            {
                entry = default;
                return false;
            }

            for (int i = 0; i < _patterns.Length; ++i)
            {
                if (_patterns[i].PatternId == patternId)
                {
                    entry = _patterns[i];
                    return true;
                }
            }

            entry = default;
            return false;
        }
    }
}
