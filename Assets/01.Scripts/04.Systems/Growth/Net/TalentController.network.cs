// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    public partial class TalentController
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly SyncList<int> _nodeIds       = new SyncList<int>();

        [SyncVar] private bool       _hasWeapon;
        [SyncVar] private WeaponKind _weapon;
        [SyncVar] private int        _spentPoints;
        [SyncVar(hook = nameof(OnRevisionChanged))] private uint _revision;

        /****************************************
        *               Commands
        ****************************************/

        [Command]
        private void CmdAllocate(int nodeId, uint expectedRevision)
        {
            TargetAllocationResult(ServerTryAllocate(nodeId, expectedRevision));
        }

        [Command]
        private void CmdReset(uint expectedRevision)
        {
            TargetAllocationResult(ServerTryReset(expectedRevision));
        }
    }
}
