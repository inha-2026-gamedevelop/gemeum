using UnityEngine;
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Art
{
    public sealed partial class PlayerCombatPresentation
    {
        private Geumeum.Combat.PlayerWeapon _skillVisualWeapon;
        private LineRenderer _fieldBoundary, _fieldCountdown, _fieldPreview, _fieldForbidden;
        private LineRenderer _stabArrow;
        private LineRenderer _slideScratch;
        private readonly Vector3[] _slidePoints = new Vector3[12];
        private int _slidePointCount;
        private Transform _stabHand;
        private GameObject _stabModel;

        private void DrawWeaponSkill(double now)
        {
            if (_fieldBoundary == null)
            {
                _fieldBoundary = NewAbilityLine("Resonance boundary", 48);
                _fieldCountdown = NewAbilityLine("Resonance countdown", 48);
                _fieldPreview = NewAbilityLine("Resonance placement", 48);
                _fieldForbidden = NewAbilityLine("Resonance forbidden", 5);
                _stabArrow = NewAbilityLine("Held marking arrow", 3);
                _slideScratch = NewAbilityLine("Sliding arrow scratch", _slidePoints.Length);
                _slideScratch.widthCurve = AnimationCurve.Linear(0, 1, 1, 0);
            }
            var config = GameDB.Presentation;
            // 확정된 진은 동료 화면에도 보인다. 남은 시간은 안쪽 고리 크기로 읽는다
            bool active = _abilities.BellFieldEnd > now && _abilities.BellFieldRadius > 0;
            _fieldBoundary.enabled = _fieldCountdown.enabled = active;
            if (active)
            {
                Vector3 point = _abilities.BellFieldPoint + Vector3.up * .04f;
                float radius = _abilities.BellFieldRadius;
                float remaining = Mathf.Clamp01((float)(_abilities.BellFieldEnd - now) /
                    Mathf.Max(.01f, GameDB.Combat.BellFieldDuration));
                Color color = config.EchoColor; color.a *= IsLocalPlayer ? .65f : config.RemoteAbilityOpacity;
                DrawArc(_fieldBoundary, point, Vector3.right, Vector3.forward, radius, 360, color);
                DrawArc(_fieldCountdown, point, Vector3.right, Vector3.forward, radius * remaining, 360, color);
            }
            // 배치 중인 진은 소유자에게만, 확정된 진과 다른 선으로 보여 준다
            _fieldPreview.enabled = false;
            _fieldForbidden.enabled = false;
            if (IsLocalPlayer && _abilities.IsPlacingField)
            {
                bool valid = _abilities.IsPlacementValid;
                Vector3 point = _abilities.PlacementPoint + Vector3.up * .03f;
                float radius = _abilities.PlacementPreviewRadius;
                Color color = config.EchoColor; color.a = valid ? .35f : .18f;
                // 유효하면 닫힌 실선, 아니면 끊긴 호에 금지 표시를 겹쳐 색 없이도 구분되게 한다
                DrawArc(_fieldPreview, point, Vector3.right, Vector3.forward, radius, valid ? 360 : 300, color);
                if (!valid)
                {
                    _fieldForbidden.enabled = true;
                    _fieldForbidden.loop = false;
                    _fieldForbidden.widthMultiplier = config.AbilityLineWidth;
                    float arm = radius * .45f;
                    _fieldForbidden.SetPosition(0, point + new Vector3(-arm, 0, -arm));
                    _fieldForbidden.SetPosition(1, point + new Vector3(arm, 0, arm));
                    _fieldForbidden.SetPosition(2, point);
                    _fieldForbidden.SetPosition(3, point + new Vector3(-arm, 0, arm));
                    _fieldForbidden.SetPosition(4, point + new Vector3(arm, 0, -arm));
                    _abilityColor.SetColor(AbilityColorId, config.EchoColor);
                    _fieldForbidden.SetPropertyBlock(_abilityColor);
                }
            }
            // A simple transient arrow shaft is attached to the stabbing hand; no icon art needed
            _stabArrow.enabled = false;
            _slideScratch.enabled = false;
            if (!IsVisualSliding(now)) { _slidePointCount = 0; }
            if (_effectCharacter != null && _stabModel != _effectCharacter.ActiveModel)
            {
                _stabModel = _effectCharacter.ActiveModel;
                _stabHand = _stabModel == null ? null : System.Array.Find(_stabModel.GetComponentsInChildren<Transform>(true), t => t.name == "LeftHand");
            }
            if (_skillVisualWeapon != null && _skillVisualWeapon.Weapon == WeaponKind.Bow &&
                _activeKind == PlayerActionCueKind.WeaponSkill && _activeExecutionId != 0 && now < _visualCue.EndGameplayTime && _stabHand != null)
            {
                _stabArrow.enabled = true;
                _stabArrow.widthMultiplier = config.AbilityLineWidth;
                Vector3 direction = _stabHand.forward;
                _stabArrow.SetPosition(0, _stabHand.position - direction * .10f);
                _stabArrow.SetPosition(1, _stabHand.position + direction * .24f);
                _stabArrow.SetPosition(2, _stabHand.position + direction * .28f);
                _abilityColor.SetColor(AbilityColorId, config.WedgeColor);
                _stabArrow.SetPropertyBlock(_abilityColor);
                if (IsVisualSliding(now))
                {
                    Vector3 tip = _stabHand.position + direction * .28f;
                    if (_slidePointCount == 0)
                    {
                        for (int i = 0; i < _slidePoints.Length; ++i) { _slidePoints[i] = tip; }
                        _slidePointCount = 1;
                    }
                    if (!RunTimeLocator.Current.IsPaused)
                    {
                        for (int i = _slidePoints.Length - 1; i > 0; --i) { _slidePoints[i] = _slidePoints[i - 1]; }
                        _slidePoints[0] = tip;
                        ++_slidePointCount;
                    }
                    _slideScratch.enabled = true;
                    _slideScratch.loop = false;
                    _slideScratch.widthMultiplier = config.AbilityGlowWidth;
                    _slideScratch.SetPositions(_slidePoints);
                    _slideScratch.SetPropertyBlock(_abilityColor);
                }
            }
        }

        private bool DrawSkillPulse(PulseView pulse, float progress)
        {
            if (pulse.Kind < WeaponAbilityEvent.SwordSkill || pulse.Kind > WeaponAbilityEvent.BellFieldBurst) { return false; }
            foreach (var line in pulse.Details) { line.enabled = false; }
            var config = GameDB.Presentation;
            Color color = pulse.Kind == WeaponAbilityEvent.BellField || pulse.Kind == WeaponAbilityEvent.BellFieldBurst ? config.EchoColor : config.WedgeColor;
            color.a *= (1f - progress) * (IsLocalPlayer ? 1f : config.RemoteAbilityOpacity);
            pulse.Line.widthMultiplier = config.AbilityGlowWidth;
            if (pulse.Kind == WeaponAbilityEvent.SwordSkill)
            {
                var skill = GameDB.Combat.Skill(WeaponKind.Sword);
                DrawArc(pulse.Line, pulse.Point, pulse.Forward, Vector3.Cross(Vector3.up, pulse.Forward),
                    skill.Range * Mathf.Lerp(.65f, 1f, progress), skill.Angle, color);
            }
            else if (pulse.Kind == WeaponAbilityEvent.BowStab)
            {
                Vector3 side = Vector3.Cross(Vector3.up, pulse.Forward);
                pulse.Line.widthMultiplier = config.AbilityGlowWidth * 2.2f * (1 - progress);
                DrawArc(pulse.Line, pulse.Point, pulse.Forward, side,
                    GameDB.Combat.Skill(WeaponKind.Bow).Range * Mathf.Lerp(.4f, .85f, progress), 100, color);
            }
            else
            {
                // 생성 순간의 한 번뿐이다. 두 번째 울림은 v4에서 없앴다
                DrawArc(pulse.Line, pulse.Point + Vector3.up * .06f, Vector3.right, Vector3.forward,
                    GameDB.Combat.BellFieldRadius * progress, 360, color);
            }
            for (int j = 0; j < 3; ++j)
            {
                LineRenderer detail = pulse.Details[j];
                Color echo = color * config.AbilityRadiance;
                echo.a = color.a * (1 - j * .2f);
                detail.widthMultiplier = config.AbilityGlowWidth * (1 - progress) * (j == 0 ? 1.5f : .6f);
                if (pulse.Kind == WeaponAbilityEvent.BellField || pulse.Kind == WeaponAbilityEvent.BellFieldBurst)
                {
                    float radius = GameDB.Combat.BellFieldRadius * Mathf.Clamp01(progress * (1.5f - j * .2f));
                    DrawArc(detail, pulse.Point + Vector3.up * (.08f + j * .18f * (1 - progress)),
                        Vector3.right, Vector3.forward, radius, 360, echo);
                }
                else
                {
                    var skill = GameDB.Combat.Skill(pulse.Kind == WeaponAbilityEvent.SwordSkill ? WeaponKind.Sword : WeaponKind.Bow);
                    Vector3 side = Vector3.Cross(Vector3.up, pulse.Forward);
                    DrawArc(detail, pulse.Point + Vector3.up * (.08f * (j - 1)), pulse.Forward, side,
                        skill.Range * Mathf.Lerp(.35f + j * .12f, .85f + j * .05f, progress), skill.Angle, echo);
                }
            }
            return true;
        }
    }
}
