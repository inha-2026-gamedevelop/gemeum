// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.Data
{
    /// <summary>전투와 아레나 표현의 조정값. 판정 범위와 서버 규칙은 포함하지 않는다</summary>
    [CreateAssetMenu(fileName = "PresentationConfigSO", menuName = "Geumeum/PresentationConfigSO")]
    public class PresentationConfigSO : ScriptableObject
    {
        [Header("Weapon abilities")]
        [SerializeField] private Material _abilityMaterial;
        [SerializeField] private Color _wedgeColor = new Color(1f, 0.62f, 0.16f, 1f);
        [SerializeField] private Color _echoColor = new Color(0.62f, 0.8f, 0.92f, 1f);
        [SerializeField, Min(0.001f)] private float _abilityLineWidth = 0.025f;
        [SerializeField, Min(0.01f)] private float _abilityPulseSeconds = 0.22f;
        [SerializeField, Min(0.01f)] private float _abilityBurstRadius = 0.65f;
        [SerializeField, Min(0.01f)] private float _echoMarkRadius = 0.22f;
        [SerializeField, Range(0f, 1f)] private float _remoteAbilityOpacity = 0.45f;
        [SerializeField, Min(0f)] private float _bellHitStopSeconds = 0.02f;
        [SerializeField, Min(0f)] private float _bellBurstHitStopSeconds = 0.04f;
        public Material AbilityMaterial => _abilityMaterial;
        public Color WedgeColor => _wedgeColor;
        public Color EchoColor => _echoColor;
        public float AbilityLineWidth => _abilityLineWidth;
        public float AbilityPulseSeconds => _abilityPulseSeconds;
        public float AbilityBurstRadius => _abilityBurstRadius;
        public float EchoMarkRadius => _echoMarkRadius;
        public float RemoteAbilityOpacity => _remoteAbilityOpacity;
        public float BellHitStopSeconds => _bellHitStopSeconds;
        [SerializeField, Min(0f)] private float _swordSkillHitStopSeconds = 0.04f;
        [SerializeField, Min(0f)] private float _bowStabHitStopSeconds = 0.03f;
        public float SwordSkillHitStopSeconds => _swordSkillHitStopSeconds;
        public float BowStabHitStopSeconds => _bowStabHitStopSeconds;
        public float BellBurstHitStopSeconds => _bellBurstHitStopSeconds;

        [Header("Bow and bell motion effects")]
        [SerializeField, Min(.01f)] private float _abilityStreakLength = .85f;
        [SerializeField, Min(.01f)] private float _abilityGlowWidth = .035f;
        [SerializeField, Min(.01f)] private float _bowFocusRadius = .24f;
        [SerializeField, Min(0f)] private float _echoRibbonBend = .35f;
        public float AbilityStreakLength => _abilityStreakLength;
        public float AbilityGlowWidth => _abilityGlowWidth;
        public float BowFocusRadius => _bowFocusRadius;
        public float EchoRibbonBend => _echoRibbonBend;

        [Header("Weapon effect accents")]
        [SerializeField, Range(4, 32)] private int _abilitySparkCount = 18;
        [SerializeField, Min(.1f)] private float _abilityAfterglowSeconds = .38f;
        [SerializeField, Min(.01f)] private float _abilitySparkSize = .055f;
        [SerializeField, Min(.1f)] private float _abilityFlashRadius = .3f;
        [SerializeField, Range(1f, 5f)] private float _abilityRadiance = 2.6f;
        public int AbilitySparkCount => _abilitySparkCount;
        public float AbilityAfterglowSeconds => _abilityAfterglowSeconds;
        public float AbilitySparkSize => _abilitySparkSize;
        public float AbilityFlashRadius => _abilityFlashRadius;
        public float AbilityRadiance => _abilityRadiance;

        [Header("보스 아레나")]
        [SerializeField, Min(0.01f)] private float _arenaPhaseTransitionSeconds = 6f;
        [SerializeField, Range(0f, 1f)] private float _arenaShrineSmoothness = 0.35f;
        [SerializeField, Range(0f, 1f)] private float _arenaUnderworldSmoothness = 0.9f;

        [Header("전투 타격감")]
        [SerializeField, Min(0f)] private float _lightHitStopSeconds = 0.04f;
        [SerializeField, Min(0f)] private float _criticalHitStopSeconds = 0.08f;
        [SerializeField, Min(0f)] private float _lateOneShotToleranceSeconds = 0.15f;
        [SerializeField, Min(0f)] private float _damageFlashSeconds = 0.075f;
        [SerializeField] private Color _damageFlashColor = new Color(1f, 0.12f, 0.08f, 1f);

        [Header("검기 - 실제 검날이 지나간 경로를 잇는다")]
        [Tooltip("궤적 한 점이 생긴 뒤 완전히 사라지기까지의 시간(초). 잔상 길이")]
        [SerializeField, Min(0.05f)] private float _meleeSlashTrailSeconds = 0.22f;
        [Tooltip("판정 시각 몇 초 전부터 검날 위치를 기록할지. 너무 길면 들어 올리는 동작까지 그린다")]
        [SerializeField, Min(0f)] private float _meleeSlashRecordBeforeImpactSeconds = 0.1f;
        [Tooltip("판정 시각 몇 초 뒤까지 기록할지. 너무 길면 검을 거두는 동작까지 그린다")]
        [SerializeField, Min(0f)] private float _meleeSlashRecordAfterImpactSeconds = 0.2f;
        [Tooltip("손잡이에서 칼끝까지 중 궤적을 시작할 비율. 손잡이와 손은 그리지 않는다")]
        [SerializeField, Range(0f, 0.9f)] private float _meleeSlashHiltRatio = 0.25f;
        [SerializeField] private Color _meleeSlashBodyColor = new Color(0.42f, 0.45f, 0.48f, 0.26f);
        [SerializeField, ColorUsage(true, true)] private Color _meleeSlashEdgeColor = new Color(1.8f, 1.68f, 1.42f, 1f);

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
        public float MeleeSlashTrailSeconds => Mathf.Max(0.05f, _meleeSlashTrailSeconds);
        public float MeleeSlashRecordBeforeImpactSeconds => Mathf.Max(0f, _meleeSlashRecordBeforeImpactSeconds);
        public float MeleeSlashRecordAfterImpactSeconds => Mathf.Max(0f, _meleeSlashRecordAfterImpactSeconds);
        public float MeleeSlashHiltRatio => Mathf.Clamp(_meleeSlashHiltRatio, 0f, 0.9f);
        public Color MeleeSlashBodyColor => _meleeSlashBodyColor;
        public Color MeleeSlashEdgeColor => _meleeSlashEdgeColor;
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

            if (!IsFiniteColor(_damageFlashColor))
            {
                failure = "damage flash color must be finite";
                return false;
            }

            if (!IsFinitePositive(_meleeSlashTrailSeconds) ||
                !IsFiniteNonNegative(_meleeSlashRecordBeforeImpactSeconds) ||
                !IsFiniteNonNegative(_meleeSlashRecordAfterImpactSeconds) ||
                !IsFiniteUnit(_meleeSlashHiltRatio))
            {
                failure = "melee slash trail values must be finite and in range";
                return false;
            }

            if (!IsFiniteColor(_meleeSlashBodyColor) || !IsFiniteColor(_meleeSlashEdgeColor))
            {
                failure = "melee slash colors must be finite";
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

        private static bool IsFiniteColor(Color color)
        {
            return IsFinite(color.r) && IsFinite(color.g) && IsFinite(color.b) && IsFinite(color.a);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
