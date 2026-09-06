// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 점수 계산기의 정적 로케이터.
    ///
    /// 점수식이 런 상태 머신 안에 있으면 배점을 고칠 때마다 Network를 건드리게 된다.
    /// 계산은 Systems가 순수 함수로 구현하고, 런은 결과 값을 보관, 배포만 한다.
    ///
    /// 아직 구현이 없는 동안에는 0점을 돌려주므로 정산 경로를 먼저 완성할 수 있다
    /// </summary>
    public static class RunResultProviderLocator
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly NullRunResultProvider _fallback = new NullRunResultProvider();

        private static IRunResultProvider _current = _fallback;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 현재 구현. 등록된 것이 없으면 0점을 돌려준다 </summary>
        public static IRunResultProvider Current => _current;

        /// <summary> 실제 점수식이 등록되어 있는지. 정산 화면이 "미집계"를 구분해 보여줄 때 쓴다 </summary>
        public static bool HasProvider => (_current != _fallback);

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 구현을 등록한다 </summary>
        public static void Register(IRunResultProvider provider)
        {
            if (provider == null)
            {
                Debug.LogError("[RunResultProviderLocator] null 구현을 등록하려 했다");
                return;
            }

            _current = provider;
        }

        /// <summary> 등록을 해제한다. 등록한 본인이 아니면 무시한다 </summary>
        public static void Unregister(IRunResultProvider provider)
        {
            if (_current != provider)
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

        // 점수 시스템이 없는 동안에도 결과 확정과 로비 복귀가 그대로 돌게 한다
        private sealed class NullRunResultProvider : IRunResultProvider
        {
            public int ScoreRuleVersion => 0;

            public int CalculateScore(in RunResultSnapshot facts) => 0;
        }
    }
}
