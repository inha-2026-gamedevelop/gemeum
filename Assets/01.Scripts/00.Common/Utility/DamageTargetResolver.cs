// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 피해 판정이 "무엇을 맞혔는가"를 해석하는 단일 창구다.
    ///
    /// 이 규칙이 한곳에 없으면 판정 경로마다 다르게 찾게 되고, 실제로 그런 상태였다 —
    /// 근접과 화살은 transform.root에서 찾고, 액귀 저주와 어그로 스캔은 콜라이더 본인에서만 찾았다.
    /// 플레이어와 잡귀는 IDamageable도 콜라이더도 루트에 있어 지금까지는 어느 쪽이든 결과가 같았지만,
    /// 보스 꼬리처럼 자식이 독립된 피해 대상이 되는 순간 경로마다 다르게 깨진다.
    ///
    /// 규칙: 맞은 콜라이더에서 시작해 부모로 올라가며 처음 만나는 IDamageable에서 멈춘다.
    /// 부위가 붙은 자식이 있으면 부위가 맞고, 없으면 루트까지 올라가 본체가 맞는다.
    /// 부위가 없는 대상에서는 동작이 바뀌지 않는다.
    ///
    /// 이것은 "무엇이 맞았는가"를 정할 뿐이고 "내 것인가"를 가리는 소유자 판별과는 다른 문제다.
    /// 자기 자신 제외는 transform.root 비교를 그대로 쓴다 - 내 무기 콜라이더가 자식에 있어도 걸러야 한다.
    /// 둘을 섞지 않는다.
    /// </summary>
    public static class DamageTargetResolver
    {
        /// <summary> 물리 질의가 돌려준 콜라이더에서 이번 타격의 대상을 해석한다 </summary>
        public static bool TryResolve(Collider hit, out IDamageable damageable)
        {
            if (hit == null)
            {
                damageable = null;
                return false;
            }

            // GetComponentInParent는 자기 자신부터 보므로, 콜라이더에 직접 붙어 있으면 그것이 대상이다
            damageable = hit.GetComponentInParent<IDamageable>();
            return (damageable != null);
        }

        /// <summary>
        /// 이미 Transform으로 좁혀진 대상에서 해석한다.
        /// 잡귀가 추적 중인 대상처럼 콜라이더가 아니라 Transform을 들고 있는 경로가 있다
        /// </summary>
        public static bool TryResolve(Transform origin, out IDamageable damageable)
        {
            if (origin == null)
            {
                damageable = null;
                return false;
            }

            damageable = origin.GetComponentInParent<IDamageable>();
            return (damageable != null);
        }
    }
}
