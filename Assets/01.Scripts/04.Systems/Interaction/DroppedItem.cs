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
    public class DroppedItem : NetworkBehaviour, IInteractable
    {
        /****************************************
        *                Fields
        ****************************************/

        // struct는 통째로 바뀔 때만 동기화된다. 필드 하나만 고치지 않는다
        [SyncVar] private WeaponCombatSnapshot _equipment;

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

        /// <summary> 스폰 직후 서버가 값을 심는다. 클라이언트는 무기 값을 보내지 않는다 </summary>
        [Server]
        public void ServerInitialize(in WeaponCombatSnapshot equipment)
        {
            _equipment  = equipment;
            _reservedBy = 0u;
            _isConsumed = false;
        }

        /// <summary>
        /// 지금 이 플레이어가 주울 수 있는지. 안내 문구를 띄울지 판단할 때도 같은 경로를 쓴다.
        /// 여기서 통과해도 Interact가 성공한다는 보장은 없다 - 그 사이에 남이 가져갈 수 있다
        /// </summary>
        public bool CanInteract(uint playerNetId)
        {
            return (ServerEvaluate(playerNetId) == InteractionResult.Success);
        }

        /// <summary> 줍기를 실행한다. 서버에서만 호출한다 </summary>
        [Server]
        public void Interact(uint playerNetId)
        {
            ServerTryPickup(playerNetId, out _);
        }

        /// <summary>
        /// 실패 사유가 필요한 호출자를 위한 형태.
        /// 검증 -> 예약 -> 인벤토리 반영 -> despawn 순서이며, 예약 뒤 실패하면 예약을 풀고 원본을 남긴다
        /// </summary>
        [Server]
        public bool ServerTryPickup(uint playerNetId, out InventoryResult inventoryResult)
        {
            inventoryResult = InventoryResult.InvalidItem;

            if (ServerEvaluate(playerNetId) != InteractionResult.Success)
            {
                return false;
            }

            if (!TryGetComponentOnPlayer(playerNetId, out InventoryController inventory))
            {
                return false;
            }

            // 넣는 도중 같은 드랍에 다른 요청이 들어오면 안 된다
            _reservedBy = playerNetId;

            inventoryResult = inventory.ServerTryStoreEquipment(_equipment, inventory.Revision);
            if (inventoryResult != InventoryResult.Success)
            {
                // 칸이 찼거나 revision이 어긋났다. 드랍은 그대로 남는다
                _reservedBy = 0u;
                return false;
            }

            _isConsumed = true;
            NetworkServer.Destroy(gameObject);
            return true;
        }

        /****************************************
        *               Internals
        ****************************************/

        [Server]
        private InteractionResult ServerEvaluate(uint playerNetId)
        {
            if (IsClaimed)
            {
                return InteractionResult.InvalidRequest;
            }

            if (!NetworkServer.spawned.TryGetValue(playerNetId, out NetworkIdentity identity))
            {
                return InteractionResult.InvalidRequest;
            }

            if (!GameDB.IsInitialized)
            {
                Debug.LogError("[DroppedItem] GameDB가 없어 상호작용 조건을 읽지 못했다", this);
                return InteractionResult.InvalidRequest;
            }

            // 파트 경계를 넘지 않으려고 PlayerStatus 대신 Common 인터페이스로 생명을 본다
            var damageable = identity.GetComponent<IDamageable>();
            if (damageable == null)
            {
                return InteractionResult.InvalidRequest;
            }

            CombatConfigSO combat = GameDB.Combat;
            float distanceSquared = (identity.transform.position - transform.position).sqrMagnitude;

            var request = new InteractionRequest(distanceSquared, combat.InteractRange,
                !damageable.IsDead, IsRunActive(), NetworkTime.time,
                CombatClock.GetLastCombatTime(playerNetId), combat.InteractOutOfCombatSeconds);

            return InteractionRules.Evaluate(request);
        }

        /// <summary> 런이 진행 중인가. 로비나 결과 화면에서는 줍지 못한다 </summary>
        private static bool IsRunActive()
        {
            RunManager run = RunManager.ServerInstance;
            if (run == null)
            {
                return false;
            }

            return ((run.Phase != RunPhase.Lobby) && (run.Phase != RunPhase.Result));
        }

        private static bool TryGetComponentOnPlayer<T>(uint playerNetId, out T component) where T : Component
        {
            component = null;
            if (!NetworkServer.spawned.TryGetValue(playerNetId, out NetworkIdentity identity))
            {
                return false;
            }

            component = identity.GetComponent<T>();
            return (component != null);
        }
    }
}
