using UnityEngine;
using Mirror;
using Geumeum.Common;
using Geumeum.Combat;
using Geumeum.Data;

namespace Geumeum.Art
{
    public sealed partial class PlayerCombatPresentation
    {
        private sealed class MarkView
        {
            public uint Generation;
            public Transform Target;
            public LineRenderer Ring;
            public readonly LineRenderer[] Pips = new LineRenderer[3];
        }
        private sealed class PulseView
        {
            public LineRenderer Line;
            public Vector3 Point;
            public Vector3 Forward;
            public double Start;
            public WeaponAbilityEvent Kind;
            public bool Active;
            public readonly LineRenderer[] Details = new LineRenderer[8];
            public byte Count;
        }
        private PlayerAttack _abilities;
        private PlayerAnimationController _abilityAnimation;
        private PlayerPresentation _abilityFacing;
        private readonly MarkView[] _markViews = new MarkView[3];
        private readonly PulseView[] _pulseViews = new PulseView[8];
        private MaterialPropertyBlock _abilityColor;
        private GameObject _abilityVisualRoot;
        private Camera _abilityCamera;
        private int _nextPulse;
        private static readonly int AbilityColorId = Shader.PropertyToID("_Color");

        private void BindAbilities()
        {
            _abilities = GetComponent<PlayerAttack>();
            _chargeSource = GetComponent<PrecisionShotController>();
            _effectCharacter = GetComponent<PlayerCharacter>();
            _abilityAnimation = GetComponent<PlayerAnimationController>();
            _abilityFacing = GetComponent<PlayerPresentation>();
            _skillVisualWeapon = GetComponent<PlayerWeapon>();
            if (_abilities != null) { _abilities.AbilityPresented += PlayAbility; }
        }

        private void OnDestroy()
        {
            if (_abilities != null) { _abilities.AbilityPresented -= PlayAbility; }
            if (_accentMesh != null) { Destroy(_accentMesh); }
            if (_abilityVisualRoot != null) { Destroy(_abilityVisualRoot); }
        }

        private bool EnsureAbilityViews()
        {
            if (_abilityVisualRoot != null) { _abilityVisualRoot.SetActive(true); return true; }
            if (!NetworkClient.active || !GameDB.IsInitialized || GameDB.Presentation.AbilityMaterial == null) { return false; }
            _abilityCamera = Camera.main;
            _abilityColor = new MaterialPropertyBlock();
            _abilityVisualRoot = new GameObject("Weapon ability visuals");
            for (int i = 0; i < 3; ++i)
            {
                MarkView view = new MarkView { Ring = NewAbilityLine("Mark", 40) };
                for (int j = 0; j < 3; ++j) { view.Pips[j] = NewAbilityLine("Charge", 12); }
                _markViews[i] = view;
            }
            for (int i = 0; i < _pulseViews.Length; ++i)
            {
                _pulseViews[i] = new PulseView { Line = NewAbilityLine("Pulse", 40) };
                for (int j = 0; j < 8; ++j) { _pulseViews[i].Details[j] = NewAbilityLine("Ability streak", 12); }
            }
            _chargeLine = NewAbilityLine("Bow draw focus", 40);
            return true;
        }

