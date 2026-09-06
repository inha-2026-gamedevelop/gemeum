// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 런 시간 창구의 정적 로케이터.
    ///
    /// 결계, 스폰, 보스, UI가 Network 파트의 구체 타입을 참조하지 않고 시간을 읽게 한다.
    /// 런 매니저가 없는 개발 씬에서는 0을 돌려주므로 호출부에 null 체크가 없다
    /// </summary>
    public static class RunTimeLocator
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly NullRunTimeSource _fallback = new NullRunTimeSource();

        private static IRunTimeSource _current = _fallback;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 현재 구현. 등록된 것이 없으면 멈춰 있는 0초를 돌려준다 </summary>
        public static IRunTimeSource Current => _current;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 구현을 등록한다. RunManager가 OnEnable에서 호출한다 </summary>
        public static void Register(IRunTimeSource source)
        {
            if (source == null)
            {
                Debug.LogError("[RunTimeLocator] null 구현을 등록하려 했다");
                return;
            }

            _current = source;
        }

        /// <summary> 등록을 해제한다. 등록한 본인이 아니면 무시하므로 씬 전환 중 순서가 엇갈려도 안전하다 </summary>
        public static void Unregister(IRunTimeSource source)
        {
            if (_current != source)
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

        // 런 매니저가 없는 Dev 씬에서도 결계, AI, UI 코드가 그대로 돌게 한다
        private sealed class NullRunTimeSource : IRunTimeSource
        {
            public double FieldElapsedSeconds    => 0.0;
            public double BossElapsedSeconds     => 0.0;
            public double GameplayElapsedSeconds => 0.0;

            public RunPauseReason PauseReasons => RunPauseReason.None;
            public bool           IsPaused     => false;
        }
    }
}
