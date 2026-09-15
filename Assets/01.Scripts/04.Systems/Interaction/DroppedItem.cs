// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Systems
{
    /// <summary>
    /// 필드에 떨어진 장비. 서버가 값을 정해 스폰하고 클라이언트는 표시만 한다.
    /// 3인이 같은 드랍을 동시에 주워도 소유자가 하나만 남도록, 인벤토리에 넣기 전에 예약을 잡는다
    /// </summary>
    [DisallowMultipleComponent]
    public partial class DroppedItem : NetworkBehaviour, IInteractable
    {
        /****************************************
        *                Fields
        ****************************************/

        // 서버 전용. 예약이 잡히면 다른 요청은 실패한다
        private uint _reservedBy = 0u;
        private bool _isConsumed = false;

        /****************************************
        *              Properties
        ****************************************/

        public WeaponCombatSnapshot Equipment => _equipment;

        /// <summary> 누군가 가져갔거나 가져가는 중인가 </summary>
        public bool IsClaimed => ((_isConsumed) || (_reservedBy != 0u));

        public string PromptText => "줍기";

        /****************************************
        *              Server API
        ****************************************/

        /// <summary>
        /// 지금 이 플레이어가 주울 수 있는지. 안내 문구를 띄울지 판단할 때도 같은 경로를 쓴다.
        /// 여기서 통과해도 Interact가 성공한다는 보장은 없다 - 그 사이에 남이 가져갈 수 있다
        /// </summary>
        public bool CanInteract(uint playerNetId)
        {
            if (!isServer)
            {
                // 후보 표시만 한다. 비전투·생명·거리·예약은 요청을 받은 서버가 판정한다.
                return (isClient) && (playerNetId != 0u) && (_equipment.HasEquipment);
            }

            return (ServerEvaluate(playerNetId) == InteractionResult.Success);
        }
    }
}
