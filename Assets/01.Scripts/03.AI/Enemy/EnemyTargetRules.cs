// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI
{
    /// <summary>일반 적이 추적하거나 공격할 수 없는 플레이어 상태를 판정한다.</summary>
    public static class EnemyTargetRules
    {
        public static bool IsUnavailable(IDamageable damageable)
        {
            Component component = damageable as Component;
            if ((damageable == null) || (component == null) || damageable.IsDead)
            {
                return true;
            }

            return (damageable is IBossSwallowTarget swallowTarget) &&
                swallowTarget.IsSwallowed;
        }
    }
}
