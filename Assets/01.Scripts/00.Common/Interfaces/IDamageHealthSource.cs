namespace Geumeum.Common
{
    /// <summary> 직접 타격의 실제 피해 여부를 확인하는 서버 체력 조회 계약 </summary>
    public interface IDamageHealthSource
    {
        float Health { get; }
    }
}
