using UnityEngine;
using Geumeum.Common;
using Geumeum.Combat;
using Geumeum.Data;

namespace Geumeum.Art
{
    public sealed partial class PlayerCombatPresentation
    {
        private LineRenderer _chargeLine;
        private PrecisionShotController _chargeSource;
        private PlayerCharacter _effectCharacter;
        private GameObject _effectModel;
        private Transform _effectHand;
        private WeaponKind _effectWeapon;

        private Vector3 EffectHandPoint()
        {
            WeaponKind weapon = _skillVisualWeapon != null ? _skillVisualWeapon.Weapon : WeaponKind.Sword;
            if (_effectCharacter != null && (_effectModel != _effectCharacter.ActiveModel || _effectWeapon != weapon))
            {
                _effectWeapon = weapon;
                _effectModel = _effectCharacter.ActiveModel;
                // 미러 모션의 활은 오른손에 있고, 왼손은 줄과 화살의 표현 기준점이다.
                string boneName = weapon == WeaponKind.Bow ? "LeftHand" : "RightHand";
                _effectHand = _effectModel == null ? null : System.Array.Find(
                    _effectModel.GetComponentsInChildren<Transform>(true), bone => bone.name == boneName);
            }
            return _effectHand != null ? _effectHand.position : transform.position + Vector3.up * GameDB.Combat.RangedMuzzleHeight;
        }

        private void DrawCharge(double now)
        {
            bool holding = _chargeSource != null && (_chargeSource.State == PrecisionShotState.Holding || _chargeSource.State == PrecisionShotState.Ready);
            holding &= _bowChargeView == null && !IsReleasedBowHold &&
                (_bowStatus == null || (!_bowStatus.IsDead && !_bowStatus.IsSwallowed && !_bowStatus.IsHitReacting));
            _chargeLine.enabled = holding;
            if (!holding) { return; }
            float progress = Mathf.Clamp01((float)((now - _chargeSource.HoldStartGameTime) / GameDB.Combat.PrecisionShotHoldDuration));
            var config = GameDB.Presentation;
            Vector3 forward = transform.forward;
            if (_abilityFacing != null) { Vector2 aim = _abilityFacing.AimDirection; forward = new Vector3(aim.x, 0f, aim.y); }
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Color color = config.WedgeColor;
            color.a *= IsLocalPlayer ? .8f : config.RemoteAbilityOpacity;
            _chargeLine.widthMultiplier = config.AbilityGlowWidth;
            DrawArc(_chargeLine, EffectHandPoint(), right, Vector3.up,
                config.BowFocusRadius * Mathf.Lerp(1.8f, .45f, progress), 90f + 270f * progress, color);
        }

        private static void HidePulse(PulseView pulse)
        {
            pulse.Active = false;
            if (pulse.Line != null) { pulse.Line.enabled = false; }
            foreach (var line in pulse.Details) { if (line != null) { line.enabled = false; } }
        }

        private void DrawPulseDetails(PulseView pulse, float progress)
        {
            var config = GameDB.Presentation;
            bool bow = pulse.Kind == WeaponAbilityEvent.BowReleased || pulse.Kind == WeaponAbilityEvent.ArrowContact ||
                pulse.Kind == WeaponAbilityEvent.WedgePlaced || pulse.Kind == WeaponAbilityEvent.WedgeCracked || pulse.Kind == WeaponAbilityEvent.WedgeBurst;
            bool recall = pulse.Kind == WeaponAbilityEvent.EchoRecall;
            bool wave = pulse.Kind == WeaponAbilityEvent.BellContact;
            bool burst = pulse.Kind == WeaponAbilityEvent.WedgeBurst || pulse.Kind == WeaponAbilityEvent.EchoBurst;
            Vector3 right = _abilityCamera != null ? _abilityCamera.transform.right : Vector3.right;
            Vector3 up = _abilityCamera != null ? _abilityCamera.transform.up : Vector3.up;
            Color color = bow ? config.WedgeColor : config.EchoColor;
            color.a *= (1f - progress) * (IsLocalPlayer ? 1f : config.RemoteAbilityOpacity);
            pulse.Line.widthMultiplier = config.AbilityGlowWidth * (burst ? 1.5f : .7f) * (1f - progress * .7f);
            for (int j = 0; j < pulse.Details.Length; ++j)
            {
                var line = pulse.Details[j];
                line.enabled = true;
                line.loop = false;
                line.widthMultiplier = config.AbilityGlowWidth * (1f - progress) * .6f;
                _abilityColor.SetColor(AbilityColorId, color);
                line.SetPropertyBlock(_abilityColor);
                float angle = j * Mathf.PI * 2f / pulse.Details.Length;
                Vector3 radial = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                if (wave)
                {
                    if (j >= 2) { line.enabled = false; continue; }
                    float p = Mathf.Clamp01(progress - (j + 1) * .09f);
                    DrawArc(line, pulse.Point + Vector3.up * (.2f + .12f * j), pulse.Forward,
                        Vector3.Cross(Vector3.up, pulse.Forward), Mathf.Lerp(.1f, GameDB.Combat.BellRange, p), GameDB.Combat.BellAngle, color);
                    continue;
                }
                if (recall)
                {
                    if (j >= 3) { line.enabled = false; continue; }
                    Vector3 end = EffectHandPoint();
                    for (int k = 0; k < line.positionCount; ++k)
                    {
                        float t = Mathf.Lerp(Mathf.Max(0f, progress - .3f), progress, (float)k / (line.positionCount - 1));
                        line.SetPosition(k, Vector3.Lerp(pulse.Point, end, t) + radial * (Mathf.Sin(t * Mathf.PI) * config.EchoRibbonBend));
                    }
                    continue;
                }
                float length = config.AbilityStreakLength * (burst ? 1.6f : .55f);
                Vector3 direction = pulse.Kind == WeaponAbilityEvent.BowReleased ? (pulse.Forward + radial * .15f).normalized : radial;
                Vector3 start = pulse.Point + direction * length * progress;
                Vector3 finish = start + direction * length * (1f - progress) * .45f;
                for (int k = 0; k < line.positionCount; ++k)
                {
                    line.SetPosition(k, Vector3.Lerp(start, finish, (float)k / (line.positionCount - 1)));
                }
            }
        }
    }
}
