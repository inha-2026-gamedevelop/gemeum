// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 결계와 나머지 파트를 잇는 정적 로케이터.
    /// 전투, AI, 아트, UI가 시스템 파트의 구현 클래스를 직접 참조하지 않게 하고,
    /// 결계가 아직 없는 동안에는 "아무도 밖에 있지 않다"는 기본값을 돌려준다.
    ///
    /// 덕분에 결계 구현을 기다리지 않고 무녀 치유, 잡귀 이동, 결계 비주얼을 각자 착수할 수 있다
    /// </summary>
    public static class BarrierStateLocator
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly NullBarrierState _fallback = new NullBarrierState();

        private static IBarrierState _current = _fallback;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 현재 구현, 등록된 것이 없으면 결계가 꺼진 것으로 답하는 구현을 돌려주므로 호출부는 null 체크를 하지 않는다 </summary>
        public static IBarrierState Current => _current;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 구현을 등록한다, BarrierController가 OnEnable에서 호출한다 </summary>
        public static void Register(IBarrierState state)
        {
            if (state == null)
            {
                Debug.LogError("[BarrierStateLocator] null 구현을 등록하려 했다");
                return;
            }

            _current = state;
        }

        /// <summary> 등록을 해제한다, 등록한 본인이 아니면 무시하므로 씬 전환 중 순서가 엇갈려도 안전하다 </summary>
        public static void Unregister(IBarrierState state)
        {
            if (_current != state)
            {
                return;
            }

            _current = _fallback;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            _current = _fallback;
        }

        /****************************************
        *              Null Object
        ****************************************/

        // 결계가 아직 없는 프로토타입에서도 전투, AI, 아트 코드가 그대로 돌게 한다
        private sealed class NullBarrierState : IBarrierState
        {
            public bool    IsActive => false;
            public Vector3 Center   => Vector3.zero;
            public float   Radius   => 0f;

            public bool  IsOutside(Vector3 worldPosition)            => false;
            public float GetHealingMultiplier(Vector3 worldPosition) => 1f;
        }
    }
}
