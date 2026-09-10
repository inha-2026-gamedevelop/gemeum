// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Dev;

namespace Geumeum.Combat
{
    /// <summary>
    /// 씬에 있는 콜라이더를 게임 화면에 그린다.
    /// 판정 범위와 타겟 박스가 실제로 어디에 있는지는 값으로 못 본다 - 모양으로 봐야 본다.
    ///
    /// 씬 전체를 훑지 않고 카메라 주변만 Overlap으로 모은다.
    /// 개발 도구가 프레임을 먹으면 정작 확인하려던 감각이 달라진다
    /// </summary>
    public class ColliderGizmoView : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS

        /****************************************
        *                Preload
        ****************************************/

        /// <summary> 씬에 배치하지 않아도 실행 시작에 스스로 붙는다 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Preload()
        {
            DevToolHost.Attach<ColliderGizmoView>();
        }

        /****************************************
        *              Constants
        ****************************************/

        private const string PANEL_ID    = "collider";
        private const string PANEL_TITLE = "콜라이더";

        private const int MAX_COLLIDERS = 512; // 한 번에 모을 최대 개수

        private const float RADIUS_MIN     = 5f;
        private const float RADIUS_MAX     = 150f;
        private const float RADIUS_DEFAULT = 35f;

        // 갈래별 색. 무엇이 무엇인지 색으로 먼저 알아보게 한다
        private static readonly Color COLOR_PLAYER  = new Color(0.4f, 1f, 0.5f, 0.9f);
        private static readonly Color COLOR_ENEMY   = new Color(1f, 0.4f, 0.4f, 0.9f);
        private static readonly Color COLOR_BOSS    = new Color(1f, 0.55f, 0.15f, 0.9f);
        private static readonly Color COLOR_BARRIER = new Color(0.4f, 0.85f, 1f, 0.9f);
        private static readonly Color COLOR_WORLD   = new Color(0.7f, 0.7f, 0.75f, 0.5f);
        private static readonly Color COLOR_TRIGGER = new Color(1f, 0.9f, 0.35f, 0.7f);
        private static readonly Color COLOR_REACH   = new Color(1f, 0.6f, 0.25f, 0.8f);

        /****************************************
        *                Fields
        ****************************************/

        private readonly Collider[] _buffer = new Collider[MAX_COLLIDERS];

        private bool _isEnabled = false; // 기본은 꺼짐. 화면이 선으로 덮이면 아무것도 못 본다

        private bool _showPlayer   = true;
        private bool _showEnemy    = true;
        private bool _showBarrier  = true;
        private bool _showWorld    = false; // 지형·소품은 수가 많아 기본으로 끈다
        private bool _showArrows   = true;  // 화살은 몇 개 안 되고 확인할 일이 잦다
        private bool _showTriggers = true;
        private bool _showPartIds  = true;
        private bool _showMyReach  = true;

        private float _radius = RADIUS_DEFAULT;

