// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 전투와 아트를 잇는 정적 로케이터
    /// 전투가 아트의 구현 클래스를 직접 참조하지 않게 하고, 구현이 없을 때는 아무것도 하지 않는 기본값을 돌려준다
    /// </summary>
    public static class CombatFeedbackLocator
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly NullCombatFeedback _fallback = new NullCombatFeedback();

        private static ICombatFeedback _current = _fallback;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 현재 구현, 등록된 것이 없으면 아무것도 하지 않는 구현을 돌려주므로 호출부는 null 체크를 하지 않는다 </summary>
        public static ICombatFeedback Current => _current;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 구현을 등록한다, 아트 구현체가 OnEnable에서 호출한다 </summary>
        public static void Register(ICombatFeedback feedback)
        {
            if (feedback == null)
            {
                Debug.LogError("[CombatFeedbackLocator] null 구현을 등록하려 했다");
                return;
            }

            _current = feedback;
        }

        /// <summary> 등록을 해제한다, 등록한 본인이 아니면 무시하므로 교체 중 순서가 엇갈려도 안전하다 </summary>
        public static void Unregister(ICombatFeedback feedback)
        {
            if (_current != feedback)
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

        // 연출이 아직 없는 프로토타입에서도 전투 코드가 그대로 돌게 한다
        private sealed class NullCombatFeedback : ICombatFeedback
        {
            public void PlayHit(HitStrength strength, float duration) { }
            public void PlayParrySuccess() { }
            public void PlayCritical(float multiplier) { }
            public void PlayGroggyEnter() { }
            public void PlayPartBreak() { }
        }
    }
}