        private LineRenderer NewAbilityLine(string label, int points)
        {
            GameObject node = new GameObject(label);
            node.transform.SetParent(_abilityVisualRoot.transform);
            LineRenderer line = node.AddComponent<LineRenderer>();
            line.sharedMaterial = GameDB.Presentation.AbilityMaterial;
            line.useWorldSpace = true;
            line.positionCount = points;
            line.widthMultiplier = GameDB.Presentation.AbilityLineWidth;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        private void LateUpdate()
        {
            if (_abilities == null || !EnsureAbilityViews()) { return; }
            if (_abilityCamera == null) { _abilityCamera = Camera.main; }
            double now = VisualTime;
            BeginAccents();
            // 성공 패링은 장판과 잔광보다 먼저 메시 예산을 받는다
            DrawDeflection(Time.unscaledDeltaTime);
            DrawCharge(now);
            DrawWeaponSkill(now);
            DrawSustainedAccents(now);
            DrawMark(_markViews[0], _abilities.Wedge, true, now);
            DrawMark(_markViews[1], _abilities.EchoFirst, false, now);
            DrawMark(_markViews[2], _abilities.EchoSecond, false, now);
            PresentationConfigSO config = GameDB.Presentation;
            foreach (PulseView pulse in _pulseViews)
            {
                if (!pulse.Active) { continue; }
                float duration = pulse.Kind == WeaponAbilityEvent.EchoRecall ? GameDB.Combat.EchoStartupDuration : config.AbilityPulseSeconds;
                float elapsed = Mathf.Max(0, (float)(now - pulse.Start));
                float lifetime = pulse.Kind == WeaponAbilityEvent.EchoRecall ? duration : Mathf.Max(duration, config.AbilityAfterglowSeconds);
                if (elapsed >= lifetime)
                {
                    pulse.Line.enabled = false;
                    foreach (var detail in pulse.Details) { detail.enabled = false; }
                    // A delayed pause can correct the interpolated clock backwards; retire only on confirmed time
                    if (RunTimeLocator.Current.GameplayElapsedSeconds - pulse.Start >= lifetime) { HidePulse(pulse); }
                    continue;
                }
                DrawRadiantPulse(pulse, elapsed, lifetime);
                float progress = elapsed / duration;
                if (progress >= 1f)
                {
                    pulse.Line.enabled = false;
                    foreach (var detail in pulse.Details) { detail.enabled = false; }
                    continue;
                }
                if (DrawSkillPulse(pulse, progress)) { continue; }
                DrawPulseDetails(pulse, progress);
                bool bell = pulse.Kind == WeaponAbilityEvent.BellContact;
                bool recall = pulse.Kind == WeaponAbilityEvent.EchoRecall;
                float radius = bell ? Mathf.Lerp(0.3f, GameDB.Combat.BellRange, progress) :
                    recall ? Mathf.Lerp(config.EchoMarkRadius, 0.05f, progress) :
                    Mathf.Lerp(0.04f, pulse.Kind == WeaponAbilityEvent.WedgeBurst || pulse.Kind == WeaponAbilityEvent.EchoBurst
                        ? config.AbilityBurstRadius : config.EchoMarkRadius, progress);
                Color color = pulse.Kind == WeaponAbilityEvent.BowReleased || pulse.Kind == WeaponAbilityEvent.ArrowContact || pulse.Kind == WeaponAbilityEvent.WedgeBurst ||
                    pulse.Kind == WeaponAbilityEvent.WedgeCracked || pulse.Kind == WeaponAbilityEvent.WedgePlaced
                    ? config.WedgeColor : config.EchoColor;
                color.a *= 1f - Mathf.Clamp01(progress);
                color.a *= IsLocalPlayer ? 1f : config.RemoteAbilityOpacity;
                if (bell)
                {
                    DrawArc(pulse.Line, pulse.Point + Vector3.up * 0.15f, pulse.Forward,
                        Vector3.Cross(Vector3.up, pulse.Forward), radius, GameDB.Combat.BellAngle, color);
                }
                else
                {
                    Vector3 right = _abilityCamera != null ? _abilityCamera.transform.right : Vector3.right;
                    Vector3 up = _abilityCamera != null ? _abilityCamera.transform.up : Vector3.up;
                    DrawArc(pulse.Line, pulse.Point, right, up, radius, 360f, color);
                }
            }
            EndAccents();
        }

        private void DrawMark(MarkView view, WeaponAbilityMark mark, bool wedge, double now)
        {
            if (!mark.IsActive(now)) { HideMark(view); return; }
            if (view.Generation != mark.Generation || view.Target == null)
            {
                view.Target = null;
                if (NetworkClient.spawned.TryGetValue(mark.Target.EntityNetId, out NetworkIdentity identity))
                {
                    if (mark.Target.PartId != 0 && AimAssistTargetResolver.TryResolve(identity.transform, mark.Target.PartId, out IAimAssistTarget aim))
                    { view.Target = (aim as Component)?.transform; }
                    else if (mark.Target.PartId == 0 && DamageTargetResolver.TryResolve(identity.transform, out IDamageable damage))
                    { view.Target = (damage as Component)?.transform; }
                }
                view.Generation = mark.Generation;
            }
            if (view.Target == null) { HideMark(view); return; }
            PresentationConfigSO config = GameDB.Presentation;
            Vector3 point = view.Target.TransformPoint(mark.LocalPoint);
            Vector3 right = _abilityCamera != null ? _abilityCamera.transform.right : Vector3.right;
            Vector3 up = _abilityCamera != null ? _abilityCamera.transform.up : Vector3.up;
            // 표면보다 조금 띄워 깊이 충돌을 피하되 벽 너머로 그리지 않는다
            if (_abilityCamera != null) { point += (_abilityCamera.transform.position - point).normalized * 0.04f; }
            Color color = wedge ? config.WedgeColor : config.EchoColor;
            color.a *= IsLocalPlayer ? 1f : config.RemoteAbilityOpacity;
            float radius = wedge ? GameDB.Combat.WedgeRadius : config.EchoMarkRadius;
            DrawArc(view.Ring, point, right, up, radius, wedge && mark.Count > 0 ? 270f : 360f, color);
            Color halo = RadiantColor(wedge, .16f + .06f * Mathf.Sin((float)now * 4));
            Accent(point, right, up, radius * 1.4f, radius * 1.4f, halo, 1);
            for (int j = 0; j < (wedge ? 3 : mark.Count); ++j)
            {
                if (wedge)
                {
                    Accent(point + right * (j - 1) * radius * .32f, (right + up * .3f).normalized,
                        (up - right * .3f).normalized, radius * .06f, radius * (.7f - Mathf.Abs(j - 1) * .16f),
                        RadiantColor(true, .55f + mark.Count * .2f), 2);
                }
                else
                {
                    float angle = (float)now * 1.3f + j * Mathf.PI * 2 / 3;
                    Vector3 orbit = point + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius * 1.3f;
                    Accent(orbit, right, up, radius * .22f, radius * .35f, RadiantColor(false, .65f), 2);
                }
            }
            for (int i = 0; i < 3; ++i)
            {
                bool visible = !wedge && i < mark.Count;
                view.Pips[i].enabled = visible;
                if (visible)
                {
                    Vector3 pip = point + right * ((i - 1) * radius * 0.7f) + up * radius * 1.5f;
                    DrawArc(view.Pips[i], pip, right, up, radius * 0.16f, 360f, color);
                }
            }
        }

        private static void HideMark(MarkView view)
        {
            view.Ring.enabled = false;
            foreach (LineRenderer pip in view.Pips) { pip.enabled = false; }
        }

        private void DrawArc(LineRenderer line, Vector3 point, Vector3 a, Vector3 b, float radius, float angle, Color color)
        {
            line.enabled = true;
            line.loop = angle >= 360f;
            _abilityColor.SetColor(AbilityColorId, color);
            line.SetPropertyBlock(_abilityColor);
            int count = line.positionCount;
            for (int i = 0; i < count; ++i)
            {
                float radians = ((float)i / (line.loop ? count : count - 1) - 0.5f) * angle * Mathf.Deg2Rad;
                line.SetPosition(i, point + radius * (a * Mathf.Cos(radians) + b * Mathf.Sin(radians)));
            }
        }

        private void QueueAbilityPulse(WeaponAbilityEvent kind, Vector3 point, byte count, double time)
        {
            PulseView pulse = _pulseViews[_nextPulse++ % _pulseViews.Length];
            HidePulse(pulse);
            if (kind == WeaponAbilityEvent.BellContact) { point.y = EffectHandPoint().y; }
            pulse.Count = count; pulse.Kind = kind; pulse.Point = point; pulse.Start = time; pulse.Active = true;
            Vector2 facing = _abilityFacing != null ? _abilityFacing.AimDirection : Vector2.up;
            pulse.Forward = new Vector3(facing.x, 0f, facing.y);
        }

        private void PlayAbility(WeaponAbilityEvent kind, Vector3 point, byte count, double time)
        {
            // RPC can precede the next replicated clock tick. A slightly future cue is fresh,
            // not stale; retain it until the presentation clock catches up
            double now = RunTimeLocator.Current.GameplayElapsedSeconds;
            _ = VisualTime;
            if (kind == WeaponAbilityEvent.BowReleased && count != 0 && !double.IsNaN(time) && !double.IsInfinity(time))
            {
                _abilityAnimation?.PlayPrecisionRelease(time);
            }

            if (!EnsureAbilityViews() || double.IsNaN(time) || double.IsInfinity(time) ||
                now - time > GameDB.Presentation.LateOneShotToleranceSeconds) { return; }
            _visualClock.ObserveCue(time);
            SoundKey key = SoundKey.None;
            switch (kind)
            {
                case WeaponAbilityEvent.SwordSkill: key = count > 0 ? SoundKey.MeleeHit : SoundKey.MeleeWhiff; break;
                case WeaponAbilityEvent.BowStab: key = count > 0 ? SoundKey.ArrowHit : SoundKey.BowFire; break;
                case WeaponAbilityEvent.BellField: key = SoundKey.BellSwing; break;
                case WeaponAbilityEvent.BellFieldBurst: key = SoundKey.EchoBurst; break;
                case WeaponAbilityEvent.BowReleased: key = SoundKey.BowFire; break;
                case WeaponAbilityEvent.ArrowContact: key = SoundKey.ArrowHit; break;
                case WeaponAbilityEvent.WedgePlaced: key = SoundKey.WedgePlace; break;
                case WeaponAbilityEvent.WedgeCracked: key = SoundKey.WedgeCrack; break;
                case WeaponAbilityEvent.WedgeBurst: key = SoundKey.WedgeBurst; break;
                case WeaponAbilityEvent.BellContact: key = SoundKey.BellSwing; break;
                case WeaponAbilityEvent.EchoCharged: key = count >= 3 ? SoundKey.EchoThird : count == 2 ? SoundKey.EchoSecond : SoundKey.EchoFirst; break;
                case WeaponAbilityEvent.EchoRecall: key = SoundKey.EchoRecall; break;
                case WeaponAbilityEvent.EchoBurst: key = SoundKey.EchoBurst; break;
            }
            SfxPlayer.Play(key, point, IsLocalPlayer, _identity == null ? 0u : _identity.netId);
            if (kind == WeaponAbilityEvent.EchoRecall)
            {
                for (int i = 0; i < 2; ++i)
                {
                    WeaponAbilityMark mark = i == 0 ? _abilities.EchoFirst : _abilities.EchoSecond;
                    DrawMark(_markViews[i + 1], mark, false, now);
                    Transform target = _markViews[i + 1].Target;
                    if (mark.IsActive(now) && target != null)
                    {
                        QueueAbilityPulse(kind, target.TransformPoint(mark.LocalPoint), mark.Count, time);
                    }
                }
            }
            else { QueueAbilityPulse(kind, point, count, time); }
            if (IsLocalPlayer && count > 0 && (kind == WeaponAbilityEvent.SwordSkill || kind == WeaponAbilityEvent.BowStab))
            {
                _abilityAnimation?.PlayHitStop(kind == WeaponAbilityEvent.SwordSkill ? GameDB.Presentation.SwordSkillHitStopSeconds : GameDB.Presentation.BowStabHitStopSeconds);
            }

            if (IsLocalPlayer && kind == WeaponAbilityEvent.EchoBurst)
            { _abilityAnimation?.PlayHitStop(GameDB.Presentation.BellBurstHitStopSeconds); }
        }
    }
}
