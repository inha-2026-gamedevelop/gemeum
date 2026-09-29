namespace Geumeum.AI
{
    /// <summary>
    /// 자체 완료 시점을 스스로 판단하는 실행 객체(RotatingBeam 등)의 TickWindup 결과.
    /// FSM 상태(_state),쿨다운,종료 통지는 이 값을 받은 PhaseState만 수행한다 -
    /// 실행 객체는 이 값을 반환할 뿐 직접 Recovery로 전환하거나 ServerNotifyPatternEnded를
    /// 호출하지 않는다. 표준 반복-오프셋 스케줄을 그대로 쓰는 패턴(TargetedDash, MarkedCircle
    /// 등)은 이 결과가 필요 없어 TickWindup을 void로 둔다 - 전 패턴에 강제하지 않는다
    /// </summary>
    internal enum PatternTickResult
    {
        /// <summary> 아직 진행 중이다 - PhaseState는 표준 처리를 건너뛰고 다음 프레임을 기다린다 </summary>
        Continue,

        /// <summary> 이번 프레임에 끝났다 - PhaseState가 텔레그래프 정리,종료 통지,Recovery 전환을 수행한다 </summary>
        Complete,
    }
}
