// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Dev;
using Geumeum.Network;

namespace Geumeum.Combat
{
    /// <summary> 신궁의 화살. 이동과 명중 판정을 모두 서버가 한다 </summary>
    public class ArrowProjectile : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float DIRECTION_EPSILON = 0.0001f; // 0 벡터로 LookRotation을 부르면 에러가 난다

        private const int HIT_BUFFER_SIZE     = 8;
        private const int PIERCE_BUFFER_SIZE  = 8;

        private const float GUIDANCE_ABORT_ANGLE = 35f;

        private const uint NO_GUIDANCE_TARGET = 0u;

        // 클라이언트가 스스로 같은 궤적을 그리도록 발사 조건만 보낸다. 유도로 방향이 바뀌면 그때 다시 간다
        [SyncVar] private Vector3 _direction;
        // 화살이 지나간 자리를 잠깐 남긴다. 한 프레임만 그리면 빠른 화살은 눈에 안 들어온다
        private const float GIZMO_SECONDS = 1f;

        [SyncVar] private float   _speed;
        private float   _damage;
        private float _basePower;
        private CombatStatsSnapshot _talents;
        private bool _followThroughArmed;
        private bool _followThroughUsed;
        private uint _ownerGeneration;
        private PlayerAttack _abilityOwner;
        private HitStrength _strength;
        private uint    _attackerNetId;
        private ParticipantId _attacker;
        private uint    _actionId;
        private bool    _isPrecision;
        private uint    _runIdValue;
        private uint    _sceneRevisionValue;
        private float   _hitRadius;
        private int     _remainingPierce;
        private float   _remainingLifetime;
        private int     _targetMask;
        private int     _worldMask;
        private bool    _initialized = false;

        // 락온 유도. 대상이 없으면 0이고 그때는 직선으로 날아간다
        private uint  _guidanceTargetNetId = NO_GUIDANCE_TARGET;
        private byte  _guidanceTargetPart  = 0;
        private float _guidanceRemaining   = 0f;
        private float _guidanceDegreesPerSecond = 0f;
        private NetworkIdentity _guidanceIdentity = null;
        private IAimAssistTarget _guidanceAim = null;
        private Component _guidanceAimComponent = null;
        private Collider _guidanceCollider = null;
        private IDamageable _guidanceDamageable = null;
        private Component _guidanceDamageComponent = null;
        private ITargetPartIdSource _guidancePartSource = null;
        private Component _guidancePartComponent = null;

        // 화살마다 새로 만들면 연사할수록 GC 할당이 쌓인다
        private readonly RaycastHit[]      _hitBuffer      = new RaycastHit[HIT_BUFFER_SIZE];
        private readonly RaycastHit[]      _worldHitBuffer = new RaycastHit[HIT_BUFFER_SIZE];
        private readonly List<IDamageable> _piercedTargets = new List<IDamageable>(PIERCE_BUFFER_SIZE);

        /****************************************
        *                Client
        ****************************************/

