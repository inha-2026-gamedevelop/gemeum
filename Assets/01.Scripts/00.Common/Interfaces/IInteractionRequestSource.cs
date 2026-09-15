// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> 로컬 상호작용 소비자에게 위치와 요청 전송만 제공한다 </summary>
    public interface IInteractionRequestSource
    {
        uint ActorNetId { get; }
        Vector3 ActorPosition { get; }

        /// <summary> true는 로컬에서 전송했다는 뜻이며 서버 승인을 의미하지 않는다 </summary>
        bool RequestInteract(uint targetNetId);
    }
}
