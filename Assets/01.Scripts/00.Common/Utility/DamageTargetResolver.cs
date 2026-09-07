// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 피해 판정이 "무엇을 맞혔는가"를 해석하는 단일 창구.
    /// 맞은 콜라이더에서 부모로 올라가며 처음 만나는 IDamageable에서 멈춘다
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

            damageable = hit.GetComponentInParent<IDamageable>();
            return (damageable != null);
        }

        /// <summary> 이미 Transform으로 좁혀진 대상에서 해석한다 </summary>
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