        // 서버는 위치를 매 틱 보내지 않는다. 방향과 속도만 받아 각자 같은 직선을 그린다
        // 판정은 서버에서만 하므로 여기 계산은 보여주기 전용이고, 틀려도 게임 결과가 갈리지 않는다
        private void Update()
        {
            IRunTimeSource runTime = RunTimeLocator.Current;
            if ((isServer) || (runTime.IsPaused)
                || (runTime.PauseReasons != RunPauseReason.None))
            {
                return;
            }

            if ((_speed <= 0f) || (_direction.sqrMagnitude <= DIRECTION_EPSILON))
            {
                return;
            }

            transform.position += _direction * (_speed * Time.deltaTime);
            transform.rotation  = Quaternion.LookRotation(_direction);
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 스폰 직전에 서버가 한 번 호출해 비행 조건을 확정한다 </summary>
        [Server]
        public void ServerInitialize(in ArrowFireSnapshot snapshot, ArrowConfigSO arrow)
        {
            _direction         = snapshot.Direction.normalized;
            _damage            = snapshot.Damage;
            _basePower = snapshot.BasePower;
            _talents = snapshot.Talents; _followThroughArmed = false; _followThroughUsed = false;
            if (NetworkServer.spawned.TryGetValue(snapshot.AttackerNetId, out NetworkIdentity owner))
            { _abilityOwner = owner.GetComponent<PlayerAttack>(); }
            _ownerGeneration = _abilityOwner != null ? _abilityOwner.CombatGeneration : 0;
            _strength          = snapshot.Strength;
            _attackerNetId     = snapshot.AttackerNetId;
            _attacker          = snapshot.Attacker;
            _actionId          = snapshot.ActionId;
            _isPrecision       = snapshot.IsPrecision;
            _runIdValue        = snapshot.RunIdValue;
            _sceneRevisionValue = snapshot.SceneRevisionValue;
            _targetMask        = snapshot.TargetMask;
            _speed             = snapshot.Speed;
            _hitRadius         = snapshot.HitRadius;
            _remainingPierce   = Mathf.Max(1, snapshot.PierceCount);
            _remainingLifetime = arrow.ArrowLifetime;
            _worldMask         = arrow.WorldMask;
            _initialized       = true;

            // 스폰 회전이 발사 방향과 다르면 첫 프레임에 옆을 보고 날아간다
            if (_direction.sqrMagnitude > DIRECTION_EPSILON)
            {
                transform.rotation = Quaternion.LookRotation(_direction);
            }

            bool canGuide = ((snapshot.GuidanceTargetNetId != NO_GUIDANCE_TARGET) && (_remainingPierce <= 1));

            _guidanceTargetNetId      = (canGuide) ? snapshot.GuidanceTargetNetId : NO_GUIDANCE_TARGET;
            _guidanceTargetPart       = snapshot.GuidanceTargetPart;
            _guidanceRemaining        = (canGuide) ? snapshot.GuidanceDuration : 0f;
            _guidanceDegreesPerSecond = arrow.GuidanceDegreesPerSecond;

            if (canGuide && (!CacheGuidanceTarget()))
            {
                StopGuidance();
            }

            _piercedTargets.Clear();
        }

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            if (AuthorityTickScheduler.ServerInstance == null)
            {
                Debug.LogError("[ArrowProjectile] 서버 틱 스케줄러가 없어 화살을 띄울 수 없다", this);
                NetworkServer.Destroy(gameObject);
                return;
            }

            AuthorityTickScheduler.ServerInstance.Register(Constants.Network.TICK_PRIORITY_PROJECTILE, ServerTick);
        }

        public override void OnStopServer()
        {
            if (AuthorityTickScheduler.ServerInstance == null)
            {
                return;
            }

            AuthorityTickScheduler.ServerInstance.Unregister(ServerTick);
        }

        /****************************************
        *              Server Tick
        ****************************************/

        private void ServerTick(float deltaTime)
        {
            if ((RunManager.ServerInstance != null && RunManager.ServerInstance.RunId.Value != _runIdValue) ||
                (RunSceneDirector.ServerInstance != null && RunSceneDirector.ServerInstance.Revision.Value != _sceneRevisionValue))
            { NetworkServer.Destroy(gameObject); return; }

            if (!_initialized)
            {
                return;
            }

            if (!IsCurrentRunContext())
            {
                NetworkServer.Destroy(gameObject);
                return;
            }

            _remainingLifetime -= deltaTime;
            if (_remainingLifetime <= 0f)
            {
                NetworkServer.Destroy(gameObject);
                return;
            }

            UpdateGuidance(deltaTime);

            Vector3 origin = transform.position;
            float   step   = _speed * deltaTime;

            bool  isBlocked   = TryFindWorldBlock(origin, step, out float blockDistance, out Vector3 blockPoint);
            float travelLimit = (isBlocked) ? blockDistance : step;

            // 화살은 콜라이더로 부딪히지 않고 이 구간을 쓸어 판정한다
            // 콜라이더만 그리면 정작 맞는 자리가 안 보이므로 쓸어 간 선을 남긴다
            GGizmo.Line(GGizmoChannel.Combat, origin, origin + (_direction * travelLimit),
                (isBlocked) ? Color.red : new Color(1f, 0.95f, 0.5f, 0.9f), GIZMO_SECONDS);

            if (TryApplyHits(origin, travelLimit))
            {
                return;
            }

            if (isBlocked)
            {
                transform.position = blockPoint;
                NetworkServer.Destroy(gameObject);
                return;
            }

            transform.position = origin + (_direction * step);
        }

        /****************************************
        *                Guidance
        ****************************************/

        /// <summary> 락온 대상 쪽으로 초당 최대 각도만큼만 방향을 돌린다 </summary>
        [Server]
        private void UpdateGuidance(float deltaTime)
        {
            if (_guidanceTargetNetId == NO_GUIDANCE_TARGET)
            {
                return;
            }

            _guidanceRemaining -= deltaTime;
            if (_guidanceRemaining <= 0f)
            {
                StopGuidance();
                return;
            }

            if (!TryResolveGuidanceAimPoint(out Vector3 aimPoint))
            {
                StopGuidance();
                return;
            }

            Vector3 toTarget = aimPoint - transform.position;
            if (toTarget.sqrMagnitude <= 0f)
            {
                StopGuidance();
                return;
            }

            Vector3 desired = toTarget.normalized;

            if (Vector3.Angle(_direction, desired) > GUIDANCE_ABORT_ANGLE)
            {
                StopGuidance();
                return;
            }

            float maxRadians = _guidanceDegreesPerSecond * Mathf.Deg2Rad * deltaTime;

            _direction         = Vector3.RotateTowards(_direction, desired, maxRadians, 0f).normalized;
            transform.rotation = Quaternion.LookRotation(_direction);
        }

