// 프로젝트 내부
using Geumeum.Network;

namespace Geumeum.Combat
{
    /// <summary> PlayerActionGate가 단발 행동의 공통 허용 여부를 계산할 때 쓰는 서버 상태 snapshot </summary>
    public readonly struct PlayerActionContext
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 현재 런 단계 </summary>
        public RunPhase Phase { get; }

        /// <summary> 현재 생명 상태 </summary>
        public PlayerLifeState LifeState { get; }

        /// <summary> 행동에 필요한 무기를 장착했는지 여부 </summary>
        public bool HasRequiredWeapon { get; }

        /// <summary> 서로 배타적인 다른 행동이 진행 중인지 여부 </summary>
        public bool IsActionInProgress { get; }

        /// <summary> 서버 판정 시각 </summary>
        public double ServerTime { get; }

        /// <summary> 행동 쿨다운이 끝나는 서버 시각 </summary>
        public double CooldownEndServerTime { get; }

        /// <summary> 현재 자원 잔액 </summary>
        public float ResourceBalance { get; }

        /// <summary> 행동에 필요한 유효 자원 비용 </summary>
        public float ResourceCost { get; }

        /****************************************
        *              Constructors
        ****************************************/

        /// <summary> 행동 판정 시점의 서버 상태를 만든다 </summary>
        public PlayerActionContext(
            RunPhase phase,
            PlayerLifeState lifeState,
            bool hasRequiredWeapon,
            bool isActionInProgress,
            double serverTime,
            double cooldownEndServerTime,
            float resourceBalance,
            float resourceCost)
        {
            Phase                 = phase;
            LifeState             = lifeState;
            HasRequiredWeapon     = hasRequiredWeapon;
            IsActionInProgress    = isActionInProgress;
            ServerTime            = serverTime;
            CooldownEndServerTime = cooldownEndServerTime;
            ResourceBalance       = resourceBalance;
            ResourceCost          = resourceCost;
        }
    }
}
