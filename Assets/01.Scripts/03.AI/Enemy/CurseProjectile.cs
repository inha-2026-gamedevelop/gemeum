// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI
{
    /// <summary>
    /// 액귀의 저주 투사체. 서버가 이동과 명중 판정을 모두 수행하고, 클라이언트는 표현만 한다
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CurseProjectile : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private Vector3 _direction;
        private float   _speed;
        private float   _damage;
        private uint    _attackerNetId;
        private float   _remainingDistance;
        private bool    _initialized = false;

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 스폰 직후 서버가 한 번 호출해 발사 조건을 확정한다 </summary>
        [Server]
        public void Initialize(Vector3 direction, float speed, float damage, uint attackerNetId, float maxDistance)
        {
            _direction         = direction;
            _speed             = speed;
            _damage            = damage;
            _attackerNetId     = attackerNetId;
            _remainingDistance = maxDistance;
            _initialized       = true;
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            if ((!isServer) || (!_initialized))
            {
                return;
            }

            float step = _speed * Time.deltaTime;
            transform.position += _direction * step;
            _remainingDistance -= step;

            if (_remainingDistance <= 0f)
            {
                NetworkServer.Destroy(gameObject);
            }
        }

        [ServerCallback]
        private void OnTriggerEnter(Collider other)
        {
            if (!_initialized)
            {
                return;
            }

            if (other.gameObject.layer != LayerMask.NameToLayer(Constants.Layer.PLAYER))
            {
                return;
            }

            if (!other.TryGetComponent(out IDamageable damageable) || damageable.IsDead)
            {
                return;
            }

            damageable.TakeDamage(new DamageInfo(_damage, _attackerNetId, HitStrength.Light));
            NetworkServer.Destroy(gameObject);
        }
    }
}