        private int _drawnCount = 0;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            DevPanelHost.Register(PANEL_ID, PANEL_TITLE, Build, Handle);
        }

        private void OnDestroy()
        {
            DevPanelHost.Unregister(PANEL_ID);
        }

        private void Update()
        {
            if (!_isEnabled)
            {
                _drawnCount = 0;
                return;
            }

            DrawColliders();
            DrawMyReach();
        }

        /****************************************
        *              Collecting
        ****************************************/

        private void DrawColliders()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            int mask = BuildMask();
            if (mask == 0)
            {
                _drawnCount = 0;
                return;
            }

            // 트리거를 켜면 Collide, 끄면 Ignore. 물어보는 쪽에서 정해야 트리거를 아예 안 받는다
            QueryTriggerInteraction triggers = (_showTriggers)
                ? QueryTriggerInteraction.Collide
                : QueryTriggerInteraction.Ignore;

            int count = Physics.OverlapSphereNonAlloc(
                camera.transform.position, _radius, _buffer, mask, triggers);

            _drawnCount = 0;

            for (int i = 0; i < count; ++i)
            {
                Collider collider = _buffer[i];
                if ((collider == null) || (!collider.enabled))
                {
                    continue;
                }

                DrawOne(collider);
                ++_drawnCount;
            }
        }

        /// <summary> 켜 둔 갈래의 레이어만 모은다. 꺼진 갈래는 물리 조회 자체를 하지 않는다 </summary>
        private int BuildMask()
        {
            int mask = 0;

            if (_showPlayer)
            {
                mask |= LayerToMask("Player");
            }

            if (_showEnemy)
            {
                mask |= LayerToMask("Enemy");
                mask |= LayerToMask("Boss");
            }

            if (_showBarrier)
            {
                mask |= LayerToMask("BarrierCenter");
            }

            if (_showWorld)
            {
                mask |= LayerToMask("Ground");
                mask |= LayerToMask("Default");
                mask |= LayerToMask("Interactable");
            }

            if (_showArrows)
            {
                mask |= LayerToMask("Projectile");
            }

            return mask;
        }

        private static int LayerToMask(string name)
        {
            int layer = LayerMask.NameToLayer(name);

            // 프로젝트에 없는 레이어 이름이면 0을 돌려 마스크에 아무 영향을 주지 않는다
            return (layer < 0) ? 0 : (1 << layer);
        }

        /****************************************
        *              Drawing
        ****************************************/

        private void DrawOne(Collider collider)
        {
            GGizmoChannel channel = ResolveChannel(collider);
            Color         color   = ResolveColor(collider);

            switch (collider)
            {
                case BoxCollider box:
                    DrawBox(channel, box, color);
                    break;

                case SphereCollider sphere:
                    DrawSphere(channel, sphere, color);
                    break;

                case CapsuleCollider capsule:
                    DrawCapsule(channel, capsule, color);
                    break;

                default:
                    // 메시·지형 콜라이더를 선으로 옮기면 수천 개가 된다. 감싸는 상자만 그린다
                    Bounds bounds = collider.bounds;
                    GGizmo.Box(channel, bounds.center, bounds.size, Quaternion.identity, color);
                    break;
            }

            DrawPartId(channel, collider);
        }

        private static void DrawBox(GGizmoChannel channel, BoxCollider box, Color color)
        {
            Transform owner = box.transform;

            Vector3 center = owner.TransformPoint(box.center);
            Vector3 size   = Vector3.Scale(box.size, owner.lossyScale);

            GGizmo.Box(channel, center, size, owner.rotation, color);
        }

        private static void DrawSphere(GGizmoChannel channel, SphereCollider sphere, Color color)
        {
            Transform owner = sphere.transform;

            Vector3 center = owner.TransformPoint(sphere.center);
            float   radius = sphere.radius * MaxAbsScale(owner.lossyScale);

            GGizmo.Sphere(channel, center, radius, color);
        }

        /// <summary>
        /// 캡슐은 위아래 원과 그 둘을 잇는 세로선으로 그린다.
        /// 둥근 끝까지 그리면 선이 배로 늘고, 어디까지가 판정인지 보는 데는 이 정도로 충분하다
        /// </summary>
        private static void DrawCapsule(GGizmoChannel channel, CapsuleCollider capsule, Color color)
        {
            Transform owner = capsule.transform;
            Vector3   scale = owner.lossyScale;

            // 축은 0=X, 1=Y, 2=Z다
            Vector3 axis = (capsule.direction == 0) ? Vector3.right
                         : (capsule.direction == 2) ? Vector3.forward
                         : Vector3.up;

            float radius = capsule.radius * MaxAbsScale(scale);
            float height = Mathf.Max(capsule.height * AxisScale(scale, capsule.direction), radius * 2f);

            Vector3 center = owner.TransformPoint(capsule.center);
            Vector3 up     = owner.rotation * axis;

            float half = (height * 0.5f) - radius;

            Vector3 top    = center + (up * half);
            Vector3 bottom = center - (up * half);

            GGizmo.Circle(channel, top,    up, radius, color);
            GGizmo.Circle(channel, bottom, up, radius, color);

            // 옆면 네 줄. 어느 방향에서 봐도 기둥으로 보이게 한다
            Vector3 side = Vector3.Cross(up, Vector3.up);
            if (side.sqrMagnitude <= 0.0001f)
            {
                side = Vector3.Cross(up, Vector3.forward);
            }

            side = side.normalized * radius;

            Vector3 other = Vector3.Cross(up, side.normalized).normalized * radius;

            GGizmo.Line(channel, bottom + side,  top + side,  color);
            GGizmo.Line(channel, bottom - side,  top - side,  color);
            GGizmo.Line(channel, bottom + other, top + other, color);
            GGizmo.Line(channel, bottom - other, top - other, color);
        }

        /// <summary> 몬스터 타겟 박스는 부위 번호가 있어야 어디를 맞히는지 구분된다 </summary>
        private void DrawPartId(GGizmoChannel channel, Collider collider)
        {
            if (!_showPartIds)
            {
                return;
            }

            if (!collider.TryGetComponent(out ITargetPartIdSource part))
            {
                return;
            }

            // 월드 글자는 한글 글리프가 없는 폰트로 그려질 수 있어 ASCII로 적는다
            string text = "part " + part.TargetPartId + ((part.IsTargetable) ? string.Empty : " (off)");

            GGizmo.Label3D(channel, collider.bounds.center, text,
                (part.IsTargetable) ? Color.white : new Color(1f, 1f, 1f, 0.5f));
        }

        /// <summary> 내 근접 판정 반경과 각도. 콜라이더가 아니라 값으로만 있는 판정이라 따로 그린다 </summary>
        private void DrawMyReach()
        {
            if ((!_showMyReach) || (!GameDB.IsInitialized))
            {
                return;
            }

            NetworkIdentity local = NetworkClient.localPlayer;
            if (local == null)
            {
                return;
            }

            CombatConfigSO combat = GameDB.Combat;
            if (combat == null)
            {
                return;
            }

            bool isBell = ((local.TryGetComponent(out PlayerWeapon weapon)) && (weapon.HasWeapon)
                        && (weapon.Weapon == WeaponKind.Bell));

            float range = (isBell) ? combat.BellRange : combat.MeleeRange;
            float angle = (isBell) ? combat.BellAngle : combat.MeleeAngle;

            Vector3 origin = local.transform.position;

            if (angle >= Constants.Combat.FULL_CIRCLE_DEGREES)
            {
                GGizmo.Circle(GGizmoChannel.Combat, origin, Vector3.up, range, COLOR_REACH);
                return;
            }

            GGizmo.Arc(GGizmoChannel.Combat, origin, local.transform.forward, angle * 0.5f, range,
                COLOR_REACH);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 콜라이더의 레이어로 갈래를 정한다. 기즈모 패널의 갈래 토글과 함께 걸린다 </summary>
        private static GGizmoChannel ResolveChannel(Collider collider)
        {
            int layer = collider.gameObject.layer;

            if (layer == LayerMask.NameToLayer("Player"))
            {
                return GGizmoChannel.Combat;
            }

            if ((layer == LayerMask.NameToLayer("Enemy")) || (layer == LayerMask.NameToLayer("Boss")))
            {
                return GGizmoChannel.Monster;
            }

            if (layer == LayerMask.NameToLayer("BarrierCenter"))
            {
                return GGizmoChannel.Barrier;
            }

            return GGizmoChannel.Misc;
        }

        private static Color ResolveColor(Collider collider)
        {
            if (collider.isTrigger)
            {
                return COLOR_TRIGGER;
            }

            int layer = collider.gameObject.layer;

            if (layer == LayerMask.NameToLayer("Player"))
            {
                return COLOR_PLAYER;
            }

            if (layer == LayerMask.NameToLayer("Enemy"))
            {
                return COLOR_ENEMY;
            }

            if (layer == LayerMask.NameToLayer("Boss"))
            {
                return COLOR_BOSS;
            }

            if (layer == LayerMask.NameToLayer("BarrierCenter"))
            {
                return COLOR_BARRIER;
            }

            return COLOR_WORLD;
        }

        private static float MaxAbsScale(Vector3 scale)
        {
            return Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        }

        private static float AxisScale(Vector3 scale, int direction)
        {
            switch (direction)
            {
                case 0:  return Mathf.Abs(scale.x);
                case 2:  return Mathf.Abs(scale.z);
                default: return Mathf.Abs(scale.y);
            }
        }

        /****************************************
        *                Panel
        ****************************************/

        private void Build(DevPanelBuilder builder)
        {
            builder.Toggle("enabled", "콜라이더 그리기", _isEnabled);
            builder.Label($"지금 {_drawnCount}개 그린다 (최대 {MAX_COLLIDERS}개)");

            builder.Header("무엇을");
            builder.Toggle("player",  "플레이어",              _showPlayer);
            builder.Toggle("enemy",   "잡귀 · 보스 타겟 박스", _showEnemy);
            builder.Toggle("barrier", "결계",                  _showBarrier);
            builder.Toggle("world",   "지형 · 소품",           _showWorld);
            builder.Toggle("arrows",  "화살 (투사체)",         _showArrows);

            builder.Header("어떻게");
            builder.Toggle("triggers", "트리거도 (노란색)",     _showTriggers);
            builder.Toggle("partids",  "타겟 부위 번호 글자",   _showPartIds);
            builder.Toggle("myreach",  "내 근접 판정 범위",     _showMyReach);
            builder.Slider("radius",   "카메라에서 __ m 안까지", _radius, RADIUS_MIN, RADIUS_MAX);
        }

        private void Handle(DevCommand command)
        {
            switch (command.Control)
            {
                case "enabled":  _isEnabled    = command.IsOn; return;
                case "player":   _showPlayer   = command.IsOn; return;
                case "enemy":    _showEnemy    = command.IsOn; return;
                case "barrier":  _showBarrier  = command.IsOn; return;
                case "world":    _showWorld    = command.IsOn; return;
                case "arrows":   _showArrows   = command.IsOn; return;
                case "triggers": _showTriggers = command.IsOn; return;
                case "partids":  _showPartIds  = command.IsOn; return;
                case "myreach":  _showMyReach  = command.IsOn; return;

                case "radius":
                    if (command.TryGetFloat(out float radius))
                    {
                        _radius = Mathf.Clamp(radius, RADIUS_MIN, RADIUS_MAX);
                    }

                    return;
            }
        }

#endif
    }
}
