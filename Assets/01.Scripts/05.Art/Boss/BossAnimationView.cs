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
        private IBossPhaseSource      _phaseSource;
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
            _phaseSource     = GetComponentInParent<IBossPhaseSource>();
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
            if ((_isSubscribed) || (_telegraphSource == null) || (_phaseSource == null) || (_status == null))
            {
                return;
            }

            _telegraphSource.TelegraphChanged += HandleTelegraphChanged;
            _phaseSource.PhaseChanged         += HandlePhaseChanged;
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
            _phaseSource.PhaseChanged         -= HandlePhaseChanged;
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

        // 꼬리 표식(BossPartSnapshot.ActivePartId)은 2페이즈에서만 켜지므로 그로기 자세는
        // 거기 얹지 않고 페이즈 단계(BossEncounterStage)로 직접 판정한다 - 1페이즈 그로기도
        // 같은 자세를 보여줘야 한다. WakeWarning은 기상 예고 2초 구간으로, 그동안도 쓰러진
        // 자세를 유지한 채 별도 경고만 얹히므로 그로기로 함께 취급한다
        private void HandlePhaseChanged(BossPhaseSnapshot snapshot)
        {
            bool isGroggy = (snapshot.Stage == BossEncounterStage.Groggy) ||
                (snapshot.Stage == BossEncounterStage.WakeWarning);
            _animator.SetBool(G_IsGroggy, isGroggy);
        }

        private void HandleDied(uint attackerNetId)
        {
            _animator.SetTrigger(G_Dead);
        }
    }
}
