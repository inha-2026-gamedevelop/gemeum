// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Combat
{
    /// <summary> 신궁의 화살. 이동과 명중 판정을 모두 서버가 한다 </summary>
    public class ArrowProjectile : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int HIT_BUFFER_SIZE     = 8;
        private const int PIERCE_BUFFER_SIZE  = 8;

        private const float GUIDANCE_ABORT_ANGLE = 35f;

        private const uint NO_GUIDANCE_TARGET = 0u;

        private Vector3 _direction;
        private float   _speed;
        private float   _damage;
        private uint    _attackerNetId;
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

        // 화살마다 새로 만들면 연사할수록 GC 할당이 쌓인다
        private readonly RaycastHit[]      _hitBuffer      = new RaycastHit[HIT_BUFFER_SIZE];
        private readonly List<IDamageable> _piercedTargets = new List<IDamageable>(PIERCE_BUFFER_SIZE);

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 스폰 직전에 서버가 한 번 호출해 비행 조건을 확정한다 </summary>
        [Server]
        public void ServerInitialize(
            Vector3 direction, float damage, uint attackerNetId, int targetMask, ArrowConfigSO arrow,
            uint guidanceTargetNetId = NO_GUIDANCE_TARGET, byte guidanceTargetPart = 0)
        {
            _direction         = direction.normalized;
            _damage            = damage;
            _attackerNetId     = attackerNetId;
            _targetMask        = targetMask;
            _speed             = arrow.ArrowSpeed;
            _hitRadius         = arrow.UnlockedHitRadius;
            _remainingPierce   = Mathf.Max(1, arrow.BasePierceCount);
            _remainingLifetime = arrow.ArrowLifetime;
            _worldMask         = arrow.WorldMask;
            _initialized       = true;

            bool canGuide = ((guidanceTargetNetId != NO_GUIDANCE_TARGET) && (_remainingPierce <= 1));

            _guidanceTargetNetId      = (canGuide) ? guidanceTargetNetId : NO_GUIDANCE_TARGET;
            _guidanceTargetPart       = guidanceTargetPart;
            _guidanceRemaining        = (canGuide) ? arrow.GuidanceDuration : 0f;
            _guidanceDegreesPerSecond = arrow.GuidanceDegreesPerSecond;

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
            if (!_initialized)
            {
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

            if (!NetworkServer.spawned.TryGetValue(_guidanceTargetNetId, out NetworkIdentity identity))
            {
                return false;
            }

            if ((DamageTargetResolver.TryResolve(identity.transform, out IDamageable damageable)) && (damageable.IsDead))
            {
                return false;
            }

            IAimAssistTarget[] parts = identity.GetComponentsInChildren<IAimAssistTarget>(true);

            if ((_guidanceTargetPart < parts.Length) && (parts[_guidanceTargetPart] != null))
            {
                aimPoint = parts[_guidanceTargetPart].AimPoint;
                return true;
            }

            Collider collider = identity.GetComponentInChildren<Collider>();
            if (collider == null)
            {
                return false;
            }

            aimPoint = collider.bounds.center;
            return true;
        }

        [Server]
        private void StopGuidance()
        {
            _guidanceTargetNetId = NO_GUIDANCE_TARGET;
            _guidanceRemaining   = 0f;
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

            if (!Physics.SphereCast(
                    origin, _hitRadius, _direction, out RaycastHit wall, step, _worldMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            blockDistance = wall.distance;
            blockPoint    = origin + (_direction * wall.distance);
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
                damageable.TakeDamage(new DamageInfo(_damage, _attackerNetId, HitStrength.Light));

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
