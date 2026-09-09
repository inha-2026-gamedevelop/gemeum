// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> 결계와 나머지 파트를 잇는 정적 로케이터. 구현이 없으면 기본값을 돌려준다 </summary>
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

        /// <summary> 목표 원 공급 계약. 현재 구현이 예전 계약만 지원하면 비활성 fallback을 돌려준다 </summary>
        public static IBarrierTransitionSource TransitionSource =>
            (_current as IBarrierTransitionSource) ?? _fallback;

        /// <summary> 씬 수명 때문에 목표 원 공급자가 바뀐 뒤 발생한다 </summary>
        public static event Action<IBarrierTransitionSource> Changed;

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
            ObserverDispatch.Invoke(Changed, TransitionSource, null,
                nameof(BarrierStateLocator), nameof(Changed));
        }

        /// <summary> 등록을 해제한다, 등록한 본인이 아니면 무시하므로 씬 전환 중 순서가 엇갈려도 안전하다 </summary>
        public static void Unregister(IBarrierState state)
        {
            if (_current != state)
            {
                return;
            }

            _current = _fallback;
            ObserverDispatch.Invoke(Changed, _fallback, null,
                nameof(BarrierStateLocator), nameof(Changed));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            _current = _fallback;
            Changed  = null;
        }

        /****************************************
        *              Null Object
        ****************************************/

        private sealed class NullBarrierState : IBarrierTransitionSource
        {
            public bool    IsActive => false;
            public Vector3 Center   => Vector3.zero;
            public float   Radius   => 0f;
            public BarrierTransitionState Snapshot => default;

            public event Action<BarrierTransitionState> SnapshotChanged
            {
                add { }
                remove { }
            }

            public bool  IsOutside(Vector3 worldPosition)            => false;
            public float GetHealingMultiplier(Vector3 worldPosition) => 1f;
        }
    }
}
