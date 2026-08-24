// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.Data
{
    /// <summary> 보스 페이즈별 패턴 · 쿨다운 · 예고 정보. 페이즈 클래스는 선택과 전환만 하고 수치는 여기서 읽는다 </summary>
    [CreateAssetMenu(fileName = "BossPatternSO", menuName = "Geumeum/BossPatternSO")]
    public class BossPatternSO : ScriptableObject
    {
        /****************************************
        *                Types
        ****************************************/

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

        // TODO(민성): 보스 HP · 페이즈 수 · 패턴 세부는 전부 미정, AI 파트가 확정 후 채운다
        [Header("보스")]
        [SerializeField] private float _maxHealth = 0f;

        [Header("페이즈")]
        [SerializeField] private PhaseEntry[] _phases;

        [Header("패턴")]
        [SerializeField] private PatternEntry[] _patterns;

        /****************************************
        *              Properties
        ****************************************/

        public float MaxHealth  => _maxHealth;
        public int   PhaseCount => (_phases == null) ? 0 : _phases.Length;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 페이즈 정보를 찾는다, 없으면 false </summary>
        public bool TryGetPhase(int phaseIndex, out PhaseEntry entry)
        {
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

        /// <summary> 패턴 정보를 찾는다, 없으면 false </summary>
        public bool TryGetPattern(int patternId, out PatternEntry entry)
        {
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
