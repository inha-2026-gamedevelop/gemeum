namespace Geumeum.Combat
{
    /// <summary>
    /// 패링 권위 판정 레이어, 서버 보정 시각과 판정 창으로 성공 여부만 계산한다.
    /// Mirror에 의존하지 않아야 데디케이티드로 옮길 때 그대로 쓴다
    /// </summary>
    public static class ParryJudge
    {
        /// <summary> 패링 입력 시각이 판정 창(windowStartTime ~ windowStartTime + windowDuration) 안에 들어오는지 판정한다 </summary>
        public static bool IsSuccess(double parryInputTime, double windowStartTime, float windowDuration)
        {
            double windowEndTime = windowStartTime + windowDuration;
            return (parryInputTime >= windowStartTime) && (parryInputTime <= windowEndTime);
        }
    }
}
