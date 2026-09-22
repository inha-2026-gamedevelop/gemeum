using UnityEngine;
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Art
{
    public sealed partial class PlayerCombatPresentation
    {
        // One reusable mesh per player: bounded accents, no per-hit objects or particle simulation clocks
        private const int AccentCapacity = 384;
        private Mesh _accentMesh;
        private MeshRenderer _accentRenderer;
        private readonly Vector3[] _accentVertices = new Vector3[AccentCapacity * 4];
        private readonly Vector3[] _accentUv = new Vector3[AccentCapacity * 4];
        private readonly Color[] _accentColors = new Color[AccentCapacity * 4];
        private readonly int[] _accentIndices = new int[AccentCapacity * 6];
        private int _accentCount;
        private AbilityVisualClock _visualClock;
        private PlayerActionCue _visualCue;
        private readonly PulseView _deflection = new PulseView();
        private uint _lastDeflectionExecution;
        private float _deflectionElapsed;
        private float _deflectionDelay;

        private void QueueDeflection(uint executionId, double time)
        {
            if (executionId == _lastDeflectionExecution || !EnsureAbilityViews()) { return; }
            _lastDeflectionExecution = executionId;
            _visualClock.ObserveCue(time);
            UpdateDeflectionPoint();
            _deflection.Start = time;
            _deflection.Kind = WeaponAbilityEvent.SwordSkill;
            _deflection.Count = 1;
            _deflection.Active = true;
            _deflectionElapsed = 0f;
            _deflectionDelay = Mathf.Clamp((float)(_visualCue.ImpactGameplayTime - time),
                0f, GameDB.Presentation.AbilityAfterglowSeconds);
        }

        private void UpdateDeflectionPoint()
        {
            Vector3 point = EffectHandPoint();
            if (_blade != null && _blade.TryGetBladeSegment(out Vector3 hilt, out Vector3 tip))
            {
                point = Vector3.Lerp(hilt, tip, .7f);
            }

            _deflection.Point = point;
        }

        private void DrawDeflection(float deltaTime)
        {
            if (!_deflection.Active) { return; }
            float duration = GameDB.Presentation.AbilityAfterglowSeconds;
            // 성공은 선딜 전에 확정된다. 검을 들어 올린 순간의 위치에 충돌광을 고정한다.
            if (_deflectionDelay > 0f)
            {
                if (RunTimeLocator.Current.IsPaused) { return; }
                _deflectionDelay -= Mathf.Max(0f, deltaTime);
                if (_deflectionDelay > 0f) { return; }
                UpdateDeflectionPoint();
            }
            // 성공 통지 수신부터 재생해 앞선 보간 시계가 섬광을 생략하지 않게 한다
            if (_deflectionElapsed < duration) { DrawRadiantPulse(_deflection, _deflectionElapsed, duration); }
            else { _deflection.Active = false; }
            if (!RunTimeLocator.Current.IsPaused) { _deflectionElapsed += Mathf.Max(0f, deltaTime); }
        }

        private double VisualTime => _visualClock.Sample(RunTimeLocator.Current.GameplayElapsedSeconds,
            Time.unscaledTimeAsDouble, RunTimeLocator.Current.IsPaused, Mirror.NetworkServer.active);

        private bool IsVisualSliding(double now)
        {
            if (_activeKind != PlayerActionCueKind.WeaponSkill || _activeWeapon != WeaponKind.Bow || _activeExecutionId == 0) { return false; }
            var config = GameDB.Combat; var skill = config.Skill(WeaponKind.Bow);
            double scale = (_visualCue.EndGameplayTime - _visualCue.StartGameplayTime) /
                (skill.Startup + config.BowSkillSlideDuration + skill.Recovery);
            return now >= _visualCue.ImpactGameplayTime && now < _visualCue.EndGameplayTime - skill.Recovery * scale;
        }

        private void BeginAccents()
        {
            _accentCount = 0;
            if (_accentMesh != null && _accentRenderer != null) { return; }
            if (_accentMesh != null) { Destroy(_accentMesh); }
            var node = new GameObject("Weapon flashes and embers");
            node.transform.SetParent(_abilityVisualRoot.transform, false);
            _accentMesh = new Mesh { name = "Pooled weapon accents" };
            _accentMesh.MarkDynamic();
            node.AddComponent<MeshFilter>().sharedMesh = _accentMesh;
            _accentRenderer = node.AddComponent<MeshRenderer>();
            _accentRenderer.sharedMaterial = GameDB.Presentation.AbilityMaterial;
            _accentRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _accentRenderer.receiveShadows = false;
            for (int i = 0; i < AccentCapacity; ++i)
            {
                int v = i * 4, t = i * 6;
                _accentIndices[t] = v; _accentIndices[t + 1] = v + 1; _accentIndices[t + 2] = v + 2;
                _accentIndices[t + 3] = v; _accentIndices[t + 4] = v + 2; _accentIndices[t + 5] = v + 3;
            }
        }

        private void EndAccents()
        {
            _accentRenderer.enabled = _accentCount > 0;
            if (_accentCount == 0) { return; }
            _accentMesh.Clear(false);
            _accentMesh.SetVertices(_accentVertices, 0, _accentCount * 4);
            _accentMesh.SetUVs(0, _accentUv, 0, _accentCount * 4);
            _accentMesh.SetColors(_accentColors, 0, _accentCount * 4);
            _accentMesh.SetTriangles(_accentIndices, 0, _accentCount * 6, 0, true);
        }

        private void Accent(Vector3 point, Vector3 right, Vector3 up, float width, float height, Color color, float shape)
        {
            if (_accentCount >= AccentCapacity || color.a <= .001f || width <= 0 || height <= 0) { return; }
            int v = _accentCount++ * 4;
            right *= width; up *= height;
            _accentVertices[v] = point - right - up; _accentVertices[v + 1] = point + right - up;
            _accentVertices[v + 2] = point + right + up; _accentVertices[v + 3] = point - right + up;
            _accentUv[v] = new Vector3(0, 0, shape); _accentUv[v + 1] = new Vector3(1, 0, shape);
            _accentUv[v + 2] = new Vector3(1, 1, shape); _accentUv[v + 3] = new Vector3(0, 1, shape);
            for (int i = 0; i < 4; ++i) { _accentColors[v + i] = color; }
        }

        private Color RadiantColor(bool bow, float alpha)
        {
            var c = GameDB.Presentation;
            Color tint = bow ? c.WedgeColor : c.EchoColor;
            tint *= c.AbilityRadiance;
            tint.a = alpha * (IsLocalPlayer ? 1f : c.RemoteAbilityOpacity);
            return tint;
        }

        private void DrawRadiantPulse(PulseView pulse, float elapsed, float duration)
        {
            if (pulse.Kind == WeaponAbilityEvent.SwordSkill && pulse.Count == 0) { return; }
            var config = GameDB.Presentation;
            float p = Mathf.Clamp01(elapsed / duration), fade = (1 - p) * (1 - p);
            bool bell = pulse.Kind == WeaponAbilityEvent.BellContact || pulse.Kind == WeaponAbilityEvent.EchoCharged ||
                pulse.Kind == WeaponAbilityEvent.EchoRecall || pulse.Kind == WeaponAbilityEvent.EchoBurst ||
                pulse.Kind == WeaponAbilityEvent.BellField || pulse.Kind == WeaponAbilityEvent.BellFieldBurst;
            bool burst = pulse.Kind == WeaponAbilityEvent.WedgeBurst || pulse.Kind == WeaponAbilityEvent.EchoBurst ||
                pulse.Kind == WeaponAbilityEvent.BellFieldBurst;
            bool field = pulse.Kind == WeaponAbilityEvent.BellField || pulse.Kind == WeaponAbilityEvent.BellFieldBurst;
            bool recall = pulse.Kind == WeaponAbilityEvent.EchoRecall;
            Vector3 right = _abilityCamera != null ? _abilityCamera.transform.right : Vector3.right;
            Vector3 up = _abilityCamera != null ? _abilityCamera.transform.up : Vector3.up;
            Vector3 center = recall ? Vector3.Lerp(pulse.Point, EffectHandPoint(), Mathf.SmoothStep(0, 1, p)) : pulse.Point;
            Color color = RadiantColor(!bell, fade);
            float scale = burst ? 1.6f : 1f;
            float flash = config.AbilityFlashRadius * scale * (1 + p * .35f);
            Color core = Color.Lerp(color, new Color(3, 2.8f, 2.4f, color.a), .45f);
            core.a *= Mathf.Pow(Mathf.Clamp01(1 - p * (bell || pulse.Kind == WeaponAbilityEvent.SwordSkill ? 4f : 7f)), 2);
            Accent(center, right, up, flash, flash, core, 1);
            // 금속 충돌은 짧고 얇은 섬광으로 구분한다
            if (!bell) { Accent(center, right, up, flash * 1.8f, flash * .045f, core, 2); }
            int sparks = Mathf.Clamp(config.AbilitySparkCount, 4, 32);
            for (int j = 0; j < sparks; ++j)
            {
                float seed = Mathf.Repeat(j * .618034f + pulse.Point.x * .137f + pulse.Point.z * .193f, 1f);
                float angle = j * 2.399963f + seed * .8f;
                Vector3 radial = field ? new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) :
                    right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                float reach = field ? GameDB.Combat.BellFieldRadius : config.AbilityStreakLength * scale;
                float travel = (1 - Mathf.Exp(-p * 3.8f)) * (.22f + seed * .78f);
                Vector3 point = center + radial * reach * travel;
                if (bell) { point += Vector3.up * p * (.25f + seed * .6f); }
                else { point -= Vector3.up * p * p * .7f; }
                bool release = pulse.Kind == WeaponAbilityEvent.BowReleased;
                if (release) { point = center + (pulse.Forward + radial * .22f) * reach * travel; }
                float size = config.AbilitySparkSize * (.35f + seed * .65f) * (1 - p * .65f);
                Color spark = color;
                spark.a *= Mathf.Clamp01((1 - p - seed * .18f) * 2);
                if (bell)
                {
                    Accent(point, right, up, size * .55f, size, spark, 1);
                }
                else
                {
                    Vector3 velocity = release ? pulse.Forward + radial * .22f : radial - Vector3.up * p * .8f;
                    Vector3 screenNormal = Vector3.Cross(right, up).normalized;
                    Vector3 along = Vector3.ProjectOnPlane(velocity, screenNormal).normalized;
                    if (along.sqrMagnitude < .01f) { along = right; }
                    Vector3 across = Vector3.Cross(screenNormal, along).normalized;
                    spark = Color.Lerp(spark, new Color(1.4f, .22f, .025f, spark.a), p * .65f);
                    Accent(point, along, across, size * Mathf.Lerp(4.5f, .65f, p), size * .3f, spark, 2);
                }
            }
            if (burst)
            {
                Color halo = color; halo.a *= bell ? .22f : .08f;
                float radius = config.AbilityBurstRadius * (1 + p) * scale;
                Accent(center, field ? Vector3.right : right, field ? Vector3.forward : up, radius, radius, halo, 3);
            }
        }

        private void DrawSustainedAccents(double now)
        {
            var config = GameDB.Presentation;
            Vector3 right = _abilityCamera != null ? _abilityCamera.transform.right : Vector3.right;
            Vector3 up = _abilityCamera != null ? _abilityCamera.transform.up : Vector3.up;
            if (_chargeLine.enabled)
            {
                float charge = Mathf.Clamp01((float)((now - _chargeSource.HoldStartGameTime) / GameDB.Combat.PrecisionShotHoldDuration));
                Vector3 hand = EffectHandPoint();
                for (int j = 0; j < 12; ++j)
                {
                    float phase = Mathf.Repeat((float)now * .8f + j * .618034f, 1f);
                    float angle = j * 2.399963f;
                    float radius = config.BowFocusRadius * (2.1f - charge) * (1 - phase);
                    Vector3 point = hand + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
                    float size = config.AbilitySparkSize * (.3f + charge * .35f);
                    float alpha = Mathf.Sin(phase * Mathf.PI) * (.3f + charge * .5f);
                    Accent(point, right, up, size * .35f, size, RadiantColor(true, alpha), 2);
                }
                float glow = config.AbilityFlashRadius * (.3f + .35f * charge);
                Accent(hand, right, up, glow, glow, RadiantColor(true, .25f + charge * .35f), 1);
            }
            if (IsVisualSliding(now) && _slidePointCount > 0)
            {
                for (int j = 0; j < Mathf.Min(_slidePointCount, _slidePoints.Length); ++j)
                {
                    float tail = 1 - (float)j / _slidePoints.Length;
                    Vector3 point = _slidePoints[j];
                    float size = config.AbilitySparkSize * (1 + tail);
                    Accent(point, right, up, size * 2, size, RadiantColor(true, tail * .65f), 1);
                    point.y = transform.position.y + .04f;
                    Accent(point, Vector3.right, Vector3.forward, size * 1.6f, size * 2.5f, RadiantColor(true, tail * .25f), 1);
                }
            }
            if (_abilities.BellFieldEnd > now)
            {
                Vector3 center = _abilities.BellFieldPoint + Vector3.up * .06f;
                float radius = _abilities.BellFieldRadius;
                for (int j = 0; j < 12; ++j)
                {
                    float phase = Mathf.Repeat((float)now * .3f + j * .618034f, 1f);
                    float angle = j * 2.399963f;
                    Vector3 axis = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    Vector3 point = center + axis * radius * Mathf.Sqrt((j + .5f) / 12f) * .9f;
                    Color mote = RadiantColor(false, Mathf.Sin(phase * Mathf.PI) * .5f);
                    Accent(point + Vector3.up * (.1f + phase * .85f), right, up,
                        config.AbilitySparkSize * .45f, config.AbilitySparkSize, mote, 1);
                }
                Accent(center, Vector3.right, Vector3.forward, radius, radius, RadiantColor(false, .09f), 3);
            }
        }
    }
}
