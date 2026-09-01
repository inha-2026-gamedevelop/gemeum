// System
using System;

namespace Geumeum.Common
{
    /// <summary>
    /// 파트 간 이벤트 발행, 구독 계약. 페이로드 타입 하나가 이벤트 하나다.
    /// 구독자는 OnEnable에서 구독하고 OnDisable에서 반드시 해제한다
    /// </summary>
    public interface IGameEventBus
    {
        /// <summary> 이벤트를 발행한다, 구독자가 없으면 아무 일도 하지 않는다 </summary>
        void Publish<T>(in T payload) where T : struct;

        /// <summary> 구독한다, 같은 핸들러를 두 번 넣으면 두 번 불린다 </summary>
        void Subscribe<T>(Action<T> handler) where T : struct;

        /// <summary> 구독을 해제한다 </summary>
        void Unsubscribe<T>(Action<T> handler) where T : struct;
    }
}
