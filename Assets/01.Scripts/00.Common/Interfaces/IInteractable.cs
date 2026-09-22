namespace Geumeum.Common
{
    /// <summary>
    /// 상호작용 대상. 객주와 드랍 아이템이 구현한다
    /// 판정과 실행은 서버가 하고 클라이언트는 PromptText만 읽는다
    /// </summary>
    public interface IInteractable
    {
        /// <summary> 조준했을 때 띄울 안내 문구 </summary>
        string PromptText { get; }

        /// <summary> 이 플레이어가 지금 상호작용할 수 있는지, 서버가 요청을 받고 먼저 확인한다 </summary>
        bool CanInteract(uint playerNetId);

        /// <summary> 상호작용을 실행한다, 서버에서만 호출한다 </summary>
        void Interact(uint playerNetId);
    }
}
