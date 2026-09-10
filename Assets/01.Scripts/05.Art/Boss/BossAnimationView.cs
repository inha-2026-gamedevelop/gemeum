// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.AI;
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Art
{
    /// <summary> 보스 판정이 확정한 SyncVar·이벤트를 Animator 파라미터로 옮긴다. 판정에는 관여하지 않는다 </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class BossAnimationView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly int G_AttackKind = Animator.StringToHash("AttackKind");
        private static readonly int G_Attack     = Animator.StringToHash("Attack");
        private static readonly int G_IsGroggy   = Animator.StringToHash("IsGroggy");
        private static readonly int G_Dead       = Animator.StringToHash("Dead");

        private Animator              _animator;
        private IBossTelegraphSource  _telegraphSource;
        private IBossPartSource       _partSource;
        private BossStatus            _status;

        private uint _lastAttackInstanceId;
        private bool _isSubscribed;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _animator        = GetComponent<Animator>();
            _telegraphSource = GetComponentInParent<IBossTelegraphSource>();
            _partSource      = GetComponentInParent<IBossPartSource>();
            _status          = GetComponentInParent<BossStatus>();
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void TrySubscribe()
        {
            if ((_isSubscribed) || (_telegraphSource == null) || (_partSource == null) || (_status == null))
            {
                return;
            }

            _telegraphSource.TelegraphChanged += HandleTelegraphChanged;
            _partSource.PartChanged           += HandlePartChanged;
            _status.Died                      += HandleDied;
            _isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_isSubscribed)
            {
                return;
            }

            _telegraphSource.TelegraphChanged -= HandleTelegraphChanged;
            _partSource.PartChanged           -= HandlePartChanged;
            _status.Died                      -= HandleDied;
            _isSubscribed = false;
        }

        // 같은 공격의 반복 타격마다 다시 오는 통지는 걸러내고, 새 공격 시작에서만 트리거한다
        private void HandleTelegraphChanged(BossTelegraphCue cue)
        {
            if ((!cue.IsActive) || (cue.RepeatIndex != 0) || (cue.AttackInstanceId == _lastAttackInstanceId) ||
                (!GameDB.Boss.TryGetPattern(cue.PatternId, out BossPatternSO.PatternEntry pattern)))
            {
                return;
            }

            _lastAttackInstanceId = cue.AttackInstanceId;
            _animator.SetInteger(G_AttackKind, (int) pattern.Template - 1);
            _animator.SetTrigger(G_Attack);
        }

        private void HandlePartChanged(BossPartSnapshot snapshot)
        {
            _animator.SetBool(G_IsGroggy, snapshot.ActivePartId != 0);
        }

        private void HandleDied(uint attackerNetId)
        {
            _animator.SetTrigger(G_Dead);
        }
    }
}
