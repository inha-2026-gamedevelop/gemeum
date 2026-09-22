namespace Geumeum.Combat
{
    /// <summary> 사람 입력과 서버 제어 입력이 공유하는 형식 검사 </summary>
    public static class PlayerInputRules
    {
        public static bool IsFinite(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }
    }
}
