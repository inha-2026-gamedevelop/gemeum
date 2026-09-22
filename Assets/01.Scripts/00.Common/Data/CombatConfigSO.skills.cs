using UnityEngine;
using Geumeum.Common;

namespace Geumeum.Data
{
    public partial class CombatConfigSO
    {
        [System.Serializable]
        public struct WeaponSkillSettings
        {
            [Min(0f)] public float Cost;
            [Min(0f)] public float Cooldown;
            [Min(0.01f)] public float Startup;
            [Min(0.01f)] public float Recovery;
            [Min(0.01f)] public float Range;
            [Range(1f, 360f)] public float Angle;
            [Min(0f)] public float DamageMultiplier;
        }

        [Header("무기 R: 끊어베기 / 파고들기 / 공명진")]
        [SerializeField] private WeaponSkillSettings _swordSkill = new WeaponSkillSettings
        { Cost = 16f, Cooldown = 5f, Startup = .34f, Recovery = .28f, Range = 3f, Angle = 140f, DamageMultiplier = 1.4f };
        [Header("받아베기 반격 표식")]
        [SerializeField] private float _counterDuration = 2f;
        [SerializeField] private float _counterEndGrace = 1.2f;
        [SerializeField] private float _counterStartup = .18f;
        [SerializeField] private float _counterRange = 2.2f;
        [SerializeField] private float _counterAngle = 90f;
        [SerializeField] private Vector3 _counterDamage = new Vector3(1.2f, 2.6f, 4.5f);
        [SerializeField] private float _swordStepDistance = .6f;
        [SerializeField] private float _combatTokenDuration = 2f;
        [SerializeField] private float _rollSkillWindow = 1f;
        public float CounterDuration => _counterDuration;
        public float CounterEndGrace => _counterEndGrace;
        public float CounterStartup => _counterStartup;
        public float CounterRange => _counterRange;
        public float CounterAngle => _counterAngle;
        public float CounterDamage(int count) => count >= 3 ? _counterDamage.z : count == 2 ? _counterDamage.y : _counterDamage.x;
        public float SwordStepDistance => _swordStepDistance;
        public float CombatTokenDuration => _combatTokenDuration;
        public float RollSkillWindow => _rollSkillWindow;
        [SerializeField] private float _attackBufferWindow = .12f;
        public float AttackBufferWindow => _attackBufferWindow;
        [SerializeField] private WeaponSkillSettings _bowSkill = new WeaponSkillSettings
        { Cost = 15f, Cooldown = 8f, Startup = .20f, Recovery = .22f, Range = 1.6f, Angle = 70f, DamageMultiplier = .6f };
        // 공명진은 설치 확인 뒤 0.25초에 생성되고 약한 설치 타격 한 번만 남긴다. Range는 설치 거리다
        [SerializeField] private WeaponSkillSettings _bellSkill = new WeaponSkillSettings
        { Cost = 15f, Cooldown = 12f, Startup = .25f, Recovery = .25f, Range = 5f, Angle = 360f, DamageMultiplier = .3f };
        [Min(0f)] [SerializeField] private float _bowSkillRollDelay = .10f;
        [Min(.01f)] [SerializeField] private float _bowSkillSlideDuration = .30f;
        [Min(0f)] [SerializeField] private float _bowSkillSlideDistance = 2f;
        [Min(1)] [SerializeField] private int _bellFieldMaxTargets = 6;
        [Min(.01f)] [SerializeField] private float _skillGroundProbeHeight = 2f;
        [Min(.01f)] [SerializeField] private float _skillGroundProbeDepth = 5f;

        [Header("공명진 - 공격 지원 장판")]
        [Min(.01f)] [SerializeField] private float _bellFieldRadius = 3f;
        [Min(.01f)] [SerializeField] private float _bellFieldDuration = 6f;
        [Min(1f)] [SerializeField] private float _bellFieldDamageMultiplier = 1.10f;
        [Min(0f)] [SerializeField] private float _bellFieldTailDuration = 1.5f;
        [Min(0f)] [SerializeField] private float _bellFieldTailMaximum = 2.5f;
        [Min(.01f)] [SerializeField] private float _bellFieldSampleInterval = .1f;
        [Min(0f)] [SerializeField] private float _bellFieldHeightTolerance = 1.5f;
        [Min(0f)] [SerializeField] private float _bellFieldSightOffset = .2f;
        [Min(0f)] [SerializeField] private float _bellFieldWideningDurationPenalty = 1f;
        [Min(.01f)] [SerializeField] private float _bellFieldHealDiscountWindow = 3f;
        public WeaponSkillSettings Skill(WeaponKind weapon) => weapon == WeaponKind.Sword ? _swordSkill : weapon == WeaponKind.Bow ? _bowSkill : _bellSkill;
        [Min(0f)] [SerializeField] private float _swordSkillStagger = .35f;
        [Min(0f)] [SerializeField] private float _swordSkillStaggerResistance = .7f;
        public float SwordSkillStagger => _swordSkillStagger;
        public float SwordSkillStaggerResistance => _swordSkillStaggerResistance;
        public float BowSkillRollDelay => _bowSkillRollDelay;
        public float BowSkillSlideDuration => _bowSkillSlideDuration;
        public float BowSkillSlideDistance => _bowSkillSlideDistance;
        public float BellFieldRadius => _bellFieldRadius;
        public float BellFieldDuration => _bellFieldDuration;
        public float BellFieldDamageMultiplier => _bellFieldDamageMultiplier;
        public float BellFieldTailDuration => _bellFieldTailDuration;
        public float BellFieldTailMaximum => _bellFieldTailMaximum;
        public float BellFieldSampleInterval => _bellFieldSampleInterval;
        public float BellFieldHeightTolerance => _bellFieldHeightTolerance;
        public float BellFieldSightOffset => _bellFieldSightOffset;
        public float BellFieldWideningDurationPenalty => _bellFieldWideningDurationPenalty;
        public float BellFieldHealDiscountWindow => _bellFieldHealDiscountWindow;
        public int BellFieldMaxTargets => _bellFieldMaxTargets;
        public float SkillGroundProbeHeight => _skillGroundProbeHeight;
        public float SkillGroundProbeDepth => _skillGroundProbeDepth;
    }
}
