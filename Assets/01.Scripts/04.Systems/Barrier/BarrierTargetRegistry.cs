// System
using System.Collections.Generic;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    /// <summary>
    /// 서버가 결계 피해 판정 때 순회할 최대 3명의 어댑터를 고정 재사용 목록으로 보관한다.
    /// 런마다 새로 만들지 않고 플레이어 오브젝트 수명에 맞춰 등록, 해제만 한다
    /// </summary>
    public static class BarrierTargetRegistry
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly List<BarrierTargetAdapter> _adapters = new List<BarrierTargetAdapter>(Constants.Network.MAX_PLAYERS);

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 등록된 어댑터 목록. 매 틱 순회용이며 호출부가 임의로 수정하지 않는다 </summary>
        public static IReadOnlyList<BarrierTargetAdapter> Adapters => _adapters;

        /****************************************
        *              Server API
        ****************************************/

        public static void Register(BarrierTargetAdapter adapter)
        {
            if (_adapters.Contains(adapter))
            {
                return;
            }

            _adapters.Add(adapter);
        }

        public static void Unregister(BarrierTargetAdapter adapter)
        {
            _adapters.Remove(adapter);
        }
    }
}
