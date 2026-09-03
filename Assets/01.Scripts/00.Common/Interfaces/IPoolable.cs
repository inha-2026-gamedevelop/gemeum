namespace Geumeum.Common
{
    /// <summary>
    /// 풀에서 재사용되는 오브젝트의 상태 복원 계약.
    /// 재사용은 Destroy를 건너뛰므로 지난 사용의 파티클, Trail, 코루틴, 임시 참조가 그대로 남는다
    /// </summary>
    public interface IPoolable
    {
        /// <summary> 대여 직후 호출, 활성화 이후이므로 코루틴을 시작할 수 있다 </summary>
        void OnRentFromPool();

        /// <summary> 반납 직전 호출, 비활성화 이전이므로 여기서 코루틴을 멈춘다 </summary>
        void OnReturnToPool();
    }
}
