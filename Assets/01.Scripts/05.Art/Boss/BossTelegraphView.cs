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
    ///
    /// 타이밍 섬광(COMBAT_STYLE_DESIGN S4)도 여기서 보스 모델에 그린다. 장판은 "어디서", 섬광은 "언제"다
    /// - 대상: ParryController가 받은 확정 창(LocalParryWindowFeed) 시작에 한 번의 반짝 + 소리
    /// - 비대상: 확정 창을 받지 못하므로 cue impact에서 역산한 근사 창으로 어둡고 약하게
    /// - 패링 불가: 창이 없으므로 impact - 경고 선행시간에 두 번 번지는 붉은 빛 + 경고음
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

        // 타이밍 섬광 - 활성 예고 최대 8개 x 가능/불가 + 대상 확정 창을 담는 고정 용량
        private const int FLASH_SCHEDULE_CAPACITY = 24;

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

        private readonly ParryFlashScheduler _flashSchedule = new ParryFlashScheduler(FLASH_SCHEDULE_CAPACITY);
        private ParryFlashTint  _flashTint;
        private NetworkIdentity _bossIdentity;
        private IBossPartSource _partSource; // 꼬리 파괴로 옮겨진 반격 기회(이양 반복)를 가리는 데 쓴다

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _telegraphSource = GetComponentInParent<IBossTelegraphSource>();
            _bossIdentity    = GetComponentInParent<NetworkIdentity>();
            _partSource      = GetComponentInParent<IBossPartSource>();

            // 이 뷰는 보스 모델 루트에 붙는다. 장판은 transform.root 아래라 섬광 대상에 섞이지 않는다
            _flashTint = new ParryFlashTint(transform, includeInactive: false, meshRenderersOnly: true);

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
            ClearFlash();
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
            if ((_activeShapes.Count == 0) && (_flashSchedule.Count == 0) && (!_flashTint.IsApplied))
            {
                return;
            }

            double now = NetworkTime.time;
            foreach (Shape shape in _activeShapes.Values)
            {
                UpdateAlpha(shape, now);
            }

            UpdateFlash(now);
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
            LocalParryWindowFeed.Opened  += HandleParryWindowOpened;
            LocalParryWindowFeed.Closed  += HandleParryWindowClosed;
            LocalParryWindowFeed.Cleared += HandleParryWindowCleared;
            _isSubscribed = true;

            // 확정 창을 먼저 줍는다. 붉은 cue인데 서버가 창을 연 타(튕겨내기 등)를 경고에서 거르려면 창이 먼저 있어야 한다
            if (LocalParryWindowFeed.TryGetActive(NetworkTime.time, out ParryWindowSignal activeWindow))
            {
                HandleParryWindowOpened(activeWindow);
            }

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
            LocalParryWindowFeed.Opened  -= HandleParryWindowOpened;
            LocalParryWindowFeed.Closed  -= HandleParryWindowClosed;
            LocalParryWindowFeed.Cleared -= HandleParryWindowCleared;
            _isSubscribed = false;
        }

        private void HandleTelegraphChanged(BossTelegraphCue cue)
        {
            // 섬광 예약은 장판과 독립이다. 장판 처리 흐름(아래)은 바꾸지 않는다
            ScheduleFlashForCue(cue);

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

        /****************************************
        *          Timing Flash (S4)
        ****************************************/

        private void HandleParryWindowOpened(ParryWindowSignal signal)
        {
            if ((_bossIdentity == null) || (signal.Key.AttackerNetId != _bossIdentity.netId))
            {
                return;
            }

            double now = NetworkTime.time;
            ulong  key = ParryFlashTiming.ExactWindowKey(signal.Key.AttackerNetId, signal.Key.WindowId);
            ReportUpsertFailure(_flashSchedule.Upsert(key, 0u, ParryFlashKind.Parryable, true,
                signal.StartServerTime, signal.EndServerTime, now), key);

            // 같은 타의 cue 기반 섬광(비대상 근사, 붉은 경고)은 확정 창이 대신한다. 신호가 두 번 나가지 않게 치운다
            RemoveCueFlashesMatching(signal.EndServerTime);
        }

        private void HandleParryWindowClosed(ParryWindowKey windowKey)
        {
            if ((_bossIdentity == null) || (windowKey.AttackerNetId != _bossIdentity.netId))
            {
                return;
            }

            _flashSchedule.Remove(ParryFlashTiming.ExactWindowKey(windowKey.AttackerNetId, windowKey.WindowId));
        }

        private void HandleParryWindowCleared()
        {
            for (int i = _flashSchedule.Count - 1; i >= 0; --i)
            {
                ulong key = _flashSchedule[i].Key;
                if (ParryFlashTiming.IsExactWindowKey(key))
                {
                    _flashSchedule.Remove(key);
                }
            }
        }

        private void ScheduleFlashForCue(in BossTelegraphCue cue)
        {
            if (!cue.IsActive)
            {
                // 취소, 타격 종료. 같은 타를 노리는 다른 대상 cue가 남아 있으면 그 타의 섬광은 유지한다
                RemoveOrphanCueFlashes();
                return;
            }

            if ((cue.AttackInstanceId == 0u) || (IsHiddenPattern(cue.PatternId)) || (!GameDB.IsInitialized) ||
                (!GameDB.Boss.TryGetPattern(cue.PatternId, out BossPatternSO.PatternEntry pattern)))
            {
                return;
            }

            // 대상이 이미 확정 창을 받은 타다. 확정 창이 신호를 맡는다
            if (HasExactWindowFor(cue.ImpactServerTime))
            {
                return;
            }

            bool   isTarget = IsLocalTargetOfHit(cue);
            double now      = NetworkTime.time;

            // 튕겨내기, 이양 반복도 서버가 창을 여는 타다. 비대상에게 붉은 불가 경고를 띄우면 안 된다
            BossPartSnapshot parts = (_partSource != null)
                ? _partSource.PartSnapshot
                : BossParryFlashClassifier.UnknownPartsSnapshot();
            bool isTransferred = BossParryFlashClassifier.IsTransferredRepeat(GameDB.Boss, parts, cue.PatternId,
                cue.RepeatIndex);

            if (BossParryFlashClassifier.Classify(pattern, cue.RepeatIndex, isTransferred) == ParryFlashKind.Parryable)
            {
                // 예고 중 꼬리가 부서져 분류가 바뀌었으면 먼저 잡힌 불가 경고를 걷는다. 한 타에 두 신호가 나가지 않게 한다
                _flashSchedule.Remove(ParryFlashTiming.CueKey(cue.AttackInstanceId, cue.RepeatIndex, ParryFlashKind.Unparryable));

                ulong parryKey = ParryFlashTiming.CueKey(cue.AttackInstanceId, cue.RepeatIndex, ParryFlashKind.Parryable);
                if (isTarget)
                {
                    // 대상은 ParryController의 확정 창(로드아웃 보너스 포함)으로만 섬광한다. 이 타의 근사는 치운다
                    _flashSchedule.Remove(parryKey);
                    return;
                }

                // 근사: 비대상은 대상의 확정 창을 받지 못한다(COMBAT_STYLE_DESIGN 8장 - cue 확장 전까지)
                // 서버와 같은 역산식에 기본 창 길이만 넣으므로 대상의 장비 보정만큼 앞쪽이 어긋날 수 있다
                ParryFlashTiming.ApproximateWindow(cue.ImpactServerTime, GameDB.Combat.ParryWindowDuration,
                    Constants.Network.PARRY_RESOLUTION_LEAD, out double start, out double end);
                ReportUpsertFailure(_flashSchedule.Upsert(parryKey, cue.Revision, ParryFlashKind.Parryable,
                    false, start, end, now), parryKey);
                return;
            }

            // 패링 불가 - 창이 없으므로 서버 impact에서 경고 선행시간만큼 당겨 예약한다
            ulong  warningKey   = ParryFlashTiming.CueKey(cue.AttackInstanceId, cue.RepeatIndex, ParryFlashKind.Unparryable);
            double warningStart = ParryFlashTiming.UnparryableWarningStart(cue.ImpactServerTime,
                GameDB.Presentation.UnparryableWarningLeadSeconds);
            ReportUpsertFailure(_flashSchedule.Upsert(warningKey, cue.Revision, ParryFlashKind.Unparryable,
                isTarget, warningStart, cue.ImpactServerTime, now), warningKey);
        }

        private void UpdateFlash(double now)
        {
            _flashSchedule.Advance(now);

            if (!GameDB.IsInitialized)
            {
                ClearFlashTint();
                return;
            }

            PresentationConfigSO presentation = GameDB.Presentation;
            uint emitterId = (_bossIdentity != null) ? _bossIdentity.netId : 0u;

            // 소리는 제때 시작한 섬광에서 한 번만. 늦게 받아 남은 구간만 강조하는 항목은 소리를 내지 않는다
            for (int i = 0; i < _flashSchedule.Count; ++i)
            {
                ParryFlashEntry entry = _flashSchedule[i];
                if ((entry.JustBegan) && (!entry.RemainingOnly))
                {
                    ParryFlashSound.Play(entry.Kind, entry.IsTarget, transform.position, emitterId);
                }
            }

            int dominant = _flashSchedule.FindDominantBegun();
            if (dominant < 0)
            {
                ClearFlashTint();
                return;
            }

            ParryFlashEntry shown = _flashSchedule[dominant];
            Color color = (shown.Kind == ParryFlashKind.Unparryable)
                ? presentation.ParryFlashUnparryableColor
                : presentation.ParryFlashParryableColor;

            float weight = 1f; // 늦게 받은 구간은 남은 동안 그대로 강조한다
            if (!shown.RemainingOnly)
            {
                float shapeSeconds = ParryFlashTiming.ShapeSeconds(shown.Kind, presentation.ParryFlashSeconds);
                weight = ParryFlashTiming.EvaluateShape(shown.Kind, now - shown.Start, shapeSeconds);
            }

            // 비대상은 약하고 어둡게. "내가 패링할 수 있다"는 밝은 반짝임과 겹쳐 보이지 않게 한다
            float nonTargetScale = presentation.ParryFlashNonTargetScale;
            weight = ParryFlashTiming.AudienceWeight(weight, shown.IsTarget, nonTargetScale);
            color  = ParryFlashTint.ForAudience(color, shown.IsTarget, nonTargetScale);

            if (weight <= 0f)
            {
                ClearFlashTint();
                return;
            }

            _flashTint.ApplyBlend(color, weight);
        }

        private bool IsLocalTargetOfHit(in BossTelegraphCue cue)
        {
            NetworkIdentity localPlayer = NetworkClient.localPlayer;
            if (localPlayer == null)
            {
                return false;
            }

            uint localNetId = localPlayer.netId;
            if (cue.TargetNetId == localNetId)
            {
                return true;
            }

            // 전원 대상 패턴은 같은 타가 대상마다 cue 하나씩 온다
            IReadOnlyList<BossTelegraphCue> cues = _telegraphSource.TelegraphCues;
            for (int i = 0; i < cues.Count; ++i)
            {
                BossTelegraphCue other = cues[i];
                if ((other.IsActive) && (other.AttackInstanceId == cue.AttackInstanceId) &&
                    (other.RepeatIndex == cue.RepeatIndex) && (other.TargetNetId == localNetId))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasExactWindowFor(double cueImpactServerTime)
        {
            for (int i = 0; i < _flashSchedule.Count; ++i)
            {
                ParryFlashEntry entry = _flashSchedule[i];
                if ((ParryFlashTiming.IsExactWindowKey(entry.Key)) &&
                    (ParryFlashTiming.MatchesImpact(entry.End, Constants.Network.PARRY_RESOLUTION_LEAD, cueImpactServerTime)))
                {
                    return true;
                }
            }

            return false;
        }

        private void RemoveCueFlashesMatching(double windowEndServerTime)
        {
            if (_telegraphSource == null)
            {
                return;
            }

            IReadOnlyList<BossTelegraphCue> cues = _telegraphSource.TelegraphCues;
            for (int i = 0; i < cues.Count; ++i)
            {
                BossTelegraphCue cue = cues[i];
                if (!ParryFlashTiming.MatchesImpact(windowEndServerTime, Constants.Network.PARRY_RESOLUTION_LEAD,
                        cue.ImpactServerTime))
                {
                    continue;
                }

                _flashSchedule.Remove(ParryFlashTiming.CueKey(cue.AttackInstanceId, cue.RepeatIndex, ParryFlashKind.Parryable));
                _flashSchedule.Remove(ParryFlashTiming.CueKey(cue.AttackInstanceId, cue.RepeatIndex, ParryFlashKind.Unparryable));
            }
        }

        // 활성 cue 목록에 더 이상 없는 타의 cue 기반 섬광을 뺀다. 확정 창은 ParryController의 닫힘이 관리한다
        private void RemoveOrphanCueFlashes()
        {
            IReadOnlyList<BossTelegraphCue> cues = _telegraphSource.TelegraphCues;
            for (int i = _flashSchedule.Count - 1; i >= 0; --i)
            {
                ulong key = _flashSchedule[i].Key;
                if ((ParryFlashTiming.IsExactWindowKey(key)) || (IsCueKeyStillActive(cues, key)))
                {
                    continue;
                }

                _flashSchedule.Remove(key);
            }
        }

        private static bool IsCueKeyStillActive(IReadOnlyList<BossTelegraphCue> cues, ulong key)
        {
            for (int i = 0; i < cues.Count; ++i)
            {
                BossTelegraphCue cue = cues[i];
                if (!cue.IsActive)
                {
                    continue;
                }

                if ((key == ParryFlashTiming.CueKey(cue.AttackInstanceId, cue.RepeatIndex, ParryFlashKind.Parryable)) ||
                    (key == ParryFlashTiming.CueKey(cue.AttackInstanceId, cue.RepeatIndex, ParryFlashKind.Unparryable)))
                {
                    return true;
                }
            }

            return false;
        }

        private void ReportUpsertFailure(ParryFlashUpsertResult result, ulong key)
        {
            // 닫힌 창, 중복, 오래된 개정은 규칙대로 버린 것이다. 용량, 잘못된 시각만 알린다
            if ((result == ParryFlashUpsertResult.IgnoredFull) || (result == ParryFlashUpsertResult.IgnoredInvalid))
            {
                Debug.LogWarning($"[BossTelegraphView] 타이밍 섬광을 예약하지 못했다 ({result}, key={key:X})", this);
            }
        }

        private void ClearFlashTint()
        {
            if (_flashTint.IsApplied)
            {
                _flashTint.Clear();
            }
        }

        private void ClearFlash()
        {
            _flashSchedule.Clear();
            ClearFlashTint();
        }
    }
}
