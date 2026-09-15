// System
using System;

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
    /// <summary> 고정 꼬리 PartId의 HP, 한 그로기 한 파괴, 봉인과 드랍 사실을 소유한다 </summary>
    public sealed partial class BossPartController : NetworkBehaviour, IBossPartSource
    {
        private const byte ALL_TAILS_BROKEN_MASK = 0b0000_0111;

        [SerializeField] private BossController _boss;
        [SerializeField] private BossStatus     _status;
        [SerializeField] private BossPartHitbox[] _hitboxes = Array.Empty<BossPartHitbox>();

        private BossPatternSO _data;

        public BossPartSnapshot PartSnapshot => _snapshot;
        public event Action<BossPartSnapshot> PartChanged;

        public bool AllPartsBroken => ((_snapshot.BrokenMask & ALL_TAILS_BROKEN_MASK) ==
            ALL_TAILS_BROKEN_MASK);

        private void Awake()
        {
            if (_boss == null)
            {
                TryGetComponent(out _boss);
            }

            if (_status == null)
            {
                TryGetComponent(out _status);
            }
        }

        private void NotifyPartChanged(BossPartSnapshot snapshot)
        {
            ObserverDispatch.Invoke(PartChanged, snapshot, this,
                nameof(BossPartController), nameof(PartChanged));
        }

        private void ApplyHitboxes(in BossPartSnapshot snapshot)
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
            NotifyPartChanged(_snapshot);
        }

        /****************************************
        *               ClientRpc
        ****************************************/

        [ClientRpc]
        private void RpcPlayPartBreak()
        {
            CombatFeedbackLocator.Current.PlayPartBreak();
        }

        /****************************************
        *             SyncVar Hooks
        ****************************************/

        private void HandlePartSnapshotChanged(BossPartSnapshot previous, BossPartSnapshot current)
        {
            ApplyHitboxes(current);
            NotifyPartChanged(current);
        }
    }
}
