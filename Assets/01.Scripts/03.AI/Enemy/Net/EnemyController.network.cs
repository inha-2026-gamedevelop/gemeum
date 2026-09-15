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

        [SyncVar(hook = nameof(OnParryWindowVisuallyOpenChanged))]
        private bool _isParryWindowVisuallyOpen = false;
    }
}
