// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.Data
{
    /// <summary>전투와 아레나 표현의 조정값. 판정 범위와 서버 규칙은 포함하지 않는다.</summary>
    [CreateAssetMenu(fileName = "PresentationConfigSO", menuName = "Geumeum/PresentationConfigSO")]
    public class PresentationConfigSO : ScriptableObject
    {
        [Header("보스 아레나")]
        [SerializeField, Min(0.01f)] private float _arenaPhaseTransitionSeconds = 6f;
        [SerializeField, Range(0f, 1f)] private float _arenaShrineSmoothness = 0.35f;
        [SerializeField, Range(0f, 1f)] private float _arenaUnderworldSmoothness = 0.9f;

        [Header("전투 타격감")]
        [SerializeField, Min(0f)] private float _lightHitStopSeconds = 0.04f;
        [SerializeField, Min(0f)] private float _criticalHitStopSeconds = 0.08f;
        [SerializeField, Min(0f)] private float _lateOneShotToleranceSeconds = 0.15f;
        [SerializeField, Min(0f)] private float _damageFlashSeconds = 0.12f;
        [SerializeField] private Color _damageFlashColor = new Color(1f, 0.12f, 0.08f, 1f);

        [Header("임시 VFX prefab - 최종 아트 교체 가능")]
        [SerializeField] private GameObject _meleeSlashPrefab;
        [SerializeField] private GameObject _bellPulsePrefab;
        [SerializeField] private GameObject _healCastPrefab;
        [SerializeField] private GameObject _parryPrefab;
        [SerializeField] private GameObject _markPrefab;

        public float ArenaPhaseTransitionSeconds => Mathf.Max(0.01f, _arenaPhaseTransitionSeconds);
        public float ArenaShrineSmoothness => Mathf.Clamp01(_arenaShrineSmoothness);
        public float ArenaUnderworldSmoothness => Mathf.Clamp01(_arenaUnderworldSmoothness);
        public float LightHitStopSeconds => Mathf.Max(0f, _lightHitStopSeconds);
        public float CriticalHitStopSeconds => Mathf.Max(0f, _criticalHitStopSeconds);
        public float LateOneShotToleranceSeconds => Mathf.Max(0f, _lateOneShotToleranceSeconds);
        public float DamageFlashSeconds => Mathf.Max(0f, _damageFlashSeconds);
        public Color DamageFlashColor => _damageFlashColor;
        public GameObject MeleeSlashPrefab => _meleeSlashPrefab;
        public GameObject BellPulsePrefab => _bellPulsePrefab;
        public GameObject HealCastPrefab => _healCastPrefab;
        public GameObject ParryPrefab => _parryPrefab;
        public GameObject MarkPrefab => _markPrefab;

        public bool ValidateDefinition(out string failure)
        {
            if (!IsFinitePositive(_arenaPhaseTransitionSeconds))
            {
                failure = "arena phase transition duration must be finite and positive";
                return false;
            }

            if (!IsFiniteUnit(_arenaShrineSmoothness) ||
                !IsFiniteUnit(_arenaUnderworldSmoothness))
            {
                failure = "arena smoothness values must be finite and between 0 and 1";
                return false;
            }

            if (!IsFiniteNonNegative(_lightHitStopSeconds) ||
                !IsFiniteNonNegative(_criticalHitStopSeconds) ||
                !IsFiniteNonNegative(_lateOneShotToleranceSeconds) ||
                !IsFiniteNonNegative(_damageFlashSeconds))
            {
                failure = "presentation durations must be finite and non-negative";
                return false;
            }

            if (!IsFinite(_damageFlashColor.r) || !IsFinite(_damageFlashColor.g) ||
                !IsFinite(_damageFlashColor.b) || !IsFinite(_damageFlashColor.a))
            {
                failure = "damage flash color must be finite";
                return false;
            }

            failure = null;
            return true;
        }

        private static bool IsFinitePositive(float value)
        {
            return IsFinite(value) && (value > 0f);
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return IsFinite(value) && (value >= 0f);
        }

        private static bool IsFiniteUnit(float value)
        {
            return IsFinite(value) && (value >= 0f) && (value <= 1f);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
