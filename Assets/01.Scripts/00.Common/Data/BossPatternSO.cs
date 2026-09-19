// System
using System;
using System.Collections.Generic;

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

        public enum PatternTemplate
        {
            Unknown      = 0,
            MeleeArc     = 1,
            TargetedDash = 2,
            MarkedCircle = 3,
            ConeBurst    = 4,
        }

        public enum PatternTargeting
        {
            PrimaryTarget = 0,
            AllPlayers    = 1,
        }

        /// <summary> 직업 하나가 카운터 한 번에 깎는 그로기 게이지 양과, 카운터가 되는 패턴 </summary>
        [Serializable]
        public struct GaugeReductionEntry
        {
            public CharacterKind Character;
            public float         Amount;

            /// <summary> 이 직업의 카운터가 성립하는 패턴 id. 비어 있으면 어떤 패턴에서도 카운터가 없다 </summary>
            public int[] CounterPatternIds;
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

            [Header("실행 템플릿")]
            public PatternTemplate  Template;
            public PatternTargeting Targeting;
            public float            Range;
            public float            AngleDegrees;
            public float            MoveSpeed;
            public float            MoveDistance;
            public float            ImpactRadius;

            [Tooltip("공격 시작부터 각 impact까지의 초. 비어 있으면 TelegraphDuration에 단일 impact")]
            public float[] ImpactOffsets;

            [Tooltip("첫 impact는 고정하고 이후 반복 간격에 더하는 서버 난수 하한(초)")]
            public float RepeatTimingJitterMin;

            [Tooltip("첫 impact는 고정하고 이후 반복 간격에 더하는 서버 난수 상한(초)")]
            public float RepeatTimingJitterMax;

            [Tooltip("-1이면 모든 반복, 그 밖에는 해당 RepeatIndex만 패링 가능")]
            public int ParryableRepeatIndex;

            [Tooltip("-1이면 독립 패턴, 값이 있으면 해당 패턴의 파생")]
            public int DerivedFromId;

            [Tooltip("일반 선택기에서 제외하고 상태 전이가 강제 실행하는 패턴")]
            public bool IsConditional;

            [Tooltip("일반 선택 가중치. 1차 이관은 모두 1")]
            public float SelectionWeight;

            public int RepeatCount => ((ImpactOffsets == null) || (ImpactOffsets.Length == 0))
                ? 1 : ImpactOffsets.Length;

            public float GetImpactOffset(int repeatIndex)
            {
                if ((ImpactOffsets == null) || (repeatIndex < 0) ||
                    (repeatIndex >= ImpactOffsets.Length))
                {
                    return TelegraphDuration;
                }

                return ImpactOffsets[repeatIndex];
            }
        }

        [Serializable]
        public struct PhaseEntry
        {
            public int   PhaseIndex;
            public float EnterHealthRatio; // 보스 HP가 이 비율 이하면 이 페이즈로 전환
            public int[] PatternIds;

            [Tooltip("그로기 기상 직후 일반 선택보다 먼저 한 번 실행할 패턴")]
            public int WakePatternId;
        }

        [Serializable]
        public struct PartPatternSealEntry
        {
            public byte PartId;
            public int  PatternId;
        }

        [Serializable]
        public struct CounterOpportunityTransferEntry
        {
            [Tooltip("이 꼬리가 파괴된 뒤 기회 이관을 활성화한다")]
            public byte          TriggerPartId;
            public CharacterKind Character;
            public int           FallbackPatternId;

            [Tooltip("차사 이관 창의 반복 번호. -1이면 모든 반복. 다른 직업에는 사용하지 않는다")]
            public int           RepeatIndex;
        }

        [Serializable]
        public struct SoulCounterEntry
        {
            public byte          SoulId;
            public CharacterKind CounterCharacter;
        }

        /****************************************
        *                Fields
        ****************************************/

        [Header("보스")]
        [SerializeField] private float _maxHealth = 0f;

        [Header("페이즈")]
        [SerializeField] private PhaseEntry[] _phases;

        [Tooltip("페이즈 전환 뒤 보스 공격 잠금 시간(초)")]
        [SerializeField] private float _transitionLockDuration = 2f;

        [Header("패턴")]
        [SerializeField] private PatternEntry[] _patterns;

        [Header("보스 시계")]
        [Tooltip("보스전 하드 제한(초). 도달하면 실패로 끝난다")]
        [SerializeField] private float _bossTimeLimitSeconds = 840f;

        [Tooltip("광폭화 시작(초). 하드 제한 30초 전이라 마지막 30초가 다른 게임이 된다")]
        [SerializeField] private float _enrageStartSeconds = 810f;

        [Tooltip("광폭화 경고(초). 여우불이 붉게 돈다, 연출만이고 수치 변화는 없다")]
        [SerializeField] private float _enrageWarningSeconds = 720f;

        [Header("보스 공격 간격과 광폭화")]
        [Tooltip("1페이즈 정상 전역 공격 간격(초)")]
        [SerializeField] private float _phase1AttackIntervalSeconds = 3f;

        [Tooltip("2페이즈 정상 전역 공격 간격(초)")]
        [SerializeField] private float _phase2AttackIntervalSeconds = 2.2f;

        [Tooltip("2페이즈 파괴 꼬리 수 0/1/2/3에 따른 전역 공격 간격(초)")]
        [SerializeField] private float[] _phase2AttackIntervalsByBrokenPart =
        {
            2.2f, 2.0f, 1.8f, 1.8f,
        };

        [Tooltip("광폭화 전역 공격 간격(초)")]
        [SerializeField] private float _enragedAttackIntervalSeconds = 1.4f;

        [Tooltip("광폭화 공격력 배수")]
        [SerializeField] private float _enrageDamageMultiplier = 3f;

        [Header("그로기 게이지")]
        [Tooltip("게이지 최대치. 셋이 함께 깎아 0에서 그로기가 열린다")]
        [SerializeField] private float _groggyGaugeMax = 100f;

        [Tooltip("직업별 카운터 1회당 감소량과 카운터 패턴. 임시값 - 14분에 그로기 5.5회가 나오게 잡았다")]
        [SerializeField] private GaugeReductionEntry[] _gaugeReductions =
        {
            new GaugeReductionEntry
            {
                Character = CharacterKind.Chasa,   Amount = 10f, CounterPatternIds = new[] { 1, 6 },
            },
            new GaugeReductionEntry
            {
                Character = CharacterKind.Singung, Amount = 8f,  CounterPatternIds = new[] { 5, 9 },
            },
            new GaugeReductionEntry
            {
                Character = CharacterKind.Munyeo,  Amount = 13f, CounterPatternIds = new[] { 4, 8 },
            },
        };

        [Tooltip("무녀 표식 공명 창(초). 카운터 패턴이 끝나는 시각 기준으로 이 안에 걸린 표식만 게이지를 깎는다. 표식 지속(8초)과는 별개다")]
        [SerializeField] private float _brandWindow = 3f;

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

        [Tooltip("고정 꼬리 PartId가 파괴될 때 영구 봉인할 패턴. 백귀야행(10)은 넣지 않는다")]
        [SerializeField] private PartPatternSealEntry[] _partPatternSeals =
        {
            new PartPatternSealEntry { PartId = 1, PatternId = 6 },
            new PartPatternSealEntry { PartId = 2, PatternId = 8 },
            new PartPatternSealEntry { PartId = 3, PatternId = 9 },
        };

        [Tooltip("봉인이 같은 직업 파티의 다음 그로기를 막지 않도록 승인된 카운터 기회 이관")]
        [SerializeField] private CounterOpportunityTransferEntry[] _counterOpportunityTransfers =
        {
            new CounterOpportunityTransferEntry
            {
                TriggerPartId = 1,
                Character = CharacterKind.Chasa,
                FallbackPatternId = 7,
                RepeatIndex = 0,
            },
            new CounterOpportunityTransferEntry
            {
                TriggerPartId = 2,
                Character = CharacterKind.Munyeo,
                FallbackPatternId = 9,
                RepeatIndex = -1,
            },
        };

        [Header("최종 구간 (원혼)")]
        [Tooltip("원혼 한 마리의 HP. 카운터 조건이 아니어도 때려서 없앨 수 있다 - 임시값, 팀 확인 후 조정")]
        [SerializeField] private float _wonhonHealth = 200f;

        [Tooltip("원혼 셋을 없애야 하는 제한시간(초). 못 넘기면 백귀야행이 발동한다 - 임시값")]
        [SerializeField] private float _finalGauntletWindowSeconds = 8f;

        [Tooltip("최종 실패 때 강제 실행할 조건부 패턴 id")]
        [SerializeField] private int _finalFailurePatternId = 10;

        [Tooltip("고정 원혼 id별 빠른 카운터. 일반 피해 처치는 언제나 허용한다")]
        [SerializeField] private SoulCounterEntry[] _soulCounters =
        {
            new SoulCounterEntry { SoulId = 1, CounterCharacter = CharacterKind.Chasa },
            new SoulCounterEntry { SoulId = 2, CounterCharacter = CharacterKind.Singung },
            new SoulCounterEntry { SoulId = 3, CounterCharacter = CharacterKind.Munyeo },
        };

        [Tooltip("마지막 impact 공명에 성공한 적격 무녀 각각의 신력 환급량. 임시값")]
        [SerializeField] private float _resonanceSpiritRefund = 15f;

        [Header("삼켜짐")]
        [SerializeField] private float _swallowDurationSeconds = 10f;
        [SerializeField] private float _swallowRescueChannelSeconds = 2f;
        [Range(0f, 1f)]
        [SerializeField] private float _swallowRestoreHealthRatio = 0.3f;

        /****************************************
        *              Properties
        ****************************************/

        public float MaxHealth  => _maxHealth;
        public float TransitionLockDuration => _transitionLockDuration;
        public IReadOnlyList<PhaseEntry> Phases => _phases;
        public IReadOnlyList<PatternEntry> Patterns => _patterns;
        public IReadOnlyList<GaugeReductionEntry> GaugeReductions => _gaugeReductions;

        /// <summary> 보스전 하드 제한(초) </summary>
        public float BossTimeLimitSeconds => _bossTimeLimitSeconds;

        /// <summary> 광폭화 시작(초) </summary>
        public float EnrageStartSeconds => _enrageStartSeconds;

        /// <summary> 광폭화 경고(초), 연출 전용 </summary>
        public float EnrageWarningSeconds => _enrageWarningSeconds;

        /// <summary> 1페이즈 정상 전역 공격 간격(초) </summary>
        public float Phase1AttackIntervalSeconds => _phase1AttackIntervalSeconds;

        /// <summary> 2페이즈 정상 전역 공격 간격(초) </summary>
        public float Phase2AttackIntervalSeconds => _phase2AttackIntervalSeconds;

        public float GetPhase2AttackInterval(int brokenPartCount)
        {
            if ((_phase2AttackIntervalsByBrokenPart == null) ||
                (_phase2AttackIntervalsByBrokenPart.Length == 0))
            {
                return _phase2AttackIntervalSeconds;
            }

            int index = Mathf.Clamp(brokenPartCount, 0,
                _phase2AttackIntervalsByBrokenPart.Length - 1);
            float value = _phase2AttackIntervalsByBrokenPart[index];
            return ((!float.IsNaN(value)) && (!float.IsInfinity(value)) && (value > 0f))
                ? value
                : _phase2AttackIntervalSeconds;
        }

        /// <summary> 광폭화 전역 공격 간격(초) </summary>
        public float EnragedAttackIntervalSeconds => _enragedAttackIntervalSeconds;

        /// <summary> 광폭화 공격력 배수 </summary>
        public float EnrageDamageMultiplier => _enrageDamageMultiplier;

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

        /// <summary> 무녀 표식 공명이 인정되는 부여 시각 창(초) </summary>
        public float BrandWindow => _brandWindow;

        public float GroggyBodyDamageMultiplier => _groggyBodyDamageMultiplier;
        public float GroggyTailDamageMultiplier => _groggyTailDamageMultiplier;

        public int   TailSegmentCount  => _tailSegmentCount;
        public float TailSegmentHealth => _tailSegmentHealth;

        /// <summary> 꼬리를 다 부수기 전 본체 HP 하한 비율 </summary>
        public float HealthGateRatio => _healthGateRatio;

        public float UnbrokenTailAttackMultiplier => _unbrokenTailAttackMultiplier;

        public IReadOnlyList<PartPatternSealEntry> PartPatternSeals => _partPatternSeals;
        public IReadOnlyList<CounterOpportunityTransferEntry> CounterOpportunityTransfers =>
            _counterOpportunityTransfers;

        /// <summary> 원혼 한 마리의 HP </summary>
        public float WonhonHealth => _wonhonHealth;

        /// <summary> 원혼 셋을 없애야 하는 제한시간(초), 넘기면 백귀야행 발동 </summary>
        public float FinalGauntletWindowSeconds => _finalGauntletWindowSeconds;

        public int FinalFailurePatternId => _finalFailurePatternId;
        public IReadOnlyList<SoulCounterEntry> SoulCounters => _soulCounters;
        public float ResonanceSpiritRefund => _resonanceSpiritRefund;
        public float SwallowDurationSeconds => _swallowDurationSeconds;
        public float SwallowRescueChannelSeconds => _swallowRescueChannelSeconds;
        public float SwallowRestoreHealthRatio => _swallowRestoreHealthRatio;

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

        /// <summary> 이 패턴이 이 직업의 카운터 패턴인가 </summary>
        public bool IsCounterPattern(CharacterKind character, int patternId)
        {
            if (_gaugeReductions == null)
            {
                return false;
            }

            for (int i = 0; i < _gaugeReductions.Length; ++i)
            {
                if (_gaugeReductions[i].Character != character)
                {
                    continue;
                }

                int[] patternIds = _gaugeReductions[i].CounterPatternIds;
                if (patternIds == null)
                {
                    return false;
                }

                for (int j = 0; j < patternIds.Length; ++j)
                {
                    if (patternIds[j] == patternId)
                    {
                        return true;
                    }
                }

                return false;
            }

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

        public bool TryGetSealedPattern(byte partId, out int patternId)
        {
            if (_partPatternSeals != null)
            {
                for (int i = 0; i < _partPatternSeals.Length; ++i)
                {
                    if (_partPatternSeals[i].PartId == partId)
                    {
                        patternId = _partPatternSeals[i].PatternId;
                        return patternId > 0;
                    }
                }
            }

            patternId = 0;
            return false;
        }

        public bool TryGetSoulCounter(byte soulId, out CharacterKind character)
        {
            if (_soulCounters != null)
            {
                for (int i = 0; i < _soulCounters.Length; ++i)
                {
                    if (_soulCounters[i].SoulId == soulId)
                    {
                        character = _soulCounters[i].CounterCharacter;
                        return true;
                    }
                }
            }

            character = default;
            return false;
        }

        public bool TryGetCounterOpportunityTransfer(byte triggerPartId,
            CharacterKind character, out CounterOpportunityTransferEntry entry)
        {
            if (_counterOpportunityTransfers != null)
            {
                for (int i = 0; i < _counterOpportunityTransfers.Length; ++i)
                {
                    CounterOpportunityTransferEntry candidate = _counterOpportunityTransfers[i];
                    if ((candidate.TriggerPartId == triggerPartId) &&
                        (candidate.Character == character) &&
                        (candidate.FallbackPatternId > 0))
                    {
                        entry = candidate;
                        return true;
                    }
                }
            }

            entry = default;
            return false;
        }

        /// <summary>
        /// Validates production-only boss invariants that cannot be inferred by the generic boot checks.
        /// </summary>
        public bool ValidateAdvancedDefinition(out string failure)
        {
            const int maxTelegraphCues = 8;
            failure = string.Empty;

            if ((_patterns == null) || (_patterns.Length == 0))
            {
                return FailValidation(out failure, "patterns are empty");
            }

            var patternIds = new HashSet<int>();
            for (int i = 0; i < _patterns.Length; ++i)
            {
                PatternEntry pattern = _patterns[i];
                if ((pattern.PatternId <= 0) || (!patternIds.Add(pattern.PatternId)))
                {
                    return FailValidation(out failure, $"pattern id {pattern.PatternId} is invalid or duplicated");
                }

                if ((pattern.Template == PatternTemplate.Unknown) ||
                    (!Enum.IsDefined(typeof(PatternTemplate), pattern.Template)) ||
                    (!Enum.IsDefined(typeof(PatternTargeting), pattern.Targeting)))
                {
                    return FailValidation(out failure, $"pattern {pattern.PatternId} has no valid execution template");
                }

                if ((!PositiveFinite(pattern.TelegraphDuration)) ||
                    (!NonNegativeFinite(pattern.ActiveDuration)) ||
                    (!NonNegativeFinite(pattern.Cooldown)) ||
                    (!PositiveFinite(pattern.Damage)) ||
                    (!NonNegativeFinite(pattern.Range)) ||
                    (!NonNegativeFinite(pattern.AngleDegrees)) ||
                    (!NonNegativeFinite(pattern.MoveSpeed)) ||
                    (!NonNegativeFinite(pattern.MoveDistance)) ||
                    (!NonNegativeFinite(pattern.ImpactRadius)) ||
                    (!NonNegativeFinite(pattern.SelectionWeight)) ||
                    (!Finite(pattern.RepeatTimingJitterMin)) ||
                    (!Finite(pattern.RepeatTimingJitterMax)) ||
                    (pattern.RepeatTimingJitterMin > pattern.RepeatTimingJitterMax))
                {
                    return FailValidation(out failure, $"pattern {pattern.PatternId} has an invalid timing, damage, or shape value");
                }

                if ((pattern.ImpactOffsets == null) || (pattern.ImpactOffsets.Length == 0) ||
                    (pattern.ImpactOffsets.Length > maxTelegraphCues))
                {
                    return FailValidation(out failure,
                        $"pattern {pattern.PatternId} impact count must be 1..{maxTelegraphCues}");
                }

                float previous = 0f;
                for (int repeat = 0; repeat < pattern.ImpactOffsets.Length; ++repeat)
                {
                    float offset = pattern.ImpactOffsets[repeat];
                    if ((!PositiveFinite(offset)) || ((repeat > 0) && (offset <= previous)))
                    {
                        return FailValidation(out failure,
                            $"pattern {pattern.PatternId} impact offsets must be finite and strictly increasing");
                    }

                    previous = offset;
                }

                if (!Mathf.Approximately(pattern.ImpactOffsets[0], pattern.TelegraphDuration))
                {
                    return FailValidation(out failure,
                        $"pattern {pattern.PatternId} first impact must equal TelegraphDuration");
                }

                if ((pattern.ParryableRepeatIndex < -1) ||
                    (pattern.ParryableRepeatIndex >= pattern.ImpactOffsets.Length))
                {
                    return FailValidation(out failure,
                        $"pattern {pattern.PatternId} ParryableRepeatIndex is outside its impact range");
                }

                if ((!pattern.IsConditional) && (!PositiveFinite(pattern.SelectionWeight)))
                {
                    return FailValidation(out failure,
                        $"selectable pattern {pattern.PatternId} needs a positive SelectionWeight");
                }

                if ((pattern.Template == PatternTemplate.MeleeArc) &&
                    ((!PositiveFinite(pattern.Range)) || (pattern.AngleDegrees <= 0f) ||
                        (pattern.AngleDegrees > 360f)))
                {
                    return FailValidation(out failure, $"melee pattern {pattern.PatternId} has an invalid arc");
                }

                if ((pattern.Template == PatternTemplate.TargetedDash) &&
                    ((!PositiveFinite(pattern.Range)) || (!PositiveFinite(pattern.MoveSpeed)) ||
                        (!PositiveFinite(pattern.MoveDistance)) || (!PositiveFinite(pattern.ImpactRadius))))
                {
                    return FailValidation(out failure, $"dash pattern {pattern.PatternId} has an invalid movement shape");
                }

                if ((pattern.Template == PatternTemplate.MarkedCircle) &&
                    (!PositiveFinite(pattern.ImpactRadius)))
                {
                    return FailValidation(out failure, $"circle pattern {pattern.PatternId} has no radius");
                }

                if ((pattern.Template == PatternTemplate.ConeBurst) &&
                    ((!PositiveFinite(pattern.Range)) || (pattern.AngleDegrees <= 0f) ||
                        (pattern.AngleDegrees > 360f)))
                {
                    return FailValidation(out failure, $"cone pattern {pattern.PatternId} has an invalid shape");
                }
            }

            for (int i = 0; i < _patterns.Length; ++i)
            {
                int parentId = _patterns[i].DerivedFromId;
                if ((parentId > 0) && (!patternIds.Contains(parentId)))
                {
                    return FailValidation(out failure,
                        $"pattern {_patterns[i].PatternId} derives from missing pattern {parentId}");
                }
            }

            if ((_patterns.Length != 10) ||
                (!TryGetPattern(1, out PatternEntry teachingCombo)) ||
                (!TryGetPattern(2, out PatternEntry derivedWake)) ||
                (!TryGetPattern(3, out PatternEntry teachingDash)) ||
                (!TryGetPattern(4, out PatternEntry teachingMark)) ||
                (!TryGetPattern(5, out PatternEntry teachingCone)) ||
                (!TryGetPattern(6, out PatternEntry testingCombo)) ||
                (!TryGetPattern(7, out PatternEntry testingDash)) ||
                (!TryGetPattern(8, out PatternEntry partyMarks)) ||
                (!TryGetPattern(9, out PatternEntry testingCone)) ||
                (!TryGetPattern(10, out PatternEntry finalCone)))
            {
                return FailValidation(out failure, "the boss contract requires pattern ids 1..10 exactly");
            }

            if ((teachingCombo.RepeatCount != 3) || (!teachingCombo.IsParryable) ||
                (teachingCombo.ParryableRepeatIndex != 2) ||
                (teachingCombo.RepeatTimingJitterMin < 0f) ||
                (teachingCombo.RepeatTimingJitterMax <= teachingCombo.RepeatTimingJitterMin))
            {
                return FailValidation(out failure,
                    "pattern 1 needs three impacts, last-hit parry, and positive-only timing variation");
            }

            if ((derivedWake.DerivedFromId != 1) ||
                (derivedWake.RepeatCount != 1) ||
                (!Mathf.Approximately(derivedWake.TelegraphDuration, 0.8f)))
            {
                return FailValidation(out failure,
                    "pattern 2 must remain the 0.8-second derivative of pattern 1");
            }

            if ((testingCombo.RepeatCount != 3) || (!testingCombo.IsParryable) ||
                (testingCombo.ParryableRepeatIndex != 2) ||
                (testingCombo.RepeatTimingJitterMin >= 0f) ||
                (testingCombo.RepeatTimingJitterMax <= 0f) ||
                (partyMarks.RepeatCount != 3) ||
                (partyMarks.Targeting != PatternTargeting.AllPlayers) ||
                (testingCone.RepeatCount != 3) ||
                (testingCone.RepeatTimingJitterMin >= 0f) ||
                (testingCone.RepeatTimingJitterMax <= 0f))
            {
                return FailValidation(out failure,
                    "patterns 6/8/9 need their three-impact test cadence and party targeting");
            }

            if ((teachingCombo.Targeting != PatternTargeting.PrimaryTarget) ||
                (derivedWake.Targeting != PatternTargeting.PrimaryTarget) ||
                (teachingDash.Targeting != PatternTargeting.PrimaryTarget) ||
                (teachingMark.Targeting != PatternTargeting.PrimaryTarget) ||
                (teachingCone.Targeting != PatternTargeting.PrimaryTarget) ||
                (testingCombo.Targeting != PatternTargeting.PrimaryTarget) ||
                (testingDash.Targeting != PatternTargeting.PrimaryTarget) ||
                (testingCone.Targeting != PatternTargeting.PrimaryTarget) ||
                (finalCone.Targeting != PatternTargeting.AllPlayers))
            {
                return FailValidation(out failure, "a pattern targeting scope differs from the 1..10 contract");
            }

            if ((_phases == null) || (_phases.Length == 0))
            {
                return FailValidation(out failure, "phases are empty");
            }

            var phaseIndices = new HashSet<int>();
            var phase2PatternIds = new HashSet<int>();
            var normalPatternIds = new HashSet<int>();
            var wakePatternIds = new HashSet<int>();
            bool foundPhase2 = false;
            for (int i = 0; i < _phases.Length; ++i)
            {
                PhaseEntry phase = _phases[i];
                if ((phase.PhaseIndex <= 0) || (!phaseIndices.Add(phase.PhaseIndex)) ||
                    (!Finite(phase.EnterHealthRatio)) || (phase.EnterHealthRatio < 0f) ||
                    (phase.EnterHealthRatio > 1f) || (phase.PatternIds == null) ||
                    (phase.PatternIds.Length == 0))
                {
                    return FailValidation(out failure, $"phase {phase.PhaseIndex} is invalid");
                }

                var phasePatterns = new HashSet<int>();
                for (int j = 0; j < phase.PatternIds.Length; ++j)
                {
                    int patternId = phase.PatternIds[j];
                    if ((!patternIds.Contains(patternId)) || (!phasePatterns.Add(patternId)))
                    {
                        return FailValidation(out failure,
                            $"phase {phase.PhaseIndex} references a missing or duplicate pattern {patternId}");
                    }

                    normalPatternIds.Add(patternId);
                    if (phase.PhaseIndex == 2)
                    {
                        phase2PatternIds.Add(patternId);
                    }
                }

                if ((phase.WakePatternId <= 0) || (!patternIds.Contains(phase.WakePatternId)))
                {
                    return FailValidation(out failure,
                        $"phase {phase.PhaseIndex} has no valid wake pattern");
                }


                if ((!TryGetPattern(phase.WakePatternId, out PatternEntry wakePattern)) ||
                    (!Mathf.Approximately(wakePattern.TelegraphDuration, 0.8f)))
                {
                    return FailValidation(out failure,
                        $"phase {phase.PhaseIndex} wake pattern must keep the fixed 0.8-second windup");
                }

                wakePatternIds.Add(phase.WakePatternId);

                foundPhase2 |= phase.PhaseIndex == 2;
            }

            if ((!foundPhase2) || (phase2PatternIds.Count == 0))
            {
                return FailValidation(out failure, "phase 2 has no selectable pattern set");
            }

            if ((_phase2AttackIntervalsByBrokenPart == null) ||
                (_phase2AttackIntervalsByBrokenPart.Length != 4))
            {
                return FailValidation(out failure,
                    "phase 2 cadence needs values for 0, 1, 2, and 3 broken tails");
            }

            float previousCadence = float.MaxValue;
            for (int i = 0; i < _phase2AttackIntervalsByBrokenPart.Length; ++i)
            {
                float cadence = _phase2AttackIntervalsByBrokenPart[i];
                if ((!PositiveFinite(cadence)) || (cadence > previousCadence))
                {
                    return FailValidation(out failure,
                        "phase 2 cadence must stay equal or become faster as tails break");
                }

                previousCadence = cadence;
            }

            var sealedParts = new HashSet<byte>();
            var sealedPatterns = new HashSet<int>();
            if ((_partPatternSeals == null) || (_partPatternSeals.Length != 3))
            {
                return FailValidation(out failure, "exactly three fixed tail seals are required");
            }

            for (int i = 0; i < _partPatternSeals.Length; ++i)
            {
                PartPatternSealEntry seal = _partPatternSeals[i];
                if ((seal.PartId < BossTargetPartIds.Tail1) ||
                    (seal.PartId > BossTargetPartIds.Tail3) || (!sealedParts.Add(seal.PartId)) ||
                    (!phase2PatternIds.Contains(seal.PatternId)) ||
                    (!sealedPatterns.Add(seal.PatternId)) ||
                    (seal.PatternId == _finalFailurePatternId))
                {
                    return FailValidation(out failure,
                        $"tail seal {seal.PartId}:{seal.PatternId} is invalid");
                }
            }

            for (byte partId = BossTargetPartIds.Tail1; partId <= BossTargetPartIds.Tail3; ++partId)
            {
                if (!sealedParts.Contains(partId))
                {
                    return FailValidation(out failure, $"tail PartId {partId} has no seal");
                }
            }


            foreach (int wakePatternId in wakePatternIds)
            {
                if (sealedPatterns.Contains(wakePatternId))
                {
                    return FailValidation(out failure,
                        $"wake pattern {wakePatternId} is also sealed by a tail");
                }
            }

            var gaugeCharacters = new HashSet<CharacterKind>();
            if ((_gaugeReductions == null) || (_gaugeReductions.Length != 3))
            {
                return FailValidation(out failure, "all three character gauge mappings are required");
            }

            for (int i = 0; i < _gaugeReductions.Length; ++i)
            {
                GaugeReductionEntry reduction = _gaugeReductions[i];
                if ((!Enum.IsDefined(typeof(CharacterKind), reduction.Character)) ||
                    (!gaugeCharacters.Add(reduction.Character)) || (!PositiveFinite(reduction.Amount)) ||
                    (reduction.CounterPatternIds == null) || (reduction.CounterPatternIds.Length == 0))
                {
                    return FailValidation(out failure, "a gauge counter mapping is invalid or duplicated");
                }

                for (int j = 0; j < reduction.CounterPatternIds.Length; ++j)
                {
                    if (!patternIds.Contains(reduction.CounterPatternIds[j]))
                    {
                        return FailValidation(out failure,
                            $"{reduction.Character} counter references missing pattern {reduction.CounterPatternIds[j]}");
                    }
                }
            }

            var transferKeys = new HashSet<int>();
            if (_counterOpportunityTransfers != null)
            {
                for (int i = 0; i < _counterOpportunityTransfers.Length; ++i)
                {
                    CounterOpportunityTransferEntry transfer = _counterOpportunityTransfers[i];
                    int key = (transfer.TriggerPartId << 8) | (int) transfer.Character;
                    if ((!transferKeys.Add(key)) || (!sealedParts.Contains(transfer.TriggerPartId)) ||
                        (!Enum.IsDefined(typeof(CharacterKind), transfer.Character)) ||
                        (!phase2PatternIds.Contains(transfer.FallbackPatternId)) ||
                        IsPatternSealedThrough(transfer.FallbackPatternId, transfer.TriggerPartId))
                    {
                        return FailValidation(out failure,
                            $"counter transfer for tail {transfer.TriggerPartId}/{transfer.Character} is invalid");
                    }

                    if ((!TryGetSealedPattern(transfer.TriggerPartId, out int originalPattern)) ||
                        (!IsCounterPattern(transfer.Character, originalPattern)) ||
                        (!TryGetPattern(transfer.FallbackPatternId, out PatternEntry fallback)) ||
                        (transfer.RepeatIndex < -1) ||
                        (transfer.RepeatIndex >= fallback.RepeatCount))
                    {
                        return FailValidation(out failure,
                            $"counter transfer for tail {transfer.TriggerPartId}/{transfer.Character} cannot be consumed");
                    }
                }
            }

            CharacterKind[] characters =
            {
                CharacterKind.Chasa, CharacterKind.Singung, CharacterKind.Munyeo,
            };
            for (int stage = 0; stage <= 2; ++stage)
            {
                for (int i = 0; i < characters.Length; ++i)
                {
                    if (!HasPhase2CounterPath(characters[i], stage, phase2PatternIds))
                    {
                        return FailValidation(out failure,
                            $"{characters[i]} has no phase 2 counter path after {stage} broken tails");
                    }
                }
            }

            if ((!TryGetPattern(_finalFailurePatternId, out PatternEntry failurePattern)) ||
                (!failurePattern.IsConditional) || normalPatternIds.Contains(_finalFailurePatternId))
            {
                return FailValidation(out failure,
                    "final failure pattern must exist, be conditional, and stay outside normal phase selection");
            }

            var soulIds = new HashSet<byte>();
            var soulCharacters = new HashSet<CharacterKind>();
            if ((_soulCounters == null) || (_soulCounters.Length != 3))
            {
                return FailValidation(out failure, "exactly three soul counter mappings are required");
            }

            for (int i = 0; i < _soulCounters.Length; ++i)
            {
                SoulCounterEntry soul = _soulCounters[i];
                if ((soul.SoulId < 1) || (soul.SoulId > 3) || (!soulIds.Add(soul.SoulId)) ||
                    (!Enum.IsDefined(typeof(CharacterKind), soul.CounterCharacter)) ||
                    (!soulCharacters.Add(soul.CounterCharacter)))
                {
                    return FailValidation(out failure, "soul ids and character counters must be unique 1..3");
                }
            }

            if ((_tailSegmentCount != 3) || (!PositiveFinite(_tailSegmentHealth)) ||
                (!PositiveFinite(_wonhonHealth)) || (!PositiveFinite(_finalGauntletWindowSeconds)) ||
                (!PositiveFinite(_resonanceSpiritRefund)) || (!PositiveFinite(_swallowDurationSeconds)) ||
                (!PositiveFinite(_swallowRescueChannelSeconds)) ||
                (!Finite(_swallowRestoreHealthRatio)) || (_swallowRestoreHealthRatio <= 0f) ||
                (_swallowRestoreHealthRatio > 1f) || (!Finite(_healthGateRatio)) ||
                (_healthGateRatio <= 0f) || (_healthGateRatio >= 1f))
            {
                return FailValidation(out failure, "tail, soul, refund, swallow, or health-gate values are invalid");
            }

            return true;
        }

        private bool HasPhase2CounterPath(CharacterKind character, int brokenTailCount,
            HashSet<int> phase2PatternIds)
        {
            foreach (int patternId in phase2PatternIds)
            {
                if (IsPatternSealedThrough(patternId, brokenTailCount))
                {
                    continue;
                }

                if (IsCounterPattern(character, patternId))
                {
                    return true;
                }

                if (_counterOpportunityTransfers == null)
                {
                    continue;
                }

                for (int i = 0; i < _counterOpportunityTransfers.Length; ++i)
                {
                    CounterOpportunityTransferEntry transfer = _counterOpportunityTransfers[i];
                    if ((transfer.Character == character) &&
                        (transfer.TriggerPartId <= brokenTailCount) &&
                        (transfer.FallbackPatternId == patternId))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsPatternSealedThrough(int patternId, int brokenTailCount)
        {
            if (_partPatternSeals == null)
            {
                return false;
            }

            for (int i = 0; i < _partPatternSeals.Length; ++i)
            {
                PartPatternSealEntry seal = _partPatternSeals[i];
                if ((seal.PartId <= brokenTailCount) && (seal.PatternId == patternId))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Finite(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }

        private static bool PositiveFinite(float value)
        {
            return Finite(value) && (value > 0f);
        }

        private static bool NonNegativeFinite(float value)
        {
            return Finite(value) && (value >= 0f);
        }

        private static bool FailValidation(out string failure, string message)
        {
            failure = message;
            return false;
        }
    }
}
