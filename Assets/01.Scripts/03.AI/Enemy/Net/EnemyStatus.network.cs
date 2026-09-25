// System
using System;

// 서드파티
using Mirror;

namespace Geumeum.AI
{
    public partial class EnemyStatus
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar(hook = nameof(OnHealthSynced))] private float _health;

        // 결계 강화와 몰이 강화가 바꾸므로 밸런스 원본으로 대신할 수 없다. 머리 위 체력 바가 비율을 만든다
        [SyncVar] private float _maxHealth;

        /****************************************
        *                Events
        ****************************************/

        /// <summary>
        /// 이 화면의 플레이어가 때렸다. 머리 위 체력 바는 때린 사람에게만 뜬다
        ///
        /// 체력 SyncVar보다 먼저 도착할 수 있다. 받는 쪽은 뒤따르는 HealthDecreased로 값을 다시 맞춘다
        /// </summary>
        public event Action HitByLocalPlayer;

        /****************************************
        *                 RPC
        ****************************************/

        [TargetRpc]
        private void TargetHitByLocalPlayer(NetworkConnection target)
        {
            HitByLocalPlayer?.Invoke();
        }
    }
}
