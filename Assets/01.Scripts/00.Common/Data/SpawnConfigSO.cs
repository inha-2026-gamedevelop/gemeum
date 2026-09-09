// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Data
{
    /// <summary>
    /// 구간별 동시 존재 수, 구성 비율, 리스폰 주기.
    /// 스폰이 없는 구간은 항목 자체를 두지 않는다, 결계 데이터와 달리 전 구간을 채우지 않아도 정상이다
    /// </summary>
    [CreateAssetMenu(fileName = "SpawnConfigSO", menuName = "Geumeum/SpawnConfigSO")]
    public class SpawnConfigSO : ScriptableObject
    {
        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        public struct PhaseEntry
        {
            public RunPhase Phase;
            public int      ConcurrentMin;
            public int      ConcurrentMax;
            public float    RespawnInterval; // 0이면 리스폰 없음(초)
            public int      RatioHeokkaebi;
            public int      RatioChanggwi;
            public int      RatioAekgwi;

            // 경험치와 노잣돈 총량의 기준값, 고치면 레벨 곡선과 경제를 다시 계산할 것
            public int      TargetKillMin;
            public int      TargetKillMax;
        }

        /****************************************
        *                Fields
        ****************************************/

        [Header("거점")]
        // 균등 분포는 이동 내내 싸우게 되어 피로하다, 거점형이어야 칠지 넘어갈지 판단이 생긴다
        [SerializeField] private int _clusterSizeMin = 3;
        [SerializeField] private int _clusterSizeMax = 5;

        [Header("축소 몰이")]
        [SerializeField] private float _postShrinkSpawnGraceSeconds       = 60f;
        [SerializeField] private float _herdingConcurrentCapMultiplier    = 1.5f;
        [SerializeField] private bool  _enableShrink3Herding              = false;
        [SerializeField] private int   _herdingPathRequestsPerTick        = 3;
        [SerializeField] private int   _herdingMaxPathFailures            = 5;
        [SerializeField] private float _herdingPathRetrySeconds           = 0.5f;
        [SerializeField] private float _herdingRequestSpreadSeconds       = 0.08f;
        [SerializeField] private float _herdingDestinationInset           = 2f;
        [SerializeField] private float _herdingNavMeshSampleRadius        = 4f;

        [Header("구간")]
        [SerializeField] private PhaseEntry[] _phases;

        /****************************************
        *              Properties
        ****************************************/

        public int ClusterSizeMin => _clusterSizeMin;
        public int ClusterSizeMax => _clusterSizeMax;
        public float PostShrinkSpawnGraceSeconds    => _postShrinkSpawnGraceSeconds;
        public float HerdingConcurrentCapMultiplier => _herdingConcurrentCapMultiplier;
        public bool  EnableShrink3Herding           => _enableShrink3Herding;
        public int   HerdingPathRequestsPerTick     => _herdingPathRequestsPerTick;
        public int   HerdingMaxPathFailures         => _herdingMaxPathFailures;
        public float HerdingPathRetrySeconds        => _herdingPathRetrySeconds;
        public float HerdingRequestSpreadSeconds    => _herdingRequestSpreadSeconds;
        public float HerdingDestinationInset        => _herdingDestinationInset;
        public float HerdingNavMeshSampleRadius     => _herdingNavMeshSampleRadius;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 구간별 스폰 설정을 찾는다.
        /// false는 데이터 오류가 아니라 그 구간에 스폰이 없다는 뜻이다, Shrink3와 Boss가 여기 해당한다
        /// </summary>
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

        /// <summary>생산 스폰과 축소 몰이의 상한, 유예, 경로 재시도 정의를 검증한다.</summary>
        public bool ValidateDefinition(out string failure)
        {
            if ((_clusterSizeMin <= 0) || (_clusterSizeMax < _clusterSizeMin) ||
                !IsFinitePositive(_postShrinkSpawnGraceSeconds) ||
                !IsFiniteAtLeast(_herdingConcurrentCapMultiplier, 1f) ||
                (_herdingPathRequestsPerTick <= 0) ||
                (_herdingMaxPathFailures <= 0) ||
                !IsFinitePositive(_herdingPathRetrySeconds) ||
                !IsFiniteNonNegative(_herdingRequestSpreadSeconds) ||
                !IsFiniteNonNegative(_herdingDestinationInset) ||
                !IsFinitePositive(_herdingNavMeshSampleRadius))
            {
                failure = "스폰 또는 축소 몰이 한계 설정이 유효하지 않다";
                return false;
            }

            RunPhase[] requiredPhases =
            {
                RunPhase.Farming,
                RunPhase.Shrink1,
                RunPhase.Shrink2,
            };

            for (int i = 0; i < requiredPhases.Length; ++i)
            {
                RunPhase phase = requiredPhases[i];
                if (!TryGetUniquePhase(phase, out PhaseEntry entry))
                {
                    failure = $"스폰 필수 구간이 없거나 중복되었다: {phase}";
                    return false;
                }

                long ratioTotal = (long) entry.RatioHeokkaebi +
                    entry.RatioChanggwi + entry.RatioAekgwi;
                if ((entry.ConcurrentMin <= 0) ||
                    (entry.ConcurrentMax < entry.ConcurrentMin) ||
                    !IsFiniteNonNegative(entry.RespawnInterval) ||
                    (entry.RatioHeokkaebi < 0) || (entry.RatioChanggwi < 0) ||
                    (entry.RatioAekgwi < 0) || (ratioTotal <= 0L) ||
                    (entry.TargetKillMin < 0) ||
                    (entry.TargetKillMax < entry.TargetKillMin))
                {
                    failure = $"스폰 구간 값이 유효하지 않다: {phase}";
                    return false;
                }
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

        private static bool IsFiniteAtLeast(float value, float minimum)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) &&
                (value >= minimum);
        }
    }
}
