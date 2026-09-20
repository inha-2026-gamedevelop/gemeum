// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.AI
{
    /// <summary>
    /// 보스 패턴 실행 템플릿의 서버 판정. 템플릿마다 클래스를 두지 않고 이 안에
    /// 재사용 NonAlloc 질의 메서드만 둔다.
    /// 지금은 MeleeArc 하나만 있다, 나머지 세 템플릿은 후속 슬라이스에서 채운다.
    /// 보스는 항상 한 마리뿐이라 정적 버퍼를 안전하게 재사용할 수 있다
    /// </summary>
    internal static class BossPatternRunner
    {
        private const int BUFFER_SIZE = Constants.Network.MAX_PLAYERS + 1; // 여유 1

        private static readonly Collider[]    s_hitBuffer  = new Collider[BUFFER_SIZE];
        private static readonly IDamageable[] s_dedupeSeen = new IDamageable[BUFFER_SIZE];

        /// <summary>
        /// 원점 앞 부채꼴 범위의 살아있는 대상 전원에게 피해를 적용한다.
        /// 같은 호출 안에서 같은 대상이 여러 콜라이더로 겹쳐도 한 번만 맞는다
        /// </summary>
        public static void ExecuteMeleeArc(
            Vector3 origin,
            Vector3 forward,
            float   range,
            float   angleDegrees,
            float   damage,
            uint    attackerNetId,
            int     targetLayerMask,
            BossController damageRouter = null,
            HitStrength hitStrength = HitStrength.Heavy)
        {
            int hitCount  = Physics.OverlapSphereNonAlloc(origin, range, s_hitBuffer, targetLayerMask);
            int seenCount = 0;

            float halfAngle = angleDegrees * 0.5f;

            for (int i = 0; i < hitCount; ++i)
            {
                if ((!DamageTargetResolver.TryResolve(s_hitBuffer[i], out IDamageable damageable)) || (damageable.IsDead))
                {
                    continue;
                }

                if (IsAlreadySeen(damageable, seenCount))
                {
                    continue;
                }

                Vector3 toTarget = s_hitBuffer[i].transform.position - origin;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    float angle = Vector3.Angle(forward, toTarget);
                    if (angle > halfAngle)
                    {
                        continue; // 부채꼴 밖이다
                    }
                }

                if (seenCount < s_dedupeSeen.Length)
                {
                    s_dedupeSeen[seenCount] = damageable;
                    ++seenCount;
                }

                var damageInfo = new DamageInfo(damage, attackerNetId, hitStrength);
                if (damageRouter != null)
                {
                    damageRouter.ServerApplyPatternDamage(damageable, damageInfo);
                }
                else
                {
                    damageable.TakeDamage(damageInfo);
                }
            }
        }

        private static bool IsAlreadySeen(IDamageable damageable, int seenCount)
        {
            for (int i = 0; i < seenCount; ++i)
            {
                if (ReferenceEquals(s_dedupeSeen[i], damageable))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
