// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.AI
{
    /// <summary> 원혼 셋의 독립 HP, 누적 제거, 실패 공격과 처치 가능 전이를 소유한다 </summary>
    public sealed partial class BossSoulController : NetworkBehaviour, IBossSoulSource
    {
        private const byte ALL_SOULS_REMOVED_MASK = 0b0000_0111;

        [SerializeField] private BossController _boss;
        [SerializeField] private BossSoulHitbox[] _hitboxes = Array.Empty<BossSoulHitbox>();

        private readonly List<ParticipantId> _markOwners =
            new List<ParticipantId>(Constants.Network.MAX_PLAYERS);
        private BossPatternSO _data;

        public BossSoulSnapshot SoulSnapshot => _snapshot;
        public event Action<BossSoulSnapshot> SoulChanged;

        private void Awake()
        {
            if (_boss == null)
            {
                TryGetComponent(out _boss);
            }
        }

        private void NotifySoulChanged(BossSoulSnapshot snapshot)
        {
            ObserverDispatch.Invoke(SoulChanged, snapshot, this,
                nameof(BossSoulController), nameof(SoulChanged));
        }

        private void ApplyHitboxes(in BossSoulSnapshot snapshot)
        {
            if (_hitboxes == null)
            {
                return;
            }

            for (int i = 0; i < _hitboxes.Length; ++i)
            {
                _hitboxes[i]?.ApplySnapshot(snapshot);
            }
        }

        /****************************************
        *           Client Callbacks
        ****************************************/

        public override void OnStartClient()
        {
            ApplyHitboxes(_snapshot);
            NotifySoulChanged(_snapshot);
        }

        /****************************************
        *             SyncVar Hooks
        ****************************************/

        private void HandleSoulSnapshotChanged(BossSoulSnapshot previous, BossSoulSnapshot current)
        {
            ApplyHitboxes(current);
            NotifySoulChanged(current);
        }
    }
}
