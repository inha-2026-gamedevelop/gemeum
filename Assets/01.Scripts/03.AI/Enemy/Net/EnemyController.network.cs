// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI
{
    public partial class EnemyController
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar(hook = nameof(OnVisualStateChanged))]
        private EnemyVisualState _visualState = EnemyVisualState.Idle;

        [SyncVar(hook = nameof(OnAttackVariantChanged))]
        private int _attackVariant = 0;

        /****************************************
        *             SyncVar Hooks
        ****************************************/

        /// <summary> _visualState 동기화 훅, 서버 · 클라이언트 양쪽에서 값이 바뀔 때 호출된다 </summary>
        private void OnVisualStateChanged(EnemyVisualState oldState, EnemyVisualState newState)
        {
            VisualStateChanged?.Invoke(newState);
        }

        /// <summary> _attackVariant 동기화 훅, 서버 · 클라이언트 양쪽에서 값이 바뀔 때 호출된다 </summary>
        private void OnAttackVariantChanged(int oldVariant, int newVariant)
        {
            AttackVariantChanged?.Invoke(newVariant);
        }
    }
}
