// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.Serialization;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Data
{
    /// <summary>
    /// 결계 단계별 시간, 반경, 피해량
    /// 각 파트가 시간을 따로 계산해 자기 상태를 바꾸지 않도록 구간 전환 시각도 여기 하나에 모은다
    /// </summary>
    [CreateAssetMenu(fileName = "BarrierPhaseSO", menuName = "Geumeum/BarrierPhaseSO")]
    public class BarrierPhaseSO : ScriptableObject
    {
        public const BarrierLandmarkKind ProductionLandmarks =
            BarrierLandmarkKind.Start |
            BarrierLandmarkKind.Shop |
            BarrierLandmarkKind.BossEntrance;

        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        public struct PhaseEntry
        {
            public RunPhase Phase;

            // 필드 시계 기준이다. Boss 항목의 두 값은 쓰이지 않는다 -
            // 보스는 진입 시각과 무관한 독립 시계이고, 이 항목은 FieldLevel 조회용으로만 남아 있다
            public float    StartSeconds;    // 런 시작 기준 경과 시간(초)
            public float    EndSeconds;      // 이 시각에 다음 단계로 전환
            public int      FieldLevel;      // 드랍 가중치, 강화 배수의 키
            public float    RadiusRatio;     // 최대 반경 대비 목표 반경
            public float    DamagePerSecond; // 결계 밖 지속 피해
            public bool     AppliesWeakened; // 쇠약, 회복 효율 감소
            public bool     BlocksHealing;   // 회복 봉쇄, 모든 회복 무효
            public BarrierLandmarkKind RequiredLandmarks; // 목표 원 안에 남길 필드 기준점
        }

        /****************************************
        *                Fields
        ****************************************/

        // 필드 구간의 상한이다. 보스전은 여기에 이어 붙지 않고 자기 시계로 14분을 센다
        // 이 값에 도달하면 실패가 아니라 보스맵으로 강제 이동한다
        [Header("필드 길이")]
        [FormerlySerializedAs("_runTimeLimitSeconds")]
        [SerializeField] private float _fieldTimeLimitSeconds = 1800f; // Shrink3의 EndSeconds와 일치시킬 것

        [SerializeField] private float _bossEntryCountdownSeconds = 30f;

        [Header("반경")]
        // TODO(민성): 맵 레이아웃 확정 후 아트 파트가 실측해 채운다
        [SerializeField] private float _maxRadius = 400f; // RadiusRatio 1.0에 해당하는 반경(m)

        [Header("연출")]
        [SerializeField] private float _shrinkDuration  = 30f; // 목표 반경까지 보간하는 시간(초)
        [SerializeField] private float _warningLeadTime = 10f; // 축소 몇 초 전에 경고를 발행할지

        [Header("목표 원 탐색")]
        [SerializeField] private int   _targetCandidateAttempts = 24; // 무작위 후보 검사 상한
        [SerializeField] private int   _fallbackSegmentSamples  = 24; // 이전 중심과 랜드마크 중심 사이 대체 후보 수
        [SerializeField] private float _navMeshSampleRadius     = 4f; // 중심과 랜드마크의 NavMesh 투영 허용 거리(m)

        [Header("피해 적용")]
        [SerializeField] private float _damageTickInterval        = 1f;   // 결계 밖 피해 간격(초)
        [SerializeField] private float _weakenedHealingMultiplier = 0.7f; // 쇠약 상태 회복 효율

        [Header("단계")]
        [SerializeField] private PhaseEntry[] _phases;

        /****************************************
        *              Properties
        ****************************************/

        public float FieldTimeLimitSeconds     => _fieldTimeLimitSeconds;
        public float BossEntryCountdownSeconds => _bossEntryCountdownSeconds;
        public float MaxRadius                 => _maxRadius;
        public float ShrinkDuration            => _shrinkDuration;
        public float WarningLeadTime           => _warningLeadTime;
        public int   TargetCandidateAttempts   => _targetCandidateAttempts;
        public int   FallbackSegmentSamples    => _fallbackSegmentSamples;
        public float NavMeshSampleRadius       => _navMeshSampleRadius;
        public float DamageTickInterval        => _damageTickInterval;
        public float WeakenedHealingMultiplier => _weakenedHealingMultiplier;

        /// <summary> 정의된 순서 그대로의 단계 목록. 서버가 부트 시 구간 연속성을 검사할 때 읽는다 </summary>
        public IReadOnlyList<PhaseEntry> Phases => _phases;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 단계 정보를 찾는다, 없으면 false </summary>
        public bool TryGetPhase(RunPhase phase, out PhaseEntry entry)
        {
            if (_phases == null)
            {
                entry = default;
                return false;
            }

            for (int i = 0; i < _phases.Length; ++i)
            {
                if (_phases[i].Phase == phase)
                {
                    entry = _phases[i];
                    return true;
                }
            }

            entry = default;
            return false;
        }

        /// <summary> 경과 시간이 속한 단계를 찾는다, 어느 구간에도 없으면 false </summary>
        public bool TryGetPhaseAt(float elapsedSeconds, out PhaseEntry entry)
        {
            if (_phases == null)
            {
                entry = default;
                return false;
            }

            for (int i = 0; i < _phases.Length; ++i)
            {
                if ((elapsedSeconds >= _phases[i].StartSeconds) && (elapsedSeconds < _phases[i].EndSeconds))
                {
                    entry = _phases[i];
                    return true;
                }
            }

            entry = default;
            return false;
        }

        /// <summary>생산 필드 결계의 후보 탐색 한계와 필수 랜드마크 정의를 검증한다</summary>
        public bool ValidateDefinition(out string failure)
        {
            if (!IsFinitePositive(_fieldTimeLimitSeconds) ||
                !IsFinitePositive(_bossEntryCountdownSeconds) ||
                (_bossEntryCountdownSeconds > _fieldTimeLimitSeconds) ||
                !IsFinitePositive(_maxRadius) ||
                !IsFiniteNonNegative(_shrinkDuration) ||
                !IsFiniteNonNegative(_warningLeadTime) ||
                (_targetCandidateAttempts <= 0) || (_fallbackSegmentSamples <= 0) ||
                !IsFinitePositive(_navMeshSampleRadius) ||
                !IsFinitePositive(_damageTickInterval) ||
                !IsFiniteRange(_weakenedHealingMultiplier, 0f, 1f))
            {
                failure = "결계 시간, 후보 한계, NavMesh 또는 피해 설정이 유효하지 않다";
                return false;
            }

            RunPhase[] requiredPhases =
            {
                RunPhase.Farming,
                RunPhase.Shrink1,
                RunPhase.Shrink2,
                RunPhase.Shrink3,
            };

            float previousRadius = float.PositiveInfinity;
            for (int i = 0; i < requiredPhases.Length; ++i)
            {
                RunPhase phase = requiredPhases[i];
                if (!TryGetUniquePhase(phase, out PhaseEntry entry))
                {
                    failure = $"결계 필수 구간이 없거나 중복되었다: {phase}";
                    return false;
                }

                if (!IsFiniteNonNegative(entry.StartSeconds) ||
                    !IsFiniteNonNegative(entry.EndSeconds) ||
                    (entry.EndSeconds <= entry.StartSeconds) ||
                    !IsFiniteRange(entry.RadiusRatio, float.Epsilon, 1f) ||
                    (entry.RadiusRatio > previousRadius) ||
                    !IsFiniteNonNegative(entry.DamagePerSecond))
                {
                    failure = $"결계 구간 값이 유효하지 않다: {phase}";
                    return false;
                }

                if ((entry.RequiredLandmarks & ProductionLandmarks) != ProductionLandmarks ||
                    (entry.RequiredLandmarks & ~ProductionLandmarks) != 0)
                {
                    failure = $"결계 구간의 생산 필수 랜드마크가 완전하지 않다: {phase}";
                    return false;
                }

                previousRadius = entry.RadiusRatio;
            }

            failure = null;
            return true;
        }

        private bool TryGetUniquePhase(RunPhase phase, out PhaseEntry entry)
        {
            entry = default;
            bool found = false;
            if (_phases == null)
            {
                return false;
            }

            for (int i = 0; i < _phases.Length; ++i)
            {
                if (_phases[i].Phase != phase)
                {
                    continue;
                }

                if (found)
                {
                    return false;
                }

                found = true;
                entry = _phases[i];
            }

            return found;
        }

        private static bool IsFinitePositive(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && (value > 0f);
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && (value >= 0f);
        }

        private static bool IsFiniteRange(float value, float minimum, float maximum)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) &&
                (value >= minimum) && (value <= maximum);
        }
    }
}
