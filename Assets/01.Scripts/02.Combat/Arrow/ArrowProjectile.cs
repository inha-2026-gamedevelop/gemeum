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
    /// <summary>
    /// 신궁의 화살. 이동과 명중 판정을 모두 서버가 하고 클라이언트는 위치만 받아 그린다.
    /// 이동을 Update에 두면 클라이언트마다 프레임이 달라 같은 화살이 다른 적을 맞힌다
    /// </summary>
    public class ArrowProjectile : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int HIT_BUFFER_SIZE     = 8;
        private const int PIERCE_BUFFER_SIZE  = 8;

        // 대상 방향이 진행 방향에서 이만큼 벗어나면 유도를 끊는다.
        // 이 값이 90도를 넘지 않으므로 "목표가 화살 뒤쪽으로 넘어감"도 여기서 함께 걸린다
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

        // 락온 유도. 대상이 없으면 0이고 그때는 직선으로 날아간다.
        // 부위 번호가 따로 있는 이유는 보스 꼬리와 몸통이 같은 NetworkIdentity라
        // netId만으로는 "잠근 그 꼬리"를 가리킬 수 없기 때문이다
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

        /// <summary>
        /// 스폰 직전에 서버가 한 번 호출해 비행 조건을 확정한다.
        /// guidanceTargetNetId가 0이면 유도 없이 직선으로 날아간다 -
        /// 락온이 걸려 있을 때만 유도가 붙는다
        /// </summary>
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

            // 관통 화살은 유도를 받지 않는다. 여러 적을 뚫고 지나가야 하는 화살이 휘면
            // 두 번째 대상이 어디 있든 첫 대상 쪽으로 꺾이므로 물리적 필연이다
            bool canGuide = ((guidanceTargetNetId != NO_GUIDANCE_TARGET) && (_remainingPierce <= 1));

            _guidanceTargetNetId      = (canGuide) ? guidanceTargetNetId : NO_GUIDANCE_TARGET;
            _guidanceTargetPart       = guidanceTargetPart;
            _guidanceRemaining        = (canGuide) ? arrow.GuidanceDuration : 0f;
            _guidanceDegreesPerSecond = arrow.GuidanceDegreesPerSecond;

            // 화살을 풀에서 재사용하면 지난 비행의 명중 기록이 남아 그 대상만 영영 맞지 않는다
            _piercedTargets.Clear();
        }

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            // 화살은 런이 도는 중에만 생기므로 스케줄러가 이미 떠 있다.
            // 없다면 이동시킬 주체가 없어 영영 떠 있게 되므로 그 자리에서 없앤다
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

        // AuthorityTickScheduler가 물리 스텝마다 TICK_PRIORITY_PROJECTILE 순서로 호출한다
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

            // 방향을 먼저 정한다. 이동한 뒤에 꺾으면 이번 틱의 충돌 판정이 옛 방향으로 나간다
            UpdateGuidance(deltaTime);

            Vector3 origin = transform.position;
            float   step   = _speed * deltaTime;

            // 벽 뒤의 적을 맞히지 않는다. 장애물이 있으면 이번 구간을 거기까지로 자르고,
            // 그 앞의 대상만 판정한 뒤 벽에서 멈춘다
            bool  isBlocked   = TryFindWorldBlock(origin, step, out float blockDistance, out Vector3 blockPoint);
            float travelLimit = (isBlocked) ? blockDistance : step;

            if (TryApplyHits(origin, travelLimit))
            {
                return; // 관통을 다 쓰고 사라졌다
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

        /// <summary>
        /// 락온 대상 쪽으로 초당 최대 각도만큼만 방향을 돌린다.
        ///
        /// 비례 보간이 아니라 고정 각속도다(RotateTowards) - 비례로 하면 대상이 멀수록
        /// 더 세게 휘어 유도탄이 된다. 0.4초 동안 총 회전량이 최대 12도이고 30m/s 기준 그동안 12m를
        /// 나아가므로, 가까운 잡귀의 횡이동을 조금 따라갈 뿐 도망간 적을 추격하지 못한다
        /// </summary>
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

            // 크게 벗어났으면 따라가지 않는다. 35도를 넘는 순간 끊으므로
            // "목표가 화살 뒤쪽으로 넘어감"도 같은 조건에 걸린다
            if (Vector3.Angle(_direction, desired) > GUIDANCE_ABORT_ANGLE)
            {
                StopGuidance();
                return;
            }

            float maxRadians = _guidanceDegreesPerSecond * Mathf.Deg2Rad * deltaTime;

            _direction         = Vector3.RotateTowards(_direction, desired, maxRadians, 0f).normalized;
            transform.rotation = Quaternion.LookRotation(_direction); // 날아가는 쪽을 향해야 화살이 옆으로 미끄러지지 않는다
        }

        // 대상이 사라졌거나 죽었으면 유도할 것이 없다. 부위 번호까지 봐야 잠근 그 부위로 휜다
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

            // 계약이 없는 대상은 콜라이더 중심으로 폴백한다. 락온이 쓰는 규칙과 같아야
            // 카메라가 보는 점과 화살이 가는 점이 갈라지지 않는다
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

        /// <summary>
        /// 이번 틱 구간에 화살을 막는 지형이 있는지 본다. 판정 대상 마스크와 분리한 이유는
        /// 벽이 맞히는 대상이 아니라 멈추는 이유이기 때문이다.
        /// 마스크가 비어 있으면(설정 전) 이전처럼 장애물을 무시한다
        /// </summary>
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

        /// <summary>
        /// 이번 틱에 지나갈 구간을 훑어 맞은 대상에 피해를 준다. 화살이 소멸했으면 true.
        /// 한 틱 이동 거리가 판정 반경보다 커서, 제자리 구형 판정으로는 적을 그대로 통과해 버린다
        /// </summary>
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
                // 맞은 콜라이더에서 위로 올라가며 처음 만나는 IDamageable이 이번 관통의 대상이다.
                // 루트에서 찾으면 보스 꼬리 같은 부위 콜라이더를 쳐도 본체가 맞는다
                if ((!DamageTargetResolver.TryResolve(_hitBuffer[i].collider, out IDamageable damageable))
                    || (damageable.IsDead))
                {
                    continue;
                }

                // 몸 하나에 콜라이더가 여럿이면 한 번의 관통으로 여러 번 맞는다.
                // netId를 키로 쓰면 부위와 본체가 같은 NetworkIdentity라 둘 중 하나만 맞는다.
                // 해석된 참조 자체를 키로 쓰면 부위가 몇 개든 각각 한 번씩만 맞는다
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

                transform.position = _hitBuffer[i].point; // 마지막 명중 지점에서 멈춘 것으로 보이게 한다
                NetworkServer.Destroy(gameObject);
                return true;
            }

            return false;
        }

        // 가까운 것부터 처리해야 관통 횟수가 실제 통과 순서와 맞는다.
        // SphereCast 결과는 거리순이 아니고, 최대 8개라 삽입 정렬이 가장 싸다
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
