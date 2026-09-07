// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Network;

namespace Geumeum.AI
{
    /// <summary>
    /// 액귀의 저주 투사체. 서버가 이동과 명중 판정을 모두 수행하고, 클라이언트는 표현만 한다.
    /// AuthorityTickScheduler의 고정 틱에서 SphereCast로 구간을 훑어 이동한다,
    /// Update에서 이동하면 호스트 프레임에 따라 결과가 갈리고 고속 이동 시 터널링도 생긴다
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class CurseProjectile : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float DIRECTION_EPSILON = 0.0001f; // 0 벡터로 LookRotation을 부르면 에러가 난다

        private const int HIT_BUFFER_SIZE = 4; // 단일 대상 관통이라 넉넉하게 잡은 NonAlloc 버퍼 크기

        [SerializeField] private SphereCollider _collider;

        // 클라이언트가 스스로 같은 직선을 그리도록 발사 조건만 보낸다
        [SyncVar] private Vector3 _direction;
        [SyncVar] private float   _speed;
        private float   _damage;
        private uint    _attackerNetId;
        private float   _remainingDistance;
        private int     _targetMask;
        private bool    _initialized = false;

        // 투사체마다 새로 만들면 연사할수록 GC 할당이 쌓인다
        private readonly RaycastHit[] _hitBuffer = new RaycastHit[HIT_BUFFER_SIZE];

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_collider == null)
            {
                _collider = GetComponent<SphereCollider>();
            }

            _targetMask = LayerMask.GetMask(Constants.Layer.PLAYER);
        }

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

            // 스폰 회전이 발사 방향과 다르면 첫 프레임에 옆을 보고 날아간다
            if (_direction.sqrMagnitude > DIRECTION_EPSILON)
            {
                transform.rotation = Quaternion.LookRotation(_direction);
            }
        }

        /****************************************
        *                Client
        ****************************************/

        // 서버는 위치를 매 틱 보내지 않는다. 방향과 속도만 받아 각자 같은 직선을 그린다.
        // 판정은 서버에서만 하므로 여기 계산은 보여주기 전용이고, 틀려도 게임 결과가 갈리지 않는다
        private void Update()
        {
            if (isServer)
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
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            // 저주 투사체는 런이 도는 중에만 생기므로 스케줄러가 이미 떠 있다.
            // 없다면 이동시킬 주체가 없어 영영 떠 있게 되므로 그 자리에서 없앤다
            if (AuthorityTickScheduler.ServerInstance == null)
            {
                Debug.LogError("[CurseProjectile] 서버 틱 스케줄러가 없어 투사체를 띄울 수 없다", this);
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

            Vector3 origin = transform.position;
            float   step   = Mathf.Min(_speed * deltaTime, _remainingDistance);

            if (TryApplyHit(origin, step))
            {
                return; // 명중해서 사라졌다
            }

            transform.position  = origin + (_direction * step);
            _remainingDistance -= step;

            if (_remainingDistance <= 0f)
            {
                NetworkServer.Destroy(gameObject);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // 제자리 구형 판정이 아니라 이번 틱 구간을 훑어야, 한 틱 이동 거리가 판정 반경보다 커도
        // 대상을 그대로 통과하지 않는다. 명중하면 위치를 명중 지점으로 맞춘 뒤 그 자리에서 없앤다
        [Server]
        private bool TryApplyHit(Vector3 origin, float step)
        {
            int count = Physics.SphereCastNonAlloc(
                origin, _collider.radius, _direction, _hitBuffer, step, _targetMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; ++i)
            {
                if ((!DamageTargetResolver.TryResolve(_hitBuffer[i].collider, out IDamageable damageable))
                    || (damageable.IsDead))
                {
                    continue;
                }

                transform.position = origin + (_direction * _hitBuffer[i].distance);
                damageable.TakeDamage(new DamageInfo(_damage, _attackerNetId, HitStrength.Light));
                NetworkServer.Destroy(gameObject);
                return true;
            }

            return false;
        }
    }
}
