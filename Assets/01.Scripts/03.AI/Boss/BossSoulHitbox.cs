// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI
{
    /// <summary> 원혼 하나의 고정 ID 피해, 정밀 사격, 조준 접점 </summary>
    public sealed class BossSoulHitbox : MonoBehaviour, IDamageable, IAimAssistTarget,
        IPrecisionInterruptTarget, ITargetPartIdSource
    {
        [SerializeField] private BossSoulController _souls;
        [SerializeField] private byte                _soulId = 1;
        [SerializeField] private Vector3             _aimOffset = Vector3.zero;
        [SerializeField] private Collider            _hitCollider;
        [SerializeField] private GameObject          _visualRoot;

        public bool IsDead => (_souls == null) || _souls.SoulSnapshot.IsRemoved(_soulId);
        public Vector3 AimPoint => transform.TransformPoint(_aimOffset);
        public TargetRef PrecisionTargetRef => CreateTargetRef();
        public byte TargetPartId => BossTargetPartIds.SoulTargetPartId(_soulId);
        public bool IsTargetable => (_souls != null) && (!IsDead) &&
            ((_souls.SoulSnapshot.State == BossFinalState.SoulWindow) ||
                (_souls.SoulSnapshot.State == BossFinalState.FailureAttack));

        private void Awake()
        {
            if (_souls == null)
            {
                _souls = GetComponentInParent<BossSoulController>();
            }

            if (_hitCollider == null)
            {
                TryGetComponent(out _hitCollider);
            }

            if (_souls != null)
            {
                ApplySnapshot(_souls.SoulSnapshot);
            }
        }

        public void TakeDamage(in DamageInfo damage)
        {
            _souls?.ServerApplyDamage(_soulId, damage);
        }

        public bool TryInterruptPrecisionShot(in PrecisionInterruptContext context)
        {
            return (_souls != null) && context.IsPrecision &&
                context.Target.Equals(PrecisionTargetRef) &&
                _souls.ServerTryCounterSoul(_soulId, CharacterKind.Singung,
                    context.Attacker, context.ActionId);
        }

        internal void ApplySnapshot(in BossSoulSnapshot snapshot)
        {
            bool active = ((snapshot.State == BossFinalState.SoulWindow) ||
                (snapshot.State == BossFinalState.FailureAttack)) &&
                (!snapshot.IsRemoved(_soulId));
            if (_hitCollider != null)
            {
                _hitCollider.enabled = active;
            }

            if ((_visualRoot != null) && (_visualRoot != gameObject))
            {
                _visualRoot.SetActive(active);
            }
        }

        private TargetRef CreateTargetRef()
        {
            if (_souls == null)
            {
                return default;
            }

            Geumeum.Network.RunManager run = Geumeum.Network.RunManager.ServerInstance;
            Geumeum.Network.RunSceneDirector director =
                Geumeum.Network.RunSceneDirector.ServerInstance;
            return TargetRef.Create(
                (run == null) ? default : run.RunId,
                (director == null) ? default : director.Revision,
                _souls.netId,
                BossTargetPartIds.SoulTargetPartId(_soulId));
        }
    }
}
