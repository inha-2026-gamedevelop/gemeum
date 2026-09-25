// 프로젝트 내부
using Geumeum.AI;
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Art
{
    /// <summary>
    /// 보스 cue 한 타를 패링 가능(창이 열림)과 패링 불가(붉은 경고)로 가른다. 대상이 아닌 사람이 확정 창 없이 쓴다
    /// 서버 Phase 상태의 TryOpenParryWindow가 창을 여는 조건(패링 가능, 튕겨내기, 이양 반복)을 클라가 가진 정보로 옮긴다
    /// 불가 경고를 잘못 띄우는 쪽이 더 해롭다. 서버가 창을 열 수 있는 타는 전부 가능으로 본다
    /// </summary>
    public static class BossParryFlashClassifier
    {
        /****************************************
        *              Constants
        ****************************************/

        // 꼬리 파괴 정보를 모를 때 쓰는 값. 세 꼬리가 모두 부서졌다고 보면 이양 가능성이 있는 타는 전부 가능이 된다
        private const byte ALL_TAILS_BROKEN_MASK = 0b0000_0111;

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 서버가 이 타에 패링 창을 열 수 있으면 Parryable </summary>
        public static ParryFlashKind Classify(in BossPatternSO.PatternEntry pattern, int repeatIndex,
            bool isTransferredRepeat)
        {
            var spec = new BossPatternExecutionSpec(pattern);
            bool opensWindow = (spec.IsRepeatParryable(pattern, repeatIndex)) ||
                (spec.IsRepeatDeflectable(pattern, repeatIndex)) || (isTransferredRepeat);

            return opensWindow ? ParryFlashKind.Parryable : ParryFlashKind.Unparryable;
        }

        /// <summary>
        /// 꼬리 파괴로 차사 반격 기회가 이 패턴 반복으로 옮겨졌는지. 서버 BossPartController의 이양 규칙과 같다
        /// 서버는 대상 직업과 관계없이 차사 이양 항목으로 창을 연다. 여기도 차사 항목만 본다
        /// </summary>
        public static bool IsTransferredRepeat(BossPatternSO data, in BossPartSnapshot parts, int patternId,
            int repeatIndex)
        {
            if ((data == null) || (patternId <= 0) || (repeatIndex < 0))
            {
                return false;
            }

            for (byte partId = BossTargetPartIds.Tail1; partId <= BossTargetPartIds.Tail3; ++partId)
            {
                if ((parts.IsBroken(partId)) &&
                    (data.TryGetCounterOpportunityTransfer(partId, CharacterKind.Chasa,
                        out BossPatternSO.CounterOpportunityTransferEntry transfer)) &&
                    (transfer.FallbackPatternId == patternId) &&
                    ((transfer.RepeatIndex < 0) || (transfer.RepeatIndex == repeatIndex)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary> 꼬리 상태를 받을 창구가 없을 때 쓰는 보수적 스냅샷. 이양 가능성이 있는 타를 가능으로 본다 </summary>
        public static BossPartSnapshot UnknownPartsSnapshot()
        {
            return new BossPartSnapshot { BrokenMask = ALL_TAILS_BROKEN_MASK };
        }
    }
}
