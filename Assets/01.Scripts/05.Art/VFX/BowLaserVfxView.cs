using UnityEngine;

namespace Geumeum.Art
{
    /// <summary>Manual game-clock bow release. The caller supplies the accepted shot pose and cosmetic distance.</summary>
    public sealed class BowLaserVfxView : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float _maxDistance = 18f;
        [SerializeField, Min(0.1f)] private float _duration = 0.8f;
        [SerializeField, Min(1f)] private float _flightSpeed = 30f;
        [SerializeField, Min(.1f)] private float _arrowTrailLength = 2.4f;
        [SerializeField, Min(.01f)] private float _arrivalFade = .08f;
        [SerializeField] private MeshRenderer _arrowTip;
        [SerializeField] private MeshRenderer _tipStar;
        [SerializeField] private ParticleSystem _fineEmbers;
        [SerializeField] private MeshRenderer[] _residueFibers;
        [SerializeField, Min(.01f)] private float _residueLifetime = .22f;
        [HideInInspector, SerializeField, Min(0.01f)] private float _extensionTime = 0.07f;
        [SerializeField, Min(0.001f)] private float _beamRadius = 0.032f;
        [SerializeField, Min(0.01f)] private float _ringRadius = 0.9f;
        [HideInInspector, SerializeField] private Vector2 _beamFadeWindow = new Vector2(0.24f, 0.9f);
        [HideInInspector, SerializeField] private Vector2 _tailDetachWindow = new Vector2(0.55f, 1.1f);
        [HideInInspector, SerializeField] private Vector2 _dissolveWindow = new Vector2(0.54f, 0.98f);
        [HideInInspector, SerializeField] private float _ringSpacing = 0.16f;
        [HideInInspector, SerializeField] private float _ringTravel = 0.18f;
        [SerializeField, ColorUsage(true, true)] private Color _nearRingColor = new Color(4f, 2f, 2.3f, 1f);
        [SerializeField, ColorUsage(true, true)] private Color _farRingColor = new Color(0.55f, 0.8f, 1.5f, 1f);
        [HideInInspector, SerializeField] private float _ringDelay = 0.025f;
        [SerializeField] private Vector2 _ringFadeWindow = new Vector2(0.14f, 0.52f);
        [SerializeField] private float _muzzleIntensity = 180f;
        [SerializeField] private float _flashSize = 0.48f;
        [SerializeField] private MeshRenderer _flash;
        [SerializeField] private MeshRenderer _beam;
        [SerializeField] private MeshRenderer _filament;
        [HideInInspector, SerializeField, Range(0f, 1f)] private float _filamentOpacity = 0.65f;
        [HideInInspector, SerializeField] private Vector2 _lateFilamentFadeWindow = new Vector2(.48f, 1.05f);
        [HideInInspector, SerializeField, ColorUsage(true, true)] private Color _lateFilamentColor = new Color(10f, 6f, 7f, 1f);
        [SerializeField] private MeshRenderer[] _rings;
        [SerializeField] private ParticleSystem _sparks;
        [SerializeField] private ParticleSystem _sparkHeads;
        [SerializeField] private Vector2 _sparkHeadSizeRange = new Vector2(0.055f, 0.12f);
        [SerializeField, Range(0f, 1f)] private float _recoilFraction = 0.42f;
        [SerializeField, ColorUsage(true, true)] private Color _beamCoreColor = new Color(7f, 2.8f, 3.6f, 1f);
        [SerializeField, ColorUsage(true, true)] private Color _beamEdgeColor = new Color(4f, .06f, .18f, 1f);
        [SerializeField, Range(1, 40)] private int _redSparkCount = 36;
        [SerializeField, Range(0, 16)] private int _goldSparkCount = 10;
        [SerializeField, Min(1f)] private float _goldStretchFactor = 1.5f;
        [SerializeField] private Vector2 _sparkSpeedRange = new Vector2(8f, 28f);
        [SerializeField] private Light _muzzleLight;
        private MaterialPropertyBlock _block;
        private ParticleSystem.Particle[] _particles;
        private ParticleSystem.Particle[] _heads;
        private ParticleSystem.Particle[] _embers;
        private uint _variation;
        public void SetVariation(uint seed) => _variation = seed;
        private float RandomValue(int index) => Hash(index + (int)(_variation % 997u));
        private static readonly int _ageId = Shader.PropertyToID("_Age");
        private static readonly int _opacityId = Shader.PropertyToID("_Opacity");
        private static readonly int _dissolveId = Shader.PropertyToID("_Dissolve");
        public float MaxDistance => _maxDistance;
        public float Duration => _duration;

        private void Awake() => Cache();
        private void OnDisable() => ResetVisual();
        private void Cache()
        {
            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
            }

