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

        private static readonly int G_AttackKind  = Animator.StringToHash("AttackKind");
        private static readonly int G_RepeatIndex = Animator.StringToHash("RepeatIndex");
        private static readonly int G_Attack      = Animator.StringToHash("Attack");
        private static readonly int G_IsGroggy    = Animator.StringToHash("IsGroggy");
        private static readonly int G_Dead        = Animator.StringToHash("Dead");

        private Animator              _animator;
        private IBossTelegraphSource  _telegraphSource;
        private IBossPartSource       _partSource;
        private BossStatus            _status;

        private uint _lastAttackInstanceId;
        private int  _lastRepeatIndex = -1;
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

        // BossController가 내보내는 "현재" 예고는 활성 예고들 중 impact가 가장 이른 것 하나뿐이다
        // (RefreshLegacyTelegraph). 그래서 반복 타격 패턴은 1타 impact에서 그 예고가 취소되는
        // 순간 자연히 2타 예고로 넘어와 이 핸들러가 다시 불린다 - 타이밍을 따로 재구현할 필요 없이
        // (AttackInstanceId, RepeatIndex) 쌍이 바뀔 때마다 그대로 트리거하면 된다
        private void HandleTelegraphChanged(BossTelegraphCue cue)
        {
            if ((!cue.IsActive) || (cue.AttackInstanceId == 0u) ||
                ((cue.AttackInstanceId == _lastAttackInstanceId) && (cue.RepeatIndex == _lastRepeatIndex)) ||
                (!GameDB.Boss.TryGetPattern(cue.PatternId, out BossPatternSO.PatternEntry pattern)))
            {
                return;
            }

            _lastAttackInstanceId = cue.AttackInstanceId;
            _lastRepeatIndex      = cue.RepeatIndex;
            _animator.SetInteger(G_AttackKind, (int) pattern.Template - 1);
            _animator.SetInteger(G_RepeatIndex, cue.RepeatIndex);
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
