// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// IBossTrialGate 등록과 조회. 기본값은 아무것도 막지 않는 NullBossTrialGate라
    /// 트라이 컴포넌트가 없는 씬에서도 정식 런이 그대로 돈다
    /// </summary>
    public static class BossTrialGateLocator
    {
        private static readonly NullBossTrialGate G_Null = new NullBossTrialGate();

        private static IBossTrialGate _current = G_Null;

        public static IBossTrialGate Current => _current;

        public static void Register(IBossTrialGate gate)
        {
            if (gate == null)
            {
                Debug.LogError("[BossTrialGateLocator] null 구현을 등록하려 했다");
                return;
            }

            _current = gate;
        }

        public static void Unregister(IBossTrialGate gate)
        {
            if (!ReferenceEquals(_current, gate))
            {
                return;
            }

            _current = G_Null;
        }

        private sealed class NullBossTrialGate : IBossTrialGate
        {
            public bool IsBossStartBlocked => false;
            public bool IsTrialRequested   => false;
        }
    }
}
