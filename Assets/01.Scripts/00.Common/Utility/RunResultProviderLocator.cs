// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> 점수 계산기의 정적 로케이터. Systems가 구현을 등록하고 Network는 결과만 읽는다 </summary>
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

        private sealed class NullRunResultProvider : IRunResultProvider
        {
            public int ScoreRuleVersion => 0;

            public int CalculateScore(in RunResultSnapshot facts) => 0;

            public RunEvaluationGrade CalculateGrade(int score) => RunEvaluationGrade.None;

            public string GetEvaluationMessage(RunEvaluationGrade grade) => string.Empty;
        }
    }
}
