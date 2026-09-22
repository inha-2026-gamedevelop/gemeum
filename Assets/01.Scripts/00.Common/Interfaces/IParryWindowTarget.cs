namespace Geumeum.Common
{
    /// <summary>
    /// 패링 창을 받을 수 있는 대상의 계약. 공격 측(AI)이 Combat을 직접 참조하지 않도록
    /// 이 인터페이스만 보고 창을 열고 닫는다. 구현은 서버에서만 유효하다
    /// </summary>
    public interface IParryWindowTarget
    {
        /// <summary>
        /// 이 대상을 노리는 패링 창을 연다. 창 구간은 impactTime에서 역산하므로
        /// 공격 측은 타격 시각만 확정하면 된다. 열지 못하면 false를 돌려준다
        /// </summary>
        bool OpenParryWindow(
            IParryable source,
            ParryWindowKey key,
            double telegraphStartTime,
            double impactTime);

        /// <summary>
        /// 공격이 취소되거나 공격자가 사라지면 창을 닫는다
        /// 만료를 기다리면 이미 없는 공격을 패링해 성공하는 상황이 생긴다
        /// </summary>
        void CloseParryWindow(ParryWindowKey key);
    }
}
