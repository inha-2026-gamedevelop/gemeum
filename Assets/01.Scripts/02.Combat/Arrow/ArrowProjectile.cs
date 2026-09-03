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

        private Vector3 _direction;
        private float   _speed;
        private float   _damage;
        private uint    _attackerNetId;
        private float   _hitRadius;
        private int     _remainingPierce;
        private float   _remainingLifetime;
        private int     _targetMask;
        private bool    _initialized = false;

        // 화살마다 새로 만들면 연사할수록 GC 할당이 쌓인다
        private readonly RaycastHit[] _hitBuffer     = new RaycastHit[HIT_BUFFER_SIZE];
        private readonly List<uint>   _piercedNetIds = new List<uint>(PIERCE_BUFFER_SIZE);

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 스폰 직전에 서버가 한 번 호출해 비행 조건을 확정한다 </summary>
        [Server]
        public void ServerInitialize(Vector3 direction, float damage, uint attackerNetId, int targetMask, ArrowConfigSO arrow)
        {
            _direction         = direction.normalized;
            _damage            = damage;
            _attackerNetId     = attackerNetId;
            _targetMask        = targetMask;
            _speed             = arrow.ArrowSpeed;
            _hitRadius         = arrow.UnlockedHitRadius;
            _remainingPierce   = Mathf.Max(1, arrow.BasePierceCount);
            _remainingLifetime = arrow.ArrowLifetime;
            _initialized       = true;

            // 화살을 풀에서 재사용하면 지난 비행의 명중 기록이 남아 그 대상만 영영 맞지 않는다
            _piercedNetIds.Clear();
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

            Vector3 origin = transform.position;
            float   step   = _speed * deltaTime;

            if (TryApplyHits(origin, step))
            {
                return; // 관통을 다 쓰고 사라졌다
            }

            transform.position = origin + (_direction * step);
        }

        /****************************************
        *                Methods
        ****************************************/

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
                Transform root = _hitBuffer[i].transform.root; // 콜라이더가 자식에 붙어 있는 경우가 있다

                if ((!root.TryGetComponent(out IDamageable damageable)) || (damageable.IsDead))
                {
                    continue;
                }

                // 몸 하나에 콜라이더가 여럿이면 한 번의 관통으로 여러 번 맞는다. netId로 한 번만 센다
                if (!root.TryGetComponent(out NetworkIdentity identity))
                {
                    continue;
                }

                if (_piercedNetIds.Contains(identity.netId))
                {
                    continue;
                }

                _piercedNetIds.Add(identity.netId);
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
