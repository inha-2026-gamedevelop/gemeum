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
    /// 결계 단계별 시간, 반경, 피해량.
    /// 각 파트가 시간을 따로 계산해 자기 상태를 바꾸지 않도록 구간 전환 시각도 여기 하나에 모은다
    /// </summary>
    [CreateAssetMenu(fileName = "BarrierPhaseSO", menuName = "Geumeum/BarrierPhaseSO")]
    public class BarrierPhaseSO : ScriptableObject
    {
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
        }

        /****************************************
        *                Fields
        ****************************************/

        // 필드 구간의 상한이다. 보스전은 여기에 이어 붙지 않고 자기 시계로 14분을 센다.
        // 이 값에 도달하면 실패가 아니라 보스맵으로 강제 이동한다
        [Header("필드 길이")]
        [FormerlySerializedAs("_runTimeLimitSeconds")]
        [SerializeField] private float _fieldTimeLimitSeconds = 1800f; // Shrink3의 EndSeconds와 일치시킬 것

        [Header("반경")]
        // TODO(민성): 맵 레이아웃 확정 후 아트 파트가 실측해 채운다
        [SerializeField] private float _maxRadius = 400f; // RadiusRatio 1.0에 해당하는 반경(m)

        [Header("연출")]
        [SerializeField] private float _shrinkDuration  = 30f; // 목표 반경까지 보간하는 시간(초)
        [SerializeField] private float _warningLeadTime = 10f; // 축소 몇 초 전에 경고를 발행할지

        [Header("피해 적용")]
        [SerializeField] private float _damageTickInterval        = 1f;   // 결계 밖 피해 간격(초)
        [SerializeField] private float _weakenedHealingMultiplier = 0.7f; // 쇠약 상태 회복 효율

        [Header("단계")]
        [SerializeField] private PhaseEntry[] _phases;

        /****************************************
        *              Properties
        ****************************************/

        public float FieldTimeLimitSeconds     => _fieldTimeLimitSeconds;
        public float MaxRadius                 => _maxRadius;
        public float ShrinkDuration            => _shrinkDuration;
        public float WarningLeadTime           => _warningLeadTime;
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
    }
}
