// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    public partial class DroppedItem
    {
        /****************************************
        *                Fields
        ****************************************/

        // struct는 통째로 바뀔 때만 동기화된다. 필드 하나만 고치지 않는다
        [SyncVar] private WeaponCombatSnapshot _equipment;
    }
}
