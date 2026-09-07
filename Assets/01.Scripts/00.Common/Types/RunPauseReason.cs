// System
using System;

namespace Geumeum.Common
{
    /// <summary> 런이 멈춰 있는 이유. 여러 개가 동시에 걸릴 수 있어 플래그다 </summary>
    [Flags]
    public enum RunPauseReason
    {
        None               = 0,
        ParticipantMissing = 1 << 0, // 이탈자를 기다린다
        SceneTransition    = 1 << 1, // 씬을 옮기는 중이다
        BossCinematic      = 1 << 2, // 페이즈 전환 연출 등 판정을 멈추는 구간
        Result             = 1 << 3, // 결과가 확정되어 더 진행하지 않는다
    }
}