            if (_particles == null)
            {
                _particles = new ParticleSystem.Particle[56];
            }

            if (_heads == null)
            {
                _heads = new ParticleSystem.Particle[56];
            }

            if (_embers == null)
            {
                _embers = new ParticleSystem.Particle[48];
            }
        }
        public void ResetVisual()
        {
            if (_beam != null)
            {
                _beam.enabled = false;
            }

            if (_arrowTip != null)
            {
                _arrowTip.enabled = false;
            }

            if (_tipStar != null)
            {
                _tipStar.enabled = false;
            }

            if (_fineEmbers != null)
            {
                _fineEmbers.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            if (_residueFibers != null)
            {
                foreach (var fiber in _residueFibers)
                {
                    if (fiber != null)
                    {
                        fiber.enabled = false;
                    }
                }
            }

            if (_filament != null)
            {
                _filament.enabled = false;
            }

            if (_flash != null)
            {
                _flash.enabled = false;
            }

            if (_rings != null)
            {
                foreach (var ring in _rings)
                {
                    if (ring != null)
                    {
                        ring.enabled = false;
                    }
                }
            }

            if (_sparks != null)
            {
                _sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            if (_sparkHeads != null)
            {
                _sparkHeads.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            if (_muzzleLight != null)
            {
                _muzzleLight.intensity = 0f;
            }
        }
        public void Evaluate(float elapsed, float distance)
        {
            Cache();
            if (elapsed < 0f || elapsed >= _duration || distance <= 0f)
            {
                ResetVisual();
                return;
            }
            float t = elapsed;
            float endFade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_duration - .18f, _duration, t));
            float limit = Mathf.Min(distance, _maxDistance);
            float arrival = limit / Mathf.Max(1f, _flightSpeed);
            float head = Mathf.Min(limit, t * _flightSpeed);
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(arrival, arrival + _arrivalFade, t));
            float visibleLength = Mathf.Min(_arrowTrailLength, head) * fade;
            float tail = Mathf.Max(0f, head - visibleLength);
            if (_beam != null)
            {
                _beam.enabled = visibleLength > .001f && fade > .001f;
                _beam.transform.localPosition = new Vector3(0f, 0f, tail);
                _beam.transform.localScale = new Vector3(_beamRadius * .72f, _beamRadius * .72f, visibleLength);
                SetShader(_beam, t, fade, 1f - fade);
                _block.SetColor("_CoreColor", _beamCoreColor);
                _block.SetColor("_EdgeColor", _beamEdgeColor);
                _beam.SetPropertyBlock(_block);
            }
            if (_filament != null)
            {
                _filament.enabled = visibleLength > .01f && fade > .001f;
                _filament.transform.localPosition = new Vector3(0f, 0f, tail);
                _filament.transform.localScale = new Vector3(_beamRadius, _beamRadius, visibleLength);
                _filament.transform.localRotation = Quaternion.AngleAxis(RandomValue(821) * 360f + t * 115f, Vector3.forward);
                SetShader(_filament, t + RandomValue(17), fade * .55f, 1f - fade);
                _block.SetColor("_CoreColor", new Color(6f, 2f, 3f, 1f));
                _filament.SetPropertyBlock(_block);
            }
            if (_arrowTip != null)
            {
                float tipLength = Mathf.Min(.38f, head);
                _arrowTip.enabled = fade > .001f && tipLength > .001f;
                _arrowTip.transform.localPosition = new Vector3(0f, 0f, head - tipLength);
                _arrowTip.transform.localScale = new Vector3(.085f, .085f, tipLength);
                SetShader(_arrowTip, t, fade, 1f - fade);
                _block.SetColor("_CoreColor", new Color(10f, 7f, 6f, 1f));
                _block.SetColor("_EdgeColor", new Color(5f, .35f, .28f, 1f));
                _arrowTip.SetPropertyBlock(_block);
            }
            if (_tipStar != null)
            {
                _tipStar.enabled = fade > .001f && t > .025f;
                _tipStar.transform.localPosition = new Vector3(0f, 0f, Mathf.Max(0f, head - .18f));
                _tipStar.transform.localRotation = Quaternion.AngleAxis(RandomValue(29) * 360f + t * 240f, Vector3.forward);
                _tipStar.transform.localScale = Vector3.one * Mathf.Min(.12f + .035f * Mathf.Sin(t * 81f), head * .3f) * fade;
                SetShader(_tipStar, t, fade * .8f, 0f);
                _block.SetColor("_CoreColor", new Color(7f, 5f, 1.7f, 1f));
                _block.SetColor("_EdgeColor", new Color(3f, 1.4f, .1f, 1f));
                _tipStar.SetPropertyBlock(_block);
            }
            if (_residueFibers != null)
            {
                for (int i = 0; i < _residueFibers.Length; ++i)
                {
                    var fiber = _residueFibers[i];
                    if (fiber == null)
                    {
                        continue;
                    }

                    float residueAge = t - arrival;
                    float residueFade = (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.01f, _arrivalFade + _residueLifetime, residueAge))) * endFade;
                    float residueGrow = Mathf.Clamp01(residueAge / .025f);
                    fiber.enabled = residueAge >= 0f && residueFade > .002f && residueGrow > 0f;
                    fiber.transform.localPosition = new Vector3(0f, 0f, Mathf.Max(0f, limit - _arrowTrailLength));
                    float tremor = .052f * (1f + .15f * Mathf.Sin(t * (23f + i * 7f) + RandomValue(i + 501) * 6f));
                    fiber.transform.localScale = new Vector3(tremor, .052f * (1f + .13f * Mathf.Sin(t * 31f + i * 1.7f)), Mathf.Min(limit, _arrowTrailLength));
                    fiber.transform.localRotation = Quaternion.AngleAxis(RandomValue(47) * 360f + i * 9f + Mathf.Sin(t * (18f + i * 3f) + RandomValue(i + 831) * 6f) * 6f, Vector3.forward);
                    SetShader(fiber, t * 2.3f + i * .19f, residueGrow * residueFade * (i == 0 ? .60f : .16f) * (.7f + .3f * Mathf.Sin(t * (51f + i * 13f) + RandomValue(i + 79) * 6f)), Mathf.Clamp01(residueAge / (_arrivalFade + _residueLifetime)) * .78f);
                    _block.SetColor("_CoreColor", i == 0 ? new Color(5f, 2.7f, 2.8f, 1f) : new Color(2.8f, .7f, 1.1f, 1f));
                    _block.SetColor("_EdgeColor", new Color(1.8f, .025f, .065f, 1f));
                    fiber.SetPropertyBlock(_block);
                }
            }

            if (_rings != null)
            {
                for (int i = 0; i < _rings.Length; ++i)
                {
                    var ring = _rings[i];
                    if (ring == null)
                    {
                        continue;
                    }

                    float z = (.025f + i * .145f) * limit;
                    float age = t - z / Mathf.Max(1f, _flightSpeed);
                    float alpha = endFade * Mathf.Clamp01(age / 0.035f) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_ringFadeWindow.x, _ringFadeWindow.y, age)));
                    ring.enabled = age >= 0f && alpha > 0.001f && z <= head;
                    float radius = _ringRadius * (1f / (1f + i * 0.85f)) * (0.65f + age * 2.8f) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / .13f));
                    ring.transform.localPosition = new Vector3(0f, 0f, z);
                    ring.transform.localScale = Vector3.one * radius;
                    ring.transform.localRotation = Quaternion.AngleAxis(i * 67f + RandomValue(i + 73) * 120f + age * (i % 2 == 0 ? 25f : -33f), Vector3.forward);
                    SetShader(ring, age + i * 0.27f, alpha, Mathf.InverseLerp(0.25f, 0.52f, age));
                    _block.SetColor("_CoreColor", i == 0 ? _nearRingColor : _farRingColor);
                    _block.SetColor("_EdgeColor", i == 0 ? new Color(2.8f, .08f, .2f, 1f) : _farRingColor * 0.4f);
                    ring.SetPropertyBlock(_block);
                }
            }

            if (_flash != null)
            {
                float flash = Mathf.Exp(-t * 32f);
                _flash.enabled = t < .09f;
                _flash.transform.localScale = Vector3.one * _flashSize * (0.6f + t * 3f);
                SetShader(_flash, t, flash, 0f);
            }
            DrawSparks(t, head, limit);
            DrawFineEmbers(t, head, limit);
            if (_muzzleLight != null)
            {
                _muzzleLight.intensity = _muzzleIntensity * Mathf.Exp(-t * 22f);
            }
        }
        private void SetShader(MeshRenderer renderer, float time, float opacity, float dissolve)
        {
            _block.Clear();
            _block.SetFloat(_ageId, time);
            _block.SetFloat(_opacityId, opacity);
            _block.SetFloat(_dissolveId, dissolve);
            renderer.SetPropertyBlock(_block);
        }
        private void DrawSparks(float time, float length, float distance)
        {
            if (_sparks == null)
            {
                return;
            }

            _sparks.Pause(true);
            int count = 0;
            int redCount = Mathf.Clamp(_redSparkCount, 1, 40);
            int total = redCount + Mathf.Clamp(_goldSparkCount, 0, 16);
            for (int i = 0; i < total; ++i)
            {
                float seed = RandomValue(i + 1);
                float spread = RandomValue(i + 73);
                float lifetime = RandomValue(i + 133);
                bool gold = i >= redCount;
                float delay = gold ? 0.015f + spread * 0.06f : -0.025f + spread * 0.10f;
                float age = time - delay;
                float life = gold ? 0.16f + lifetime * 0.23f : 0.12f + lifetime * 0.38f;
                if (age < 0f || age >= life)
                {
                    continue;
                }

                float angle = RandomValue(i + 191) * Mathf.PI * 2f;
                var radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                float speed = Mathf.Lerp(_sparkSpeedRange.x, _sparkSpeedRange.y, seed * seed);
                bool recoil = !gold && i < redCount * _recoilFraction;
                var velocity = gold
                    ? new Vector3((spread - .5f) * 3f, .1f + (lifetime - .5f) * .8f, 5f + seed * 6f)
                    : radial * speed + Vector3.forward * (spread * 9f - 4f);
                if (recoil)
                {
                    velocity = new Vector3((spread - .5f) * 7f, (lifetime - .3f) * 7f, -7f - seed * 10f);
                }
                else if (!gold)
                {
                    velocity *= .8f;
                }

                var position = velocity * age;
                if (gold)
                {
                    int goldIndex = i - redCount;
                    float lane = (float)goldIndex / Mathf.Max(1, total - redCount - 1);
                    position += new Vector3((seed - .5f) * 1.1f, .25f + Mathf.Sin(goldIndex * 2.1f) * 1.65f, length * (-.12f + lane * .95f));
                }
                position.z = Mathf.Clamp(position.z, (recoil || gold) ? -Mathf.Min(2.3f, distance * .3f) : 0f, Mathf.Max(0f, distance - 0.02f));
                float alpha = Mathf.Pow(1f - age / life, .8f) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.27f, .44f, time)));
                var color = gold ? new Color(1f, .8f, .3f, alpha) : new Color(1f, .35f + seed * .2f, .42f + seed * .18f, alpha);
                _particles[count] = new ParticleSystem.Particle { position = position, velocity = velocity * (gold ? _goldStretchFactor : 1.1f), startColor = color, startSize = (.025f + seed * .045f) * (gold ? .75f : 1f), startLifetime = 1f, remainingLifetime = 1f, randomSeed = (uint)(i + 1) };
                float headAlpha = alpha * Mathf.Lerp(.2f, .7f, RandomValue(i + 317));
                var headColor = gold ? new Color(1f, .78f, .22f, headAlpha) : new Color(1f, .18f, .26f, headAlpha);
                _heads[count] = new ParticleSystem.Particle { position = position, velocity = Vector3.zero, startColor = headColor, startSize = Mathf.Lerp(_sparkHeadSizeRange.x, _sparkHeadSizeRange.y, seed) * (gold ? .7f : 1f), startLifetime = 1f, remainingLifetime = 1f, randomSeed = (uint)(i + 1) };
                ++count;
            }
            _sparks.SetParticles(_particles, count);
            if (_sparkHeads != null)
            {
                _sparkHeads.Pause(true);
                _sparkHeads.SetParticles(_heads, count);
            }
        }
        private void DrawFineEmbers(float time, float head, float limit)
        {
            if (_fineEmbers == null)
            {
                return;
            }

            _fineEmbers.Pause(true);
            int count = 0;
            for (int i = 0; i < _embers.Length; ++i)
            {
                float seed = RandomValue(i + 501);
                float spread = RandomValue(i + 619);
                bool nearBow = i < 24;
                float z = nearBow ? 0f : limit * (.05f + .78f * seed);
                float age = time - z / Mathf.Max(1f, _flightSpeed) - spread * .035f;
                float life = .16f + seed * .2f;
                if (age < 0f || age >= life || z > head)
                {
                    continue;
                }

                float angle = RandomValue(i + 701) * Mathf.PI * 2f;
                var radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                float radius = nearBow ? age * (1f + seed * 5f) : .16f + age * (1f + spread * 2f);
                var position = radial * radius + Vector3.forward * Mathf.Clamp(z - age * seed * 2f, -Mathf.Min(.6f, limit * .1f), limit);
                float alpha = (1f - age / life) * (.35f + .65f * spread) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_duration - .18f, _duration, time)));
                _embers[count++] = new ParticleSystem.Particle { position = position, velocity = radial * (nearBow ? 2f : .6f), startColor = nearBow ? new Color(1f, .15f, .25f, alpha) : new Color(.45f, .6f, 1f, alpha), startSize = .012f + seed * .02f, startLifetime = 1f, remainingLifetime = 1f, randomSeed = (uint)(i + 1) };
            }
            _fineEmbers.SetParticles(_embers, count);
        }
        internal static float Hash(int value) => Mathf.Repeat(Mathf.Sin(value * 127.1f) * 43758.5453f, 1f);
    }
}
