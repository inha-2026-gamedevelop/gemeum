namespace Geumeum.Combat
{
    /// <summary> 패링 권위 판정 레이어, 서버 보정 시각과 판정 창으로 성공 여부를 계산한다 </summary>
    public static class ParryJudge
    {
        /// <summary> 패링 입력 시각이 시작, 종료 시각을 포함한 판정 창 안에 들어오는지 판정한다 </summary>
        public static bool IsSuccess(double parryInputTime, double windowStartTime, double windowEndTime)
        {
            return (parryInputTime >= windowStartTime) && (parryInputTime <= windowEndTime);
        }
    }
}
