// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.AI;
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Art
{
    /// <summary>
    /// 보스 예고(BossTelegraphCue)를 바닥 장판으로 그린다. 서버가 확정한 값만 읽어 표현만 한다,
    /// 판정에는 관여하지 않는다. 동시에 여러 예고(다타/다중 대상 패턴)가 떠도 각자 따로 그린다
    /// </summary>
    public sealed class BossTelegraphView : MonoBehaviour
    {
        /****************************************
        *                Types
        ****************************************/

        private readonly struct CueKey
        {
            public readonly uint AttackInstanceId;
            public readonly byte RepeatIndex;
            public readonly uint TargetNetId;

            public CueKey(in BossTelegraphCue cue)
            {
                AttackInstanceId = cue.AttackInstanceId;
                RepeatIndex      = cue.RepeatIndex;
                TargetNetId      = cue.TargetNetId;
            }

            public override bool Equals(object obj)
            {
                return (obj is CueKey other) && (AttackInstanceId == other.AttackInstanceId) &&
                    (RepeatIndex == other.RepeatIndex) && (TargetNetId == other.TargetNetId);
            }

            private const uint HASH_PRIME     = 397u;
            private const int  TARGET_ID_SHIFT = 8;

            public override int GetHashCode()
            {
                return (int) ((AttackInstanceId * HASH_PRIME) ^ RepeatIndex ^ (TargetNetId << TARGET_ID_SHIFT));
            }
        }

        private sealed class Shape
        {
            public GameObject    GameObject;
            public MeshRenderer  Renderer;
            public Mesh          Mesh;
            public BossTelegraphCue Cue;
        }

        /****************************************
        *              Constants
        ****************************************/

        private const string SHADER_NAME           = "Geumeum/BossTelegraph";
        private const int    FAN_SEGMENTS           = 24;
        private const float  GROUND_OFFSET          = 0.03f; // 바닥과 겹쳐 그릴 때 z-fighting 방지
        private const float  MIN_ALPHA              = 0.12f;
        private const float  MAX_ALPHA              = 0.55f; // impact 직전 가장 진해질 때
        private const float  FORWARD_EPSILON_SQR    = 0.0001f;
        private const float  MIN_SHAPE_SIZE         = 0.1f; // 반경, 거리 SO 값이 0 이하일 때 최소 보정치
        private const float  FULL_CIRCLE_DEGREES    = 360f;
        private const float  MIN_FAN_ANGLE_DEGREES  = 1f;
        private const int    MIN_FAN_SEGMENTS       = 2;

        // 장판을 안 그리는 패턴들 - 신호(모션 트리거)는 그대로 받되 바닥 표시만 숨긴다.
        // 3(순간 접근 3연계)은 원래부터, 11/13(여우불 광선)은 360도 회전이라 고정 부채꼴로
        // 못 그리고, 12/14(여우불 탄막)는 매 발 방향이 바뀌어 고정 부채꼴이 방향을 오해시킨다
        private static readonly int[] G_HIDDEN_PATTERN_IDS = { 3, 11, 12, 13, 14 };

        /****************************************
        *                Fields
        ****************************************/

        private IBossTelegraphSource _telegraphSource;
        private Material             _material;
        private bool                 _isSubscribed;

        private readonly Dictionary<CueKey, Shape> _activeShapes = new Dictionary<CueKey, Shape>();

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _telegraphSource = GetComponentInParent<IBossTelegraphSource>();

            Shader shader = Shader.Find(SHADER_NAME);
            if (shader == null)
            {
                Debug.LogError($"[BossTelegraphView] 셰이더 '{SHADER_NAME}'을 못 찾았다", this);
                return;
            }

            _material = new Material(shader) { name = "BossTelegraph (Instance)" };
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            ClearAllShapes();
        }

        private void OnDestroy()
        {
            if (_material != null)
            {
                Destroy(_material);
            }
        }

        private void Update()
        {
            if (_activeShapes.Count == 0)
            {
                return;
            }

            double now = NetworkTime.time;
            foreach (Shape shape in _activeShapes.Values)
            {
                UpdateAlpha(shape, now);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void TrySubscribe()
        {
            if ((_isSubscribed) || (_telegraphSource == null) || (_material == null))
            {
                return;
            }

            _telegraphSource.TelegraphChanged += HandleTelegraphChanged;
            _isSubscribed = true;

            // 뷰가 뒤늦게 켜져도(재접속 등) 이미 떠 있는 예고를 놓치지 않는다
            foreach (BossTelegraphCue cue in _telegraphSource.TelegraphCues)
            {
                HandleTelegraphChanged(cue);
            }
        }

        private void Unsubscribe()
        {
            if (!_isSubscribed)
            {
                return;
            }

            _telegraphSource.TelegraphChanged -= HandleTelegraphChanged;
            _isSubscribed = false;
        }

        private void HandleTelegraphChanged(BossTelegraphCue cue)
        {
            var key = new CueKey(cue);

            if (!cue.IsActive)
            {
                RemoveShape(key);
                return;
            }

            if ((cue.AttackInstanceId == 0u) ||
                (IsHiddenPattern(cue.PatternId)) ||
                (!GameDB.Boss.TryGetPattern(cue.PatternId, out BossPatternSO.PatternEntry pattern)))
            {
                return;
            }

            var spec = new BossPatternExecutionSpec(pattern);

            if (_activeShapes.TryGetValue(key, out Shape existing))
            {
                existing.Cue = cue;
                ApplyTransform(existing.GameObject.transform, cue, spec);
                return;
            }

            Shape shape = BuildShape(cue, spec);
            _activeShapes[key] = shape;
        }

        private static bool IsHiddenPattern(int patternId)
        {
            for (int i = 0; i < G_HIDDEN_PATTERN_IDS.Length; ++i)
            {
                if (G_HIDDEN_PATTERN_IDS[i] == patternId)
                {
                    return true;
                }
            }

            return false;
        }

        private Shape BuildShape(in BossTelegraphCue cue, in BossPatternExecutionSpec spec)
        {
            // 루트에 매단다 - Model에 매달면 모델 스케일(1.2배 등)을 그대로 물려받아 장판이 커진다
            var go = new GameObject("BossTelegraph");
            go.transform.SetParent(transform.root, worldPositionStays: false);

            var filter   = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows    = false;
            renderer.sharedMaterial    = _material;

            Mesh mesh = BuildMesh(spec);
            filter.sharedMesh = mesh;

            ApplyTransform(go.transform, cue, spec);

            bool isParryable = GameDB.Boss.TryGetPattern(cue.PatternId, out BossPatternSO.PatternEntry pattern)
                && spec.IsRepeatParryable(pattern, cue.RepeatIndex);

            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", ColorFor(isParryable, MIN_ALPHA));
            renderer.SetPropertyBlock(block);

            return new Shape { GameObject = go, Renderer = renderer, Mesh = mesh, Cue = cue };
        }

        private static void ApplyTransform(Transform target, in BossTelegraphCue cue,
            in BossPatternExecutionSpec spec)
        {
            // cue.Origin의 y는 발생원마다 다르다(MarkedCircle은 대상 피벗 높이를 그대로 쓴다) -
            // 바닥은 항상 평평하다고 보고 y를 고정해서 장판이 공중에 뜨지 않게 한다
            Vector3 origin = cue.Origin;
            origin.y = GROUND_OFFSET;
            target.position = origin;

            Vector3 forward = cue.Direction;
            forward.y = 0f;
            target.rotation = (forward.sqrMagnitude > FORWARD_EPSILON_SQR)
                ? Quaternion.LookRotation(forward.normalized)
                : Quaternion.identity;
        }

        private static Mesh BuildMesh(in BossPatternExecutionSpec spec)
        {
            switch (spec.Template)
            {
                case BossPatternSO.PatternTemplate.TargetedDash:
                    return BuildLaneMesh(Mathf.Max(MIN_SHAPE_SIZE, spec.ImpactRadius),
                        Mathf.Max(MIN_SHAPE_SIZE, spec.MoveDistance));

                case BossPatternSO.PatternTemplate.MarkedCircle:
                    return BuildFanMesh(Mathf.Max(MIN_SHAPE_SIZE, spec.ImpactRadius),
                        FULL_CIRCLE_DEGREES, FAN_SEGMENTS);

                case BossPatternSO.PatternTemplate.ConeBurst:
                    return BuildFanMesh(Mathf.Max(MIN_SHAPE_SIZE, spec.Range), spec.AngleDegrees, FAN_SEGMENTS);

                default: // MeleeArc
                    return BuildFanMesh(Mathf.Max(MIN_SHAPE_SIZE, spec.Range), spec.AngleDegrees, FAN_SEGMENTS);
            }
        }

        private static Mesh BuildFanMesh(float radius, float angleDegrees, int segments)
        {
            angleDegrees = Mathf.Clamp(angleDegrees, MIN_FAN_ANGLE_DEGREES, FULL_CIRCLE_DEGREES);
            segments     = Mathf.Max(segments, MIN_FAN_SEGMENTS);

            var vertices  = new Vector3[segments + 2];
            var triangles = new int[segments * 3];

            vertices[0] = Vector3.zero;
            float halfAngle = angleDegrees * 0.5f;
            for (int i = 0; i <= segments; ++i)
            {
                float t     = (float) i / segments;
                float angle = Mathf.Deg2Rad * Mathf.Lerp(-halfAngle, halfAngle, t);
                vertices[i + 1] = new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
            }

            for (int i = 0; i < segments; ++i)
            {
                int baseIndex = i * 3;
                triangles[baseIndex]     = 0;
                triangles[baseIndex + 1] = i + 1;
                triangles[baseIndex + 2] = i + 2;
            }

            var mesh = new Mesh { name = "BossTelegraphFan" };
            mesh.vertices  = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildLaneMesh(float halfWidth, float length)
        {
            var mesh = new Mesh { name = "BossTelegraphLane" };
            mesh.vertices = new[]
            {
                new Vector3(-halfWidth, 0f, 0f),
                new Vector3(halfWidth, 0f, 0f),
                new Vector3(halfWidth, 0f, length),
                new Vector3(-halfWidth, 0f, length),
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private void UpdateAlpha(Shape shape, double now)
        {
            double start  = shape.Cue.StartServerTime;
            double impact = shape.Cue.ImpactServerTime;
            float progress = (impact > start)
                ? Mathf.Clamp01((float) ((now - start) / (impact - start)))
                : 1f;

            float alpha = Mathf.Lerp(MIN_ALPHA, MAX_ALPHA, progress);

            // 템플릿 판별용 재조회 없이 현재 색조만 갈아끼운다 - 알파만 프레임마다 바뀐다
            var block = new MaterialPropertyBlock();
            shape.Renderer.GetPropertyBlock(block);
            Color color = block.GetColor("_Color");
            color.a = alpha;
            block.SetColor("_Color", color);
            shape.Renderer.SetPropertyBlock(block);
        }

        // 카운터(패링) 가능한 반복만 파란색, 나머지는 전부 빨간색으로 통일한다 - 템플릿별 구분 색상 폐기
        private static Color ColorFor(bool isParryable, float alpha)
        {
            return isParryable
                ? new Color(0.2f, 0.55f, 1f, alpha)
                : new Color(1f, 0.2f, 0.2f, alpha);
        }

        private void RemoveShape(in CueKey key)
        {
            if (!_activeShapes.TryGetValue(key, out Shape shape))
            {
                return;
            }

            _activeShapes.Remove(key);
            DestroyShape(shape);
        }

        private void ClearAllShapes()
        {
            foreach (Shape shape in _activeShapes.Values)
            {
                DestroyShape(shape);
            }

            _activeShapes.Clear();
        }

        private static void DestroyShape(Shape shape)
        {
            if (shape.GameObject != null)
            {
                Destroy(shape.GameObject);
            }

            if (shape.Mesh != null)
            {
                Destroy(shape.Mesh);
            }
        }
    }
}
