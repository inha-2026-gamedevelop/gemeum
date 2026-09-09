namespace Geumeum.Systems
{
    public enum InteractionResult
    {
        Success        = 0,
        TooFar         = 1,
        Dead           = 2,
        InCombat       = 3,
        RunNotActive   = 4,
        InvalidRequest = 5,
        WrongScene     = 6,
        Unavailable    = 7,
    }

    /// <summary> 상호작용 한 건을 판정하는 데 필요한 서버 사실들 </summary>
    public readonly struct InteractionRequest
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 요청자와 대상 사이 거리의 제곱 </summary>
        public float DistanceSquared { get; }

        /// <summary> 허용 최대 거리. 값을 코드에 박지 않고 SO에서 읽어 넘긴다 </summary>
        public float MaxDistance { get; }

        public bool IsRequesterAlive { get; }

        /// <summary> 런이 진행 중인가. Result나 로비면 false다 </summary>
        public bool IsRunActive { get; }

        /// <summary> 서버 기준 현재 시각 </summary>
        public double ServerTime { get; }

        /// <summary> 피해를 주거나 받은 마지막 서버 시각. 전투가 없었으면 음수를 넘긴다 </summary>
        public double LastCombatServerTime { get; }

        /// <summary> 전투 후 상호작용이 열리기까지의 유예. 게임 디자인 6.5는 5초 </summary>
        public float OutOfCombatSeconds { get; }

        /****************************************
        *                Methods
        ****************************************/

        public InteractionRequest(float distanceSquared, float maxDistance, bool isRequesterAlive,
            bool isRunActive, double serverTime, double lastCombatServerTime, float outOfCombatSeconds)
        {
            DistanceSquared      = distanceSquared;
            MaxDistance          = maxDistance;
            IsRequesterAlive     = isRequesterAlive;
            IsRunActive          = isRunActive;
            ServerTime           = serverTime;
            LastCombatServerTime = lastCombatServerTime;
            OutOfCombatSeconds   = outOfCombatSeconds;
        }
    }

    /// <summary>
    /// 줍기 · 양도 · 객주가 공통으로 통과해야 하는 조건을 계산한다.
    /// Mirror와 UnityEngine에 의존하지 않아, 물건마다 검증을 복제하지 않고 한 곳에서 고정한다.
    /// 거리와 유예 시간은 값을 박지 않고 호출자가 SO에서 읽어 넘긴다
    /// </summary>
    public static class InteractionRules
    {
        /****************************************
        *              Public API
        ****************************************/

        /// <summary>
        /// 공통 조건을 본다. 협동 부활처럼 전투 중에도 허용해야 하는 행동은
        /// requireOutOfCombat을 false로 넘긴다 - 게임 디자인 6.5가 명시한 예외다
        /// </summary>
        public static InteractionResult Evaluate(in InteractionRequest request, bool requireOutOfCombat = true)
        {
            if (!IsWellFormed(request))
            {
                return InteractionResult.InvalidRequest;
            }

            if (!request.IsRunActive)
            {
                return InteractionResult.RunNotActive;
            }

            if (!request.IsRequesterAlive)
            {
                return InteractionResult.Dead;
            }

            // 제곱으로 비교해 루트를 피한다. 경계값은 허용한다
            if (request.DistanceSquared > (request.MaxDistance * request.MaxDistance))
            {
                return InteractionResult.TooFar;
            }

            if ((requireOutOfCombat) && (IsInCombat(request)))
            {
                return InteractionResult.InCombat;
            }

            return InteractionResult.Success;
        }

        /// <summary>
        /// 마지막 전투 시각으로부터 유예가 지났는지. 안내 문구를 띄울지 판단할 때도 같은 값을 쓴다.
        /// 한 번도 전투하지 않았으면 마지막 전투 시각이 음수다
        /// </summary>
        public static bool IsInCombat(in InteractionRequest request)
        {
            if (request.LastCombatServerTime < 0d)
            {
                return false;
            }

            return ((request.ServerTime - request.LastCombatServerTime) < request.OutOfCombatSeconds);
        }

        /// <summary> 상호작용이 열리기까지 남은 시간. 이미 가능하면 0이다 </summary>
        public static double RemainingCombatLockSeconds(in InteractionRequest request)
        {
            if (!IsInCombat(request))
            {
                return 0d;
            }

            return (request.OutOfCombatSeconds - (request.ServerTime - request.LastCombatServerTime));
        }

        /****************************************
        *               Internals
        ****************************************/

        private static bool IsWellFormed(in InteractionRequest request)
        {
            return ((IsFinite(request.DistanceSquared)) && (request.DistanceSquared >= 0f) &&
                    (IsFinite(request.MaxDistance)) && (request.MaxDistance > 0f) &&
                    (IsFinite(request.OutOfCombatSeconds)) && (request.OutOfCombatSeconds >= 0f) &&
                    (IsFinite(request.ServerTime)) && (IsFinite(request.LastCombatServerTime)));
        }

        private static bool IsFinite(float value) => ((!float.IsNaN(value)) && (!float.IsInfinity(value)));

        private static bool IsFinite(double value) => ((!double.IsNaN(value)) && (!double.IsInfinity(value)));
    }
}