        [Server]
        private bool TryResolveGuidanceAimPoint(out Vector3 aimPoint)
        {
            aimPoint = Vector3.zero;

            if ((!NetworkServer.spawned.TryGetValue(_guidanceTargetNetId, out NetworkIdentity identity)) ||
                (identity != _guidanceIdentity))
            {
                return false;
            }

            if ((_guidanceDamageable != null) &&
                ((_guidanceDamageComponent == null) || _guidanceDamageable.IsDead))
            {
                return false;
            }

            if (_guidanceTargetPart != TargetRef.BODY_PART_ID)
            {
                if ((_guidancePartComponent == null) || (_guidancePartSource == null) ||
                    (!_guidancePartSource.IsTargetable))
                {
                    return false;
                }
            }

            if ((_guidanceAimComponent != null) && (_guidanceAim != null))
            {
                aimPoint = _guidanceAim.AimPoint;
                return true;
            }

            if (_guidanceCollider == null)
            {
                return false;
            }

            aimPoint = _guidanceCollider.bounds.center;
            return true;
        }

        [Server]
        private bool CacheGuidanceTarget()
        {
            if (!NetworkServer.spawned.TryGetValue(_guidanceTargetNetId, out _guidanceIdentity))
            {
                return false;
            }

            Transform root = _guidanceIdentity.transform;
            if (DamageTargetResolver.TryResolve(root, out IDamageable damageable))
            {
                _guidanceDamageable = damageable;
                _guidanceDamageComponent = damageable as Component;
            }

            if (_guidanceTargetPart != TargetRef.BODY_PART_ID)
            {
                if ((!AimAssistTargetResolver.TryResolve(root, _guidanceTargetPart,
                    out _guidanceAim)) || (!(_guidanceAim is ITargetPartIdSource partSource)) ||
                    (!partSource.IsTargetable))
                {
                    return false;
                }

                _guidancePartSource = partSource;
                _guidancePartComponent = partSource as Component;
                _guidanceDamageable = _guidanceAim as IDamageable ?? _guidanceDamageable;
                _guidanceDamageComponent = _guidanceDamageable as Component;
            }
            else
            {
                AimAssistTargetResolver.TryResolve(root, TargetRef.BODY_PART_ID,
                    out _guidanceAim);
            }

            _guidanceAimComponent = _guidanceAim as Component;
            _guidanceCollider = (_guidanceAimComponent != null)
                ? _guidanceAimComponent.GetComponentInChildren<Collider>()
                : root.GetComponentInChildren<Collider>();
            return (_guidanceAimComponent != null) || (_guidanceCollider != null);
        }

