// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// Project
using Geumeum.Common;

namespace Geumeum.Art
{
    /// <summary>두 번 교차하는 지면 할퀴기. 로컬 +Z가 공격 방향이며 판정은 포함하지 않는다.</summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class BossDoubleScratchVfx : MonoBehaviour
    {
        [Header("재질")]
        [SerializeField] private Material _brightMaterial;
        [SerializeField] private Material _darkMaterial;
        [SerializeField] private Material _smokeMaterial;
        [SerializeField] private Light _glowLight;

        [Header("타이밍")]
        [SerializeField] private bool _playOnEnable = true;
        [SerializeField, Min(0.05f)] private float _secondStrikeDelay = 0.817f;
        [SerializeField, Min(0.05f)] private float _strikeLifetime = 0.34f;
        [SerializeField, Min(0.01f)] private float _sweepSeconds = 0.045f;
        [SerializeField, Min(0.05f)] private float _afterglowLifetime = 0.62f;
        [SerializeField, Min(0.05f)] private float _emberLifetime = 0.95f;
        [SerializeField, Min(0.05f)] private float _debrisLifetime = 1.3f;

        [Header("형태")]
        [SerializeField, Min(0.5f)] private float _bandRadius = 16f;
        [SerializeField, Min(0.1f)] private float _bandThickness = 2.6f;
        [SerializeField, Range(0, 8)] private int _bodyCount = 3;
        [SerializeField, Range(0, 6)] private int _coreCount = 3;
        [SerializeField, Range(0, 40)] private int _sprayCount = 18;
        [SerializeField, Range(40f, 300f)] private float _arcDegrees = 205f;
        [SerializeField, Range(4, 64)] private int _filamentCount = 34;
        [SerializeField, Range(0, 40)] private int _darkFilamentCount = 16;
        [SerializeField, Range(0, 12)] private int _hazeCount = 6;
        [SerializeField, Range(0, 8)] private int _scarCount = 4;
        [SerializeField, Range(0, 400)] private int _emberCount = 190;
        [SerializeField, Range(0, 80)] private int _debrisCount = 26;
        [SerializeField, Range(0, 40)] private int _smokeCount = 18;
        [SerializeField] private float _groundHeight = 0.04f;
        [SerializeField, Min(0.1f)] private float _scale = 1f;
        [SerializeField] private int _seed = 20260922;

        [Header("층별 세기")]
        [SerializeField, Range(0f, 4f)] private float _hazeIntensity = 0.5f;
        [SerializeField, Range(0f, 4f)] private float _bodyIntensity = 1.4f;
        [SerializeField, Range(0f, 4f)] private float _coreIntensity = 1.6f;
        [SerializeField, Range(0f, 4f)] private float _filamentIntensity = 1f;
        [SerializeField, Range(0f, 4f)] private float _darkIntensity = 1f;
        [SerializeField, Range(0f, 4f)] private float _scarIntensity = 1.3f;
        [SerializeField, Range(0f, 4f)] private float _emberIntensity = 1f;

        [Header("빛")]
        [SerializeField, Min(0f)] private float _lightIntensity = 26f;
        [SerializeField, Min(0f)] private float _lightRange = 16f;

        private const int ARC_SAMPLES = 42;
        private const int ATLAS_ROWS = 11;     // 0~7 줄무늬, 8 파편, 9 몸통, 10 헤이즈
        private const int DEBRIS_CELLS = 8;
        private const int SUBMESH_BRIGHT = 0;
        private const int SUBMESH_DARK = 1;
        private const int SUBMESH_SMOKE = 2;

        private readonly List<Vector3> _vertices = new List<Vector3>(16000);
        private readonly List<Vector3> _tangents = new List<Vector3>(16000);
        private readonly List<Vector2> _uvs = new List<Vector2>(16000);
        private readonly List<Vector2> _shape = new List<Vector2>(16000);
        private readonly List<Vector2> _life = new List<Vector2>(16000);
        private readonly List<Color> _colors = new List<Color>(16000);
        private readonly List<int>[] _indices =
        {
            new List<int>(24000), new List<int>(12000), new List<int>(2000)
        };

        private float _pathAmp1, _pathAmp2, _pathFreq1, _pathFreq2, _pathPhase1, _pathPhase2;

        private Mesh _mesh;
        private MeshRenderer _renderer;
        private float _elapsed;
        private bool _playing;

        public float Duration => _secondStrikeDelay +
            Mathf.Max(Mathf.Max(_strikeLifetime, _afterglowLifetime),
                Mathf.Max(_emberLifetime, _debrisLifetime));

        public float SecondStrikeDelay => _secondStrikeDelay;
        public bool IsPlaying => _playing;
        public int VisibleVertexCount => _renderer != null && _renderer.enabled ? _vertices.Count : 0;

        private void OnEnable()
        {
            if (_playOnEnable && Application.IsPlaying(gameObject))
            {
                Play();
            }
        }

        private void Update()
        {
            if (!Application.IsPlaying(gameObject) || !_playing || RunTimeLocator.Current.IsPaused)
            {
                return;
            }
            _elapsed += Time.deltaTime;
            Sample(_elapsed);
            if (_elapsed >= Duration)
            {
                _playing = false;
            }
        }

        public void Play()
        {
            _elapsed = 0f;
            _playing = true;
            Sample(0f);
        }

        public void Stop()
        {
            _playing = false;
            _elapsed = 0f;
            if (_renderer != null)
            {
                _renderer.enabled = false;
            }
            if (_glowLight != null)
            {
                _glowLight.intensity = 0f;
            }
        }

        /// <summary>시간별 비교 캡처와 늦게 도착한 표현의 위치 맞춤에 사용하는 결정적 샘플.</summary>
        public void Sample(float seconds)
        {
            EnsureMesh();
            ClearBuffers();

            if (seconds >= 0f)
            {
                BuildStrike(seconds, 0);
                BuildStrike(seconds - _secondStrikeDelay, 1);
            }

            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetNormals(_tangents);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetUVs(1, _shape);
            _mesh.SetUVs(2, _life);
            _mesh.SetColors(_colors);
            _mesh.subMeshCount = 3;
            for (int i = 0; i < 3; i++)
            {
                _mesh.SetTriangles(_indices[i], i, false);
            }
            _mesh.RecalculateBounds();
            _renderer.enabled = _vertices.Count > 0;

            if (_glowLight != null)
            {
                _glowLight.range = _lightRange * _scale;
                _glowLight.intensity = LightIntensity(seconds) + LightIntensity(seconds - _secondStrikeDelay);
            }
        }

        private float LightIntensity(float age)
        {
            float window = _strikeLifetime * 1.6f;
            if (age < 0f || age >= window)
            {
                return 0f;
            }
            float t = age / window;
            float attack = Mathf.Clamp01(age / 0.03f);
            return _lightIntensity * attack * Mathf.Pow(1f - t, 2.2f);
        }

        private void ClearBuffers()
        {
            _vertices.Clear();
            _tangents.Clear();
            _uvs.Clear();
            _shape.Clear();
            _life.Clear();
            _colors.Clear();
            for (int i = 0; i < _indices.Length; i++)
            {
                _indices[i].Clear();
            }
        }

        private void EnsureMesh()
        {
            if (_mesh != null && _renderer != null)
            {
                return;
            }

            Transform existing = transform.Find("Scratch layers");
            GameObject child = existing != null ? existing.gameObject : null;
            if (child == null)
            {
                child = new GameObject("Scratch layers") { hideFlags = HideFlags.DontSave };
                child.transform.SetParent(transform, false);
            }
            child.layer = gameObject.layer;

            MeshFilter filter = child.GetComponent<MeshFilter>();
            if (filter == null)
            {
                filter = child.AddComponent<MeshFilter>();
            }
            _renderer = child.GetComponent<MeshRenderer>();
            if (_renderer == null)
            {
                _renderer = child.AddComponent<MeshRenderer>();
            }
            _renderer.sharedMaterials = new[] { _brightMaterial, _darkMaterial, _smokeMaterial };
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            _mesh = new Mesh { name = "Boss double scratch (runtime)" };
            _mesh.MarkDynamic();
            _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            filter.sharedMesh = _mesh;
        }

        // ------------------------------------------------------------------ 한 번의 타격

        private void BuildStrike(float age, int strike)
        {
            if (age < 0f)
            {
                return;
            }
            bool mirrored = strike == 1;
            float dir = mirrored ? -1f : 1f;
            float baseAngle = mirrored ? 14f * Mathf.Deg2Rad : -9f * Mathf.Deg2Rad;
            int seed = _seed + strike * 7919;
            _pathAmp1 = 0.045f + 0.05f * Hash(seed, 0, 301);
            _pathAmp2 = 0.018f + 0.03f * Hash(seed, 0, 302);
            _pathFreq1 = 0.7f + 0.6f * Hash(seed, 0, 303);
            _pathFreq2 = 1.7f + 1.4f * Hash(seed, 0, 304);
            _pathPhase1 = Hash(seed, 0, 305) * 6.2831f;
            _pathPhase2 = Hash(seed, 0, 306) * 6.2831f;

            BuildHaze(age, seed, dir, baseAngle);
            BuildBody(age, seed, dir, baseAngle);
            BuildFilaments(age, seed, dir, baseAngle);
            BuildCore(age, seed, dir, baseAngle);
            BuildSpray(age, seed, dir, baseAngle);
            BuildScar(age, seed, dir, baseAngle);
            BuildDarkFilaments(age, seed, dir, baseAngle);
            BuildSmoke(age, seed, dir, baseAngle);
            BuildEmbers(age, seed, dir, baseAngle);
            BuildDebris(age, seed, dir, baseAngle);
        }

        /// <summary>띠를 감싸는 넓고 흐린 붉은 번짐. 일부는 카메라를 향해 세워 부피감을 준다.</summary>
        private void BuildHaze(float age, int seed, float dir, float baseAngle)
        {
            for (int i = 0; i < _hazeCount; i++)
            {
                float r1 = Hash(seed, i, 11);
                float r2 = Hash(seed, i, 12);
                float life = _strikeLifetime * (0.8f + 0.5f * r1);
                if (age >= life)
                {
                    continue;
                }

                float t = age / life;
                float fade = Attack(age) * Mathf.Pow(1f - t, 1.7f) * _hazeIntensity;
                float radius = BandRadius((r2 - 0.5f) * 1.1f) * (1f + 0.16f * t);
                float span = _arcDegrees * (0.72f + 0.28f * r1) * Mathf.Deg2Rad;

                AddArcRibbon(seed * 31 + i, SUBMESH_BRIGHT, radius, span, baseAngle + (r1 - 0.5f) * 0.14f,
                    _groundHeight + 0.04f + r2 * 0.22f, 3.4f + 5.2f * r2, i % 3 == 0 ? 0.4f : 1f,
                    10, fade, 0.05f, t * 0.5f, dir, age, 0.3f + 0.25f * r1);
            }
        }

        /// <summary>띠 전체의 몸통. 붉은 덩어리감을 만든다.</summary>
        private void BuildBody(float age, int seed, float dir, float baseAngle)
        {
            for (int i = 0; i < _bodyCount; i++)
            {
                float r1 = Hash(seed, i, 15);
                float r2 = Hash(seed, i, 16);
                float life = _strikeLifetime * (0.85f + 0.4f * r1);
                if (age >= life)
                {
                    continue;
                }

                float t = age / life;
                float fade = Attack(age) * Mathf.Pow(1f - t, 1.3f) * _bodyIntensity;
                float radius = BandRadius((r1 - 0.5f) * 0.7f) * (1f + 0.12f * t);
                float span = _arcDegrees * (0.82f + 0.18f * r2) * Mathf.Deg2Rad;

                AddArcRibbon(seed * 37 + i, SUBMESH_BRIGHT, radius, span,
                    baseAngle + (r2 - 0.5f) * 0.10f, _groundHeight + 0.03f + i * 0.02f,
                    1.5f + 1.9f * r2, 1f, 9, fade, 0.35f,
                    Mathf.Pow(t, 0.9f) * 0.85f, dir, age, 0.22f + 0.2f * r1);
            }
        }

        /// <summary>띠 한가운데를 지나는 흰 심지.</summary>
        private void BuildCore(float age, int seed, float dir, float baseAngle)
        {
            for (int i = 0; i < _coreCount; i++)
            {
                float r1 = Hash(seed, i, 17);
                float r2 = Hash(seed, i, 18);
                float life = _strikeLifetime * (0.6f + 0.5f * r1);
                if (age >= life)
                {
                    continue;
                }

                float t = age / life;
                float fade = Attack(age) * Mathf.Pow(1f - t, 1.5f) * _coreIntensity;
                float radius = BandRadius((r1 - 0.5f) * 0.45f) * (1f + 0.1f * t);
                float span = _arcDegrees * (0.55f + 0.42f * r2) * Mathf.Deg2Rad;

                AddArcRibbon(seed * 43 + i, SUBMESH_BRIGHT, radius, span,
                    baseAngle + (r2 - 0.5f) * 0.14f, _groundHeight + 0.06f + i * 0.02f,
                    0.26f + 0.55f * r2, 1f, 4 + i % 3, fade, 2.4f,
                    Mathf.Pow(t, 0.8f), dir, age, 0.2f + 0.2f * r1);
            }
        }

        /// <summary>흰 코어를 가진 밝은 발톱 가닥.</summary>
        private void BuildFilaments(float age, int seed, float dir, float baseAngle)
        {
            for (int i = 0; i < _filamentCount; i++)
            {
                float r1 = Hash(seed, i, 21);
                float r2 = Hash(seed, i, 22);
                float r3 = Hash(seed, i, 23);
                float r4 = Hash(seed, i, 24);

                float delay = r1 * 0.03f;
                float life = _strikeLifetime * (0.55f + 0.85f * r2);
                float a = age - delay;
                if (a < 0f || a >= life)
                {
                    continue;
                }

                float t = a / life;
                float fade = Attack(a) * Mathf.Pow(1f - t, 1.25f) * _filamentIntensity;
                float u = i / Mathf.Max(1f, _filamentCount - 1f);
                float g = (r3 - 0.5f) * 2f;
                float radius = BandRadius(g * Mathf.Abs(g)) * (1f + 0.14f * t);
                float span = _arcDegrees * (0.12f + 0.88f * Mathf.Pow(r3, 1.6f)) * Mathf.Deg2Rad;
                float offset = baseAngle + (r4 - 0.5f) * _arcDegrees * 0.34f * Mathf.Deg2Rad;
                float height = _groundHeight + Mathf.Pow(r2, 2.4f) * 0.34f;
                float width = (0.045f + 0.30f * r4) * (1f + 0.3f * t);
                float flat = r1 < 0.80f ? 1f : 0.3f;
                float core = 0.55f + 0.85f * (1f - Mathf.Abs(u - 0.42f));

                AddArcRibbon(seed * 53 + i, SUBMESH_BRIGHT, radius, span, offset, height, width, flat,
                    i % 8, fade, core, Mathf.Pow(t, 0.85f), dir, a, 0.12f + 0.22f * r2);
            }
        }

        /// <summary>띠 양 끝에서 바깥으로 뻗는 가느다란 분사.</summary>
        private void BuildSpray(float age, int seed, float dir, float baseAngle)
        {
            for (int i = 0; i < _sprayCount; i++)
            {
                float r1 = Hash(seed, i, 19);
                float r2 = Hash(seed, i, 25);
                float r3 = Hash(seed, i, 26);
                float life = _strikeLifetime * (0.45f + 0.6f * r1);
                if (age >= life)
                {
                    continue;
                }

                float t = age / life;
                float fade = Attack(age) * Mathf.Pow(1f - t, 1.4f) * _filamentIntensity * 0.9f;
                float end = i % 2 == 0 ? 0.5f : -0.5f;
                float span = _arcDegrees * (0.06f + 0.22f * r2) * Mathf.Deg2Rad;
                float offset = baseAngle + end * _arcDegrees * (0.42f + 0.32f * r3) * Mathf.Deg2Rad;
                float radius = BandRadius((r1 - 0.5f) * 3.2f) * (1f + 0.3f * t);

                AddArcRibbon(seed * 113 + i, SUBMESH_BRIGHT, radius, span, offset,
                    _groundHeight + 0.02f + r2 * 0.4f, 0.035f + 0.11f * r3, r3 < 0.6f ? 1f : 0.2f,
                    i % 8, fade, 1.5f, Mathf.Pow(t, 0.8f), dir, age, 0.3f);
            }
        }

        /// <summary>가닥이 사라진 뒤 지면에 남는 긴 잔광 선.</summary>
        private void BuildScar(float age, int seed, float dir, float baseAngle)
        {
            if (age >= _afterglowLifetime)
            {
                return;
            }
            for (int i = 0; i < _scarCount; i++)
            {
                float r1 = Hash(seed, i, 31);
                float r2 = Hash(seed, i, 32);
                float t = age / _afterglowLifetime;
                float fade = Attack(age) * Mathf.Pow(1f - t, 1.1f) * _scarIntensity;
                float radius = BandRadius((r1 - 0.5f) * 0.5f);
                float span = _arcDegrees * (0.55f + 0.4f * r2) * Mathf.Deg2Rad;

                AddArcRibbon(seed * 71 + i, SUBMESH_BRIGHT, radius, span,
                    baseAngle + (r2 - 0.5f) * 0.18f, _groundHeight + 0.012f + i * 0.004f,
                    0.05f + 0.15f * r1, 1f, 4 + i % 4, fade, 1.35f, Mathf.Pow(t, 1.6f) * 0.75f,
                    dir, age, 0.06f);
            }
        }

        /// <summary>밝은 가닥 위에 겹치는 어두운 핏빛 줄기.</summary>
        private void BuildDarkFilaments(float age, int seed, float dir, float baseAngle)
        {
            for (int i = 0; i < _darkFilamentCount; i++)
            {
                float r1 = Hash(seed, i, 41);
                float r2 = Hash(seed, i, 42);
                float r3 = Hash(seed, i, 43);

                float life = _strikeLifetime * (1.0f + 0.9f * r1);
                if (age >= life)
                {
                    continue;
                }

                float t = age / life;
                float fade = Attack(age) * Mathf.Pow(1f - t, 1.1f) * _darkIntensity;
                float radius = BandRadius((r2 - 0.5f) * 1.9f) * (1f + 0.10f * t);
                float span = _arcDegrees * (0.28f + 0.55f * r2) * Mathf.Deg2Rad;
                float offset = baseAngle + (r3 - 0.5f) * _arcDegrees * 0.3f * Mathf.Deg2Rad;

                AddArcRibbon(seed * 97 + i, SUBMESH_DARK, radius, span, offset,
                    _groundHeight + 0.02f + r1 * 0.22f, 0.05f + 0.22f * r2, r3 < 0.7f ? 1f : 0.25f,
                    i % 8, fade, 0.2f, Mathf.Pow(t, 1.1f), dir, age, 0.1f + 0.2f * r1);
            }
        }

        /// <summary>바닥에서 피어오르는 어두운 연기.</summary>
        private void BuildSmoke(float age, int seed, float dir, float baseAngle)
        {
            for (int i = 0; i < _smokeCount; i++)
            {
                float r1 = Hash(seed, i, 51);
                float r2 = Hash(seed, i, 52);
                float r3 = Hash(seed, i, 53);
                float life = _afterglowLifetime * (0.8f + 0.9f * r1);
                float a = age - r2 * 0.08f;
                if (a < 0f || a >= life)
                {
                    continue;
                }

                float t = a / life;
                float angle = baseAngle + dir * (r2 - 0.5f) * _arcDegrees * Mathf.Deg2Rad;
                float radius = BandRadius((r3 - 0.5f) * 1.7f);
                Vector3 p = new Vector3(Mathf.Sin(angle) * radius,
                    _groundHeight + 0.2f + t * (0.8f + 1.4f * r1), Mathf.Cos(angle) * radius) * _scale;
                float size = (0.9f + 2.2f * r3) * (0.55f + 0.9f * t) * _scale;
                float fade = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * (0.35f + 0.3f * r1);

                AddCameraQuad(SUBMESH_SMOKE, p, size,
                    (r1 - 0.5f) * 4f + t * (r2 - 0.5f) * 3f, 0f, 1f, fade, 0.2f, t * 0.35f, ATLAS_ROWS);
            }
        }

        /// <summary>튀는 불티. 속도 방향으로 늘어난다.</summary>
        private void BuildEmbers(float age, int seed, float dir, float baseAngle)
        {
            for (int i = 0; i < _emberCount; i++)
            {
                float r1 = Hash(seed, i, 61);
                float r2 = Hash(seed, i, 62);
                float r3 = Hash(seed, i, 63);
                float r4 = Hash(seed, i, 64);

                float born = r1 * _sweepSeconds;
                float life = _emberLifetime * (0.35f + 0.85f * r2);
                float a = age - born;
                if (a < 0f || a >= life)
                {
                    continue;
                }

                float t = a / life;
                float angle = baseAngle + dir * (r1 - 0.5f) * _arcDegrees * Mathf.Deg2Rad;
                float radius = BandRadius((r3 - 0.5f) * 1.5f);
                Vector3 origin = new Vector3(Mathf.Sin(angle) * radius,
                    _groundHeight + 0.1f + r4 * 0.4f, Mathf.Cos(angle) * radius);

                Vector3 outward = new Vector3(origin.x, 0f, origin.z).normalized;
                Vector3 velocity = outward * (1.2f + 5.5f * r4) + Vector3.up * (1.6f + 5.2f * r2);
                velocity += new Vector3(r2 - 0.5f, 0f, r3 - 0.5f) * 2.4f;

                float drag = Mathf.Exp(-1.55f * a);
                Vector3 p = (origin + velocity * ((1f - drag) / 1.55f) + Vector3.down * (6.2f * a * a)) * _scale;
                if (p.y < _groundHeight * _scale)
                {
                    p.y = _groundHeight * _scale;
                }

                Vector3 vel = velocity * drag + Vector3.down * (12.4f * a);
                float speed = vel.magnitude;
                float len = Mathf.Clamp(speed * 0.028f, 0.05f, 0.5f) * _scale;
                float size = (0.026f + 0.05f * r3) * _scale * (1f - 0.4f * t);
                float fade = Mathf.Pow(1f - t, 0.8f) * (0.75f + 0.5f * r4) * _emberIntensity;

                AddStretchedQuad(SUBMESH_BRIGHT, p, speed > 1e-3f ? vel / speed : Vector3.up,
                    len, size, i % 8, fade, 1.6f, t * 0.3f);
            }
        }

        /// <summary>깨져 날아가는 지면 파편.</summary>
        private void BuildDebris(float age, int seed, float dir, float baseAngle)
        {
            for (int i = 0; i < _debrisCount; i++)
            {
                float r1 = Hash(seed, i, 71);
                float r2 = Hash(seed, i, 72);
                float r3 = Hash(seed, i, 73);
                float r4 = Hash(seed, i, 74);

                float life = _debrisLifetime * (0.6f + 0.6f * r1);
                if (age >= life)
                {
                    continue;
                }

                float t = age / life;
                float angle = baseAngle + dir * (r2 - 0.5f) * _arcDegrees * Mathf.Deg2Rad;
                float radius = BandRadius((r3 - 0.5f) * 1.6f);
                Vector3 origin = new Vector3(Mathf.Sin(angle) * radius, _groundHeight,
                    Mathf.Cos(angle) * radius);
                Vector3 outward = new Vector3(origin.x, 0f, origin.z).normalized;
                Vector3 velocity = outward * (0.8f + 3.4f * r4) + Vector3.up * (2.2f + 4.4f * r1);

                Vector3 p = (origin + velocity * age + Vector3.down * (4.905f * age * age)) * _scale;
                float floor = _groundHeight * _scale;
                if (p.y < floor)
                {
                    p.y = floor;
                }

                float size = (0.022f + 0.075f * r3) * _scale;
                float spin = (r4 - 0.5f) * 14f * age + r2 * 6.283f;
                float fade = Mathf.Clamp01(1f - Mathf.Pow(t, 4f)) * (0.7f + 0.3f * r1);

                AddCameraQuad(SUBMESH_DARK, p, size, spin,
                    (i % DEBRIS_CELLS) / (float)DEBRIS_CELLS, 1f / DEBRIS_CELLS, fade, 0.1f, 0f, 8);
            }
        }

        // ------------------------------------------------------------------ 기하 생성

        /// <summary>띠 두께 안에서의 반경. g는 -1~1.</summary>
        private float BandRadius(float g) => _bandRadius + Mathf.Clamp(g, -1.6f, 1.6f) * _bandThickness * 0.5f;

        private float Attack(float age) => Mathf.Clamp01(age / Mathf.Max(0.005f, _sweepSeconds * 0.45f));

        /// <summary>지면 위 호를 따라가는 리본. 스윕이 진행된 구간만 그린다.</summary>
        private void AddArcRibbon(int seed, int submesh, float radius, float span, float offset,
            float height, float halfWidth, float flatness, int atlasRow, float intensity,
            float core, float erode, float dir, float age, float wobble)
        {
            if (intensity <= 0.002f || halfWidth <= 0f)
            {
                return;
            }

            float head = Mathf.Clamp01(age / Mathf.Max(0.004f, _sweepSeconds));
            if (head <= 0.001f)
            {
                return;
            }

            float wobFreq = (0.5f + Hash(seed, 0, 81) * 1.6f) * Mathf.Max(0.35f, span / 1.3f);
            float wobPhase = Hash(seed, 0, 82) * 6.2831f;
            float lift = Hash(seed, 0, 83) * 0.5f;
            float phase = Hash(seed, 0, 84);
            float v0 = atlasRow / (float)ATLAS_ROWS;
            float v1 = (atlasRow + 1f) / ATLAS_ROWS;
            Color tint = new Color(core, 0f, 0f, intensity);

            int first = _vertices.Count;
            int emitted = 0;
            for (int k = 0; k <= ARC_SAMPLES; k++)
            {
                float u = k / (float)ARC_SAMPLES;
                if (u > head)
                {
                    break;
                }

                float su = dir > 0f ? u : 1f - u;
                float ang = offset + (su - 0.5f) * span;
                float master = _pathAmp1 * Mathf.Sin(u * _pathFreq1 * 6.2831f + _pathPhase1)
                             + _pathAmp2 * Mathf.Sin(u * _pathFreq2 * 6.2831f + _pathPhase2);
                float local = wobble * 0.035f * Mathf.Sin(u * wobFreq * 6.2831f + wobPhase);
                float rr = radius * (1f + master + local);
                float y = height + lift * Mathf.Sin(u * Mathf.PI) * 0.4f;
                Vector3 p = new Vector3(Mathf.Sin(ang) * rr, y, Mathf.Cos(ang) * rr) * _scale;

                float ang2 = offset + (su + (dir > 0f ? 0.004f : -0.004f) - 0.5f) * span;
                Vector3 nxt = new Vector3(Mathf.Sin(ang2) * rr, y, Mathf.Cos(ang2) * rr) * _scale;
                Vector3 delta = nxt - p;
                Vector3 tangent = delta.sqrMagnitude > 1e-10f ? delta.normalized : Vector3.right;

                float w = halfWidth * _scale;
                AddVertex(p, tangent, new Vector2(u, v0), new Vector2(-w, flatness),
                    new Vector2(phase, erode), tint);
                AddVertex(p, tangent, new Vector2(u, v1), new Vector2(w, flatness),
                    new Vector2(phase, erode), tint);
                emitted++;
            }

            List<int> tris = _indices[submesh];
            for (int k = 0; k < emitted - 1; k++)
            {
                int a = first + k * 2;
                tris.Add(a); tris.Add(a + 1); tris.Add(a + 3);
                tris.Add(a); tris.Add(a + 3); tris.Add(a + 2);
            }
        }

        /// <summary>속도 방향으로 늘어난 짧은 리본 하나.</summary>
        private void AddStretchedQuad(int submesh, Vector3 centre, Vector3 direction, float length,
            float halfWidth, int atlasRow, float intensity, float core, float erode)
        {
            if (intensity <= 0.002f)
            {
                return;
            }
            float v0 = atlasRow / (float)ATLAS_ROWS;
            float v1 = (atlasRow + 1f) / ATLAS_ROWS;
            Color tint = new Color(core, 0f, 0f, intensity);
            Vector3 tail = centre - direction * length;
            Vector2 life = new Vector2(Hash(atlasRow * 13 + 3, 0, 91), erode);

            int a = _vertices.Count;
            AddVertex(tail, direction, new Vector2(0f, v0), new Vector2(-halfWidth, 0f), life, tint);
            AddVertex(tail, direction, new Vector2(0f, v1), new Vector2(halfWidth, 0f), life, tint);
            AddVertex(centre, direction, new Vector2(1f, v0), new Vector2(-halfWidth, 0f), life, tint);
            AddVertex(centre, direction, new Vector2(1f, v1), new Vector2(halfWidth, 0f), life, tint);

            List<int> tris = _indices[submesh];
            tris.Add(a); tris.Add(a + 1); tris.Add(a + 3);
            tris.Add(a); tris.Add(a + 3); tris.Add(a + 2);
        }

        /// <summary>화면을 향하는 사각형. 파편과 연기에 쓴다.</summary>
        private void AddCameraQuad(int submesh, Vector3 centre, float halfSize, float spin,
            float uOffset, float uScale, float intensity, float core, float erode, int atlasRow)
        {
            if (intensity <= 0.002f)
            {
                return;
            }
            float v0 = atlasRow / (float)ATLAS_ROWS;
            float v1 = (atlasRow + 1f) / ATLAS_ROWS;
            if (atlasRow >= ATLAS_ROWS)
            {
                v0 = 0f;
                v1 = 1f;
            }
            Color tint = new Color(core, 0f, 0f, intensity);
            Vector3 axis = new Vector3(Mathf.Cos(spin), 0f, Mathf.Sin(spin));
            Vector2 life = new Vector2(Hash(Mathf.RoundToInt(spin * 97f), 0, 92), erode);

            Vector3 up = new Vector3(0f, halfSize, 0f);
            int a = _vertices.Count;
            AddVertex(centre - up, axis, new Vector2(uOffset, v0), new Vector2(-halfSize, 0f), life, tint);
            AddVertex(centre - up, axis, new Vector2(uOffset, v1), new Vector2(halfSize, 0f), life, tint);
            AddVertex(centre + up, axis, new Vector2(uOffset + uScale, v0), new Vector2(-halfSize, 0f), life, tint);
            AddVertex(centre + up, axis, new Vector2(uOffset + uScale, v1), new Vector2(halfSize, 0f), life, tint);

            List<int> tris = _indices[submesh];
            tris.Add(a); tris.Add(a + 1); tris.Add(a + 3);
            tris.Add(a); tris.Add(a + 3); tris.Add(a + 2);
        }

        private void AddVertex(Vector3 position, Vector3 tangent, Vector2 uv, Vector2 shape,
            Vector2 life, Color color)
        {
            _vertices.Add(position);
            _tangents.Add(tangent);
            _uvs.Add(uv);
            _shape.Add(shape);
            _life.Add(life);
            _colors.Add(color);
        }

        private static float Hash(int a, int b, int c)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(c * 83492791);
                h ^= h >> 13;
                h *= 0x85EBCA6B;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        private void OnDisable()
        {
            Stop();
        }

        private void OnDestroy()
        {
            if (_mesh == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Destroy(_mesh);
            }
            else
            {
                DestroyImmediate(_mesh);
            }
        }
    }
}
