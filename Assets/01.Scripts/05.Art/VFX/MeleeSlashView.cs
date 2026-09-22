// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// Project
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Art
{
    /// <summary>
    /// 검 기본 공격의 검기. 판정 시각 앞뒤로 실제 검날(손잡이 쪽에서 칼끝)이 지나간 위치를 기록해
    /// 월드 공간 리본으로 잇고, 점마다 나이를 먹여 잔상으로 지운다
    /// 애니메이션이 휘두른 방향과 경로를 그대로 따르므로 공격 방향이나 콤보 단을 따로 계산하지 않는다
    /// 판정에는 관여하지 않는 로컬 표현이다
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(PooledVisualLifetime))]
    public sealed class MeleeSlashView : MonoBehaviour, IActionCueVisual
    {
        /****************************************
        *                Types
        ****************************************/

        /// <summary> 검기 한 번의 조정값. PresentationConfigSO에서 복사한다 </summary>
        public struct TrailSettings
        {
            public float TrailSeconds;
            public float RecordBeforeImpactSeconds;
            public float RecordAfterImpactSeconds;
            public float HiltRatio;
            public Color BodyColor;
            public Color EdgeColor;
        }

        private struct BladeSample
        {
            public Vector3 Hilt;
            public Vector3 Tip;
            public float   Time;
        }

        /****************************************
        *                Fields
        ****************************************/

        private const int   MAX_SAMPLES         = 64;
        private const int   SUBDIVISIONS        = 4;     // 프레임 사이 곡선 보간. 프레임이 낮아도 호가 각지지 않는다
        private const int   MAX_POINTS          = ((MAX_SAMPLES - 1) * SUBDIVISIONS) + 1;
        private const float MIN_SAMPLE_DISTANCE = 0.01f; // 검이 멈춰 있을 때 같은 자리에 점을 쌓지 않는다
        private const float MIN_KNOT            = 0.0001f;

        private static readonly int G_BodyColor = Shader.PropertyToID("_BodyColor");
        private static readonly int G_EdgeColor = Shader.PropertyToID("_EdgeColor");

        private readonly BladeSample[] _samples  = new BladeSample[MAX_SAMPLES];
        private readonly List<Vector3> _vertices = new List<Vector3>(MAX_POINTS * 2);
        private readonly List<Vector4> _uvs      = new List<Vector4>(MAX_POINTS * 2);
        private readonly List<int>     _indices  = new List<int>((MAX_POINTS - 1) * 6);

        private MeshFilter            _filter;
        private MeshRenderer          _renderer;
        private PooledVisualLifetime  _lifetime;
        private MaterialPropertyBlock _block;
        private Mesh                  _mesh;

        private IWeaponBladeSource _blade;
        private TrailSettings      _settings;
        private uint   _lease;
        private double _recordFrom;
        private double _recordUntil;
        private float  _clock;
        private AbilityVisualClock _visualClock;
        private int    _sampleStart;
        private int    _sampleCount;
        private bool   _isRecording;
        private bool   _isActive;

        public int SampleCount => _sampleCount;
        public bool IsActive => _isActive;
        public Mesh TrailMesh => _mesh;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnDisable()
        {
            ResetTrail();
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

        // 애니메이션이 이번 프레임 뼈를 옮긴 뒤에 검날을 읽어야 한 프레임 늦지 않는다
        private void LateUpdate()
        {
            if (!_isActive)
            {
                return;
            }

            IRunTimeSource runTime = RunTimeLocator.Current;
            TickPresentation(Time.unscaledDeltaTime, runTime.GameplayElapsedSeconds,
                Time.unscaledTimeAsDouble, runTime.IsPaused, Mirror.NetworkServer.active);
        }

        /****************************************
        *                Methods
        ****************************************/

        public void PlayCue(in PlayerActionCue cue, IWeaponBladeSource blade)
        {
            if ((cue.Kind != PlayerActionCueKind.Attack) || (cue.Weapon != WeaponKind.Sword))
            {
                return;
            }

            if ((!GameDB.IsInitialized) || (GameDB.Presentation == null))
            {
                Debug.LogError("[MeleeSlashView] GameDB가 준비되지 않아 검기를 그리지 않는다", this);
                return;
            }

            PresentationConfigSO config = GameDB.Presentation;
            Begin(in cue, blade, new TrailSettings
            {
                TrailSeconds              = config.MeleeSlashTrailSeconds,
                RecordBeforeImpactSeconds = config.MeleeSlashRecordBeforeImpactSeconds,
                RecordAfterImpactSeconds  = config.MeleeSlashRecordAfterImpactSeconds,
                HiltRatio                 = config.MeleeSlashHiltRatio,
                BodyColor                 = config.MeleeSlashBodyColor,
                EdgeColor                 = config.MeleeSlashEdgeColor,
            });
        }

        public void ReleaseCue()
        {
            _isRecording = false;
        }

        /// <summary> 설정을 직접 받아 시작한다. GameDB 없이 검증할 때도 이 경로를 쓴다 </summary>
        public void Begin(in PlayerActionCue cue, IWeaponBladeSource blade, TrailSettings settings)
        {
            EnsureInitialized();
            ResetTrail();
            _lease = (_lifetime != null) ? _lifetime.LeaseGeneration : 0u;

            if (blade == null)
            {
                Debug.LogWarning("[MeleeSlashView] 검 위치를 줄 무기가 없어 검기를 그리지 않는다", this);
                ReturnToPool();
                return;
            }

            // 리본 꼭짓점이 월드 좌표라 이 오브젝트는 원점에 둔다
            transform.SetParent(null, false);
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;

            _blade    = blade;
            _settings = settings;
            ResolveRecordWindow(cue.ImpactGameplayTime, settings.RecordBeforeImpactSeconds,
                settings.RecordAfterImpactSeconds, out _recordFrom, out _recordUntil);

            _block.Clear();
            _block.SetColor(G_BodyColor, settings.BodyColor);
            _block.SetColor(G_EdgeColor, settings.EdgeColor);
            _renderer.SetPropertyBlock(_block);

            _visualClock = default;
            _visualClock.Sample(RunTimeLocator.Current.GameplayElapsedSeconds,
                Time.unscaledTimeAsDouble, RunTimeLocator.Current.IsPaused, Mirror.NetworkServer.active);
            _visualClock.ObserveCue(cue.StartGameplayTime);
            _isRecording = true;
            _isActive    = true;
        }

        /// <summary> 드문 서버 시각 갱신 사이를 보간해 원격 검기도 공격 모션 중 기록한다 </summary>
        public void TickPresentation(float deltaSeconds, double rawTime, double wallTime,
            bool isPaused, bool authoritative)
        {
            double visualTime = _visualClock.Sample(rawTime, wallTime, isPaused, authoritative);
            if (!isPaused) { Tick(deltaSeconds, visualTime); }
        }

        /// <summary> 한 프레임 진행. deltaSeconds는 잔상 나이, gameplayTime은 기록 구간 판단에 쓴다 </summary>
        public void Tick(float deltaSeconds, double gameplayTime)
        {
            if (!_isActive)
            {
                return;
            }

            _clock += Mathf.Max(0f, deltaSeconds);

            if (_isRecording && (gameplayTime >= _recordUntil))
            {
                _isRecording = false;
            }

            if (_isRecording && (gameplayTime >= _recordFrom))
            {
                RecordBlade();
            }

            DropExpiredSamples();

            // 늦게 받은 cue는 기록 구간이 이미 지나 점이 하나도 없다. 곧바로 풀에 돌려준다
            if ((!_isRecording) && (_sampleCount == 0))
            {
                ReturnToPool();
                return;
            }

            RebuildMesh();
        }

        public static void ResolveRecordWindow(double impactGameplayTime, float beforeSeconds,
            float afterSeconds, out double recordFrom, out double recordUntil)
        {
            recordFrom  = impactGameplayTime - Mathf.Max(0f, beforeSeconds);
            recordUntil = impactGameplayTime + Mathf.Max(0f, afterSeconds);
        }

        /// <summary> 0은 방금 기록한 점, 1은 사라질 점 </summary>
        public static float ResolveAge01(float clock, float sampleTime, float trailSeconds)
        {
            return (trailSeconds > 0f)
                ? Mathf.Clamp01((clock - sampleTime) / trailSeconds)
                : 1f;
        }

        /// <summary>
        /// p1에서 p2 사이를 t로 잇는 centripetal Catmull-Rom 곡선. 점 간격으로 매개변수를 잡아
        /// 검이 급하게 꺾이는 구간에서도 곡선이 바깥으로 튀어나가 가시가 생기지 않는다
        /// </summary>
        public static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t0 = 0f;
            float t1 = t0 + ResolveKnot(p0, p1);
            float t2 = t1 + ResolveKnot(p1, p2);
            float t3 = t2 + ResolveKnot(p2, p3);
            float u  = Mathf.LerpUnclamped(t1, t2, t);

            Vector3 a1 = Remap(p0, p1, t0, t1, u);
            Vector3 a2 = Remap(p1, p2, t1, t2, u);
            Vector3 a3 = Remap(p2, p3, t2, t3, u);
            Vector3 b1 = Remap(a1, a2, t0, t2, u);
            Vector3 b2 = Remap(a2, a3, t1, t3, u);
            return Remap(b1, b2, t1, t2, u);
        }

        // 거리의 제곱근이 centripetal 매개변수다. 같은 점이 겹쳐도 0으로 나누지 않게 하한을 둔다
        private static float ResolveKnot(Vector3 from, Vector3 to)
        {
            return Mathf.Max(MIN_KNOT, Mathf.Pow((to - from).sqrMagnitude, 0.25f));
        }

        private static Vector3 Remap(Vector3 a, Vector3 b, float ta, float tb, float u)
        {
            float span = tb - ta;
            return (((tb - u) / span) * a) + (((u - ta) / span) * b);
        }

        private void EnsureInitialized()
        {
            if (_block != null)
            {
                return;
            }

            _filter   = GetComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>();
            _lifetime = GetComponent<PooledVisualLifetime>();
            _block    = new MaterialPropertyBlock();

            _mesh = new Mesh
            {
                name      = "MeleeSlashTrail",
                hideFlags = HideFlags.DontSave,
            };
            _mesh.MarkDynamic();
            _filter.sharedMesh = _mesh;
            _renderer.enabled  = false;
        }

        private void RecordBlade()
        {
            if (!_blade.TryGetBladeSegment(out Vector3 hilt, out Vector3 tip))
            {
                return;
            }

            Vector3 start = Vector3.Lerp(hilt, tip, _settings.HiltRatio);

            if (_sampleCount > 0)
            {
                BladeSample last = _samples[SampleIndex(_sampleCount - 1)];
                if ((last.Tip - tip).sqrMagnitude < (MIN_SAMPLE_DISTANCE * MIN_SAMPLE_DISTANCE))
                {
                    return;
                }
            }

            if (_sampleCount == MAX_SAMPLES)
            {
                _sampleStart = (_sampleStart + 1) % MAX_SAMPLES;
                --_sampleCount;
            }

            _samples[SampleIndex(_sampleCount)] = new BladeSample
            {
                Hilt = start,
                Tip  = tip,
                Time = _clock,
            };
            ++_sampleCount;
        }

        private void DropExpiredSamples()
        {
            while ((_sampleCount > 0) &&
                ((_clock - _samples[_sampleStart].Time) >= _settings.TrailSeconds))
            {
                _sampleStart = (_sampleStart + 1) % MAX_SAMPLES;
                --_sampleCount;
            }
        }

        private void RebuildMesh()
        {
            _vertices.Clear();
            _uvs.Clear();
            _indices.Clear();

            if (_sampleCount >= 2)
            {
                float distance = 0f;
                Vector3 previousTip = _samples[SampleIndex(0)].Tip;

                for (int i = 0; i < (_sampleCount - 1); ++i)
                {
                    BladeSample s0 = _samples[SampleIndex(Mathf.Max(0, i - 1))];
                    BladeSample s1 = _samples[SampleIndex(i)];
                    BladeSample s2 = _samples[SampleIndex(i + 1)];
                    BladeSample s3 = _samples[SampleIndex(Mathf.Min(_sampleCount - 1, i + 2))];

                    for (int step = 0; step < SUBDIVISIONS; ++step)
                    {
                        float t = (float) step / SUBDIVISIONS;
                        Vector3 tip = CatmullRom(s0.Tip, s1.Tip, s2.Tip, s3.Tip, t);
                        distance += Vector3.Distance(previousTip, tip);
                        previousTip = tip;
                        AddPoint(CatmullRom(s0.Hilt, s1.Hilt, s2.Hilt, s3.Hilt, t), tip,
                            Mathf.Lerp(s1.Time, s2.Time, t), distance);
                    }
                }

                BladeSample newest = _samples[SampleIndex(_sampleCount - 1)];
                distance += Vector3.Distance(previousTip, newest.Tip);
                AddPoint(newest.Hilt, newest.Tip, newest.Time, distance);

                int pointCount = _vertices.Count / 2;
                for (int i = 0; i < (pointCount - 1); ++i)
                {
                    int hilt = i * 2;
                    _indices.Add(hilt);
                    _indices.Add(hilt + 1);
                    _indices.Add(hilt + 2);
                    _indices.Add(hilt + 1);
                    _indices.Add(hilt + 3);
                    _indices.Add(hilt + 2);
                }
            }

            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetTriangles(_indices, 0, false);
            _mesh.RecalculateBounds();
            _renderer.enabled = (_indices.Count > 0);
        }

        // uv0: x는 나이(0 새것, 1 사라짐), y는 손잡이 쪽 0에서 칼끝 1, z는 칼끝이 지나온 거리(노이즈가 궤적에 붙게)
        private void AddPoint(Vector3 hilt, Vector3 tip, float sampleTime, float distance)
        {
            float age = ResolveAge01(_clock, sampleTime, _settings.TrailSeconds);
            _vertices.Add(hilt);
            _vertices.Add(tip);
            _uvs.Add(new Vector4(age, 0f, distance, 0f));
            _uvs.Add(new Vector4(age, 1f, distance, 0f));
        }

        private int SampleIndex(int offset)
        {
            return (_sampleStart + offset) % MAX_SAMPLES;
        }

        private void ResetTrail()
        {
            _isActive    = false;
            _isRecording = false;
            _blade       = null;
            _sampleStart = 0;
            _sampleCount = 0;
            _clock       = 0f;

            if (_mesh != null)
            {
                _mesh.Clear();
            }

            if (_renderer != null)
            {
                _renderer.enabled = false;
            }
        }

        private void ReturnToPool()
        {
            uint lease = _lease;
            ResetTrail();
            if ((_lifetime != null) && (lease != 0u))
            {
                _lifetime.TryStop(lease);
            }
        }
    }
}