        [Server]
        private void StopGuidance()
        {
            _guidanceTargetNetId = NO_GUIDANCE_TARGET;
            _guidanceRemaining   = 0f;
            _guidanceIdentity = null;
            _guidanceAim = null;
            _guidanceAimComponent = null;
            _guidanceCollider = null;
            _guidanceDamageable = null;
            _guidanceDamageComponent = null;
            _guidancePartSource = null;
            _guidancePartComponent = null;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이번 틱 구간에 화살을 막는 지형이 있는지 본다 </summary>
        [Server]
        private bool TryFindWorldBlock(Vector3 origin, float step, out float blockDistance, out Vector3 blockPoint)
        {
            blockDistance = step;
            blockPoint    = origin;

            if (_worldMask == 0)
            {
                return false;
            }

            int count = Physics.SphereCastNonAlloc(
                origin, _hitRadius, _direction, _worldHitBuffer, step,
                _worldMask, QueryTriggerInteraction.Ignore);

            if (count <= 0)
            {
                return false;
            }

            // 버퍼 포화 때 가까운 벽이 누락됐을 수 있으므로 그 자리에서 막힌 것으로 처리한다
            if (count >= _worldHitBuffer.Length)
            {
                blockDistance = 0f;
                blockPoint    = origin;
                return true;
            }

            float nearestDistance = step;
            for (int i = 0; i < count; ++i)
            {
                if (_worldHitBuffer[i].distance < nearestDistance)
                {
                    nearestDistance = _worldHitBuffer[i].distance;
                }
            }

            blockDistance = nearestDistance;
            blockPoint    = origin + (_direction * nearestDistance);
            return true;
        }

        /// <summary> 이번 틱에 지나갈 구간을 훑어 맞은 대상에 피해를 준다. 화살이 소멸했으면 true </summary>
        [Server]
        private bool TryApplyHits(Vector3 origin, float step)
        {
            int count = Physics.SphereCastNonAlloc(
                origin, _hitRadius, _direction, _hitBuffer, step, _targetMask, QueryTriggerInteraction.Ignore);

            if (count <= 0)
            {
                return false;
            }

            // 포화된 결과에는 가장 가까운 대상이 있다는 보장이 없어 피해 없이 제거한다
            if (count >= _hitBuffer.Length)
            {
                NetworkServer.Destroy(gameObject);
                return true;
            }

            SortByDistance(count);

            for (int i = 0; i < count; ++i)
            {
                if ((!DamageTargetResolver.TryResolve(_hitBuffer[i].collider, out IDamageable damageable))
                    || (damageable.IsDead))
                {
                    continue;
                }

                if (_piercedTargets.Contains(damageable))
                {
                    continue;
                }

                _piercedTargets.Add(damageable);
                float healthBefore = damageable is IDamageHealthSource health ? health.Health : 0f;
                bool followThrough = _followThroughArmed && damageable is Geumeum.AI.EnemyStatus &&
                    _abilityOwner != null && _abilityOwner.CanApplyArrowTalent && _abilityOwner.CombatGeneration == _ownerGeneration;
                if (followThrough) { _followThroughArmed = false; _followThroughUsed = true; }
                damageable.TakeDamage(new DamageInfo(
                    _damage,
                    _attackerNetId,
                    _attacker,
                    _strength));

                if (damageable is IDamageHealthSource after && after.Health < healthBefore)
                {
                    _abilityOwner?.ServerPresentArrowHit(_hitBuffer[i].point);
                    // Broad phase consumes the arrow before its center reaches the surface
                    // Extend only through its contact skin; keep the narrow center-line test
                    float narrowDistance = step + _hitRadius;
                    if (Physics.Raycast(origin, _direction, out RaycastHit wall, narrowDistance, _worldMask, QueryTriggerInteraction.Ignore))
                    { narrowDistance = wall.distance; }
                    bool burst = PlayerAttack.ServerNotifyDirectHit(_attackerNetId, _actionId, _isPrecision, false,
                        damageable, _hitBuffer[i].point, origin, _direction, narrowDistance, _basePower, _talents, _ownerGeneration);
                    if (followThrough)
                    {
                        damageable.TakeDamage(new DamageInfo(_basePower * _talents.PrecisionFollowThrough,
                            _attackerNetId, _attacker, HitStrength.Heavy));
                    }

                    if (burst && !_followThroughUsed && _isPrecision && _talents.PrecisionFollowThrough > 0)
                    {
                        _followThroughArmed = true;
                    }
                }

                if (_isPrecision)
                {
                    TryInterruptPrecisionTarget(_hitBuffer[i].collider);
                }

                --_remainingPierce;
                if (_remainingPierce > 0)
                {
                    continue;
                }

                transform.position = _hitBuffer[i].point;
                NetworkServer.Destroy(gameObject);
                return true;
            }

            return false;
        }

        [Server]
        private void TryInterruptPrecisionTarget(Collider hit)
        {
            if ((_actionId == 0u) || (!_attacker.IsValid) || (hit == null))
            {
                return;
            }

            IPrecisionInterruptTarget target = hit.GetComponentInParent<IPrecisionInterruptTarget>();
            if (target == null)
            {
                return;
            }

            TargetRef targetRef = target.PrecisionTargetRef;
            if ((!targetRef.IsValid)
                || (targetRef.RunIdValue != _runIdValue)
                || (targetRef.SceneRevisionValue != _sceneRevisionValue))
            {
                return;
            }

            target.TryInterruptPrecisionShot(new PrecisionInterruptContext(
                _actionId,
                _attacker,
                _attackerNetId,
                targetRef,
                true,
                NetworkTime.time));
        }

        private bool IsCurrentRunContext()
        {
            RunManager       run      = RunManager.ServerInstance;
            RunSceneDirector director = RunSceneDirector.ServerInstance;
            return (run != null) && (director != null)
                && PrecisionShotRules.IsCurrentRequestContext(
                    _runIdValue,
                    _sceneRevisionValue,
                    run.RunId.Value,
                    director.Revision.Value);
        }

        private void SortByDistance(int count)
        {
            for (int i = 1; i < count; ++i)
            {
                RaycastHit current = _hitBuffer[i];
                int        j       = i - 1;

                while ((j >= 0) && (_hitBuffer[j].distance > current.distance))
                {
                    _hitBuffer[j + 1] = _hitBuffer[j];
                    --j;
                }

                _hitBuffer[j + 1] = current;
            }
        }
    }
}
