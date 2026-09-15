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
    public partial class CurseProjectile : NetworkBehaviour
    {

        [SerializeField] private SphereCollider _collider;

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
    }
}
