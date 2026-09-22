// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI
{
    /// <summary> 시각 꼬리 본과 분리된 고정 서버 판정 콜라이더 </summary>
    public sealed class BossPartHitbox : MonoBehaviour, IDamageable, IDamageHealthSource, IAimAssistTarget,
        ITargetPartIdSource
    {
        [SerializeField] private BossPartController _parts;
        [SerializeField] private byte               _partId = BossTargetPartIds.Tail1;
        [SerializeField] private Vector3            _aimOffset = Vector3.zero;
        [SerializeField] private Collider           _hitCollider;
        [SerializeField] private GameObject         _visualRoot;

        public byte PartId => _partId;
        public float Health => _parts == null ? 0f : _parts.PartSnapshot.GetHealth(_partId);
        public bool IsDead => (_parts == null) || _parts.PartSnapshot.IsBroken(_partId);
        public Vector3 AimPoint => transform.TransformPoint(_aimOffset);
        public byte TargetPartId => _partId;
        public bool IsTargetable => (_parts != null) && (!IsDead) &&
            (_parts.PartSnapshot.ActivePartId == _partId);

        private void Awake()
        {
            if (_parts == null)
            {
                _parts = GetComponentInParent<BossPartController>();
            }

            if (_hitCollider == null)
            {
                TryGetComponent(out _hitCollider);
            }

            if (_parts != null)
            {
                ApplySnapshot(_parts.PartSnapshot);
            }
        }

        public void TakeDamage(in DamageInfo damage)
        {
            _parts?.ServerApplyDamage(_partId, damage, transform.position);
        }

        internal void ApplySnapshot(in BossPartSnapshot snapshot)
        {
            bool broken = snapshot.IsBroken(_partId);
            bool targetable = (!broken) && (snapshot.ActivePartId == _partId);
            if (_hitCollider != null)
            {
                _hitCollider.enabled = targetable;
            }

            if ((_visualRoot != null) && (_visualRoot != gameObject))
            {
                _visualRoot.SetActive(!broken);
            }
        }
    }
}
