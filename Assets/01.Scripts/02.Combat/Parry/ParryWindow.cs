// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary> 서버가 공격별 패링 창의 식별, 대상, 시간, 소비 상태를 보관하는 값 </summary>
    public sealed class ParryWindow
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 공격 소스가 발급한 런 범위 식별자 </summary>
        public uint WindowId { get; }

        /// <summary> 패링될 공격자의 네트워크 식별자 </summary>
        public uint AttackerNetId { get; }

        /// <summary> 이 창이 허용하는 플레이어의 네트워크 식별자 </summary>
        public uint TargetNetId { get; }

        /// <summary> 서버 판정 창 시작 시각 </summary>
        public double StartServerTime { get; }

        /// <summary> 서버 판정 창 종료 시각 </summary>
        public double EndServerTime { get; }

        /// <summary> 공격이 실제로 타격을 확정하는 시각. 레코드는 이 시각까지 살아 있어야
        /// 지연으로 늦게 도착한 CmdParry도 EndServerTime 기준으로 정상 판정받을 수 있다 </summary>
        public double ImpactTime { get; }

        /// <summary> 성공 결과가 이미 적용되었는지 여부 </summary>
        public bool Consumed { get; private set; }

        internal IParryable Source { get; }

        /****************************************
        *              Constructors
        ****************************************/

        /// <summary> 서버가 확인한 공격 소스와 대상, 시간으로 창을 만든다 </summary>
        public ParryWindow(
            uint windowId,
            uint attackerNetId,
            uint targetNetId,
            double startServerTime,
            double endServerTime,
            double impactTime,
            IParryable source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            WindowId        = windowId;
            AttackerNetId   = attackerNetId;
            TargetNetId     = targetNetId;
            StartServerTime = startServerTime;
            EndServerTime   = endServerTime;
            ImpactTime      = impactTime;
            Source          = source;
            Consumed        = false;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 레코드를 지워도 되는 시점인지 확인한다. EndServerTime이 아니라 ImpactTime 기준이다 -
        /// EndServerTime 직후 지우면 지연으로 늦게 도착한 CmdParry가 판정도 못 받고 버려진다.
        /// 실제 성공 판정은 ParryJudge가 StartServerTime, EndServerTime으로 별도로 가른다
        /// </summary>
        public bool IsExpired(double serverTime)
        {
            return serverTime > ImpactTime;
        }

        /// <summary> 아직 소비되지 않은 창을 한 번만 소비한다 </summary>
        public bool TryConsume()
        {
            if (Consumed)
            {
                return false;
            }

            Consumed = true;
            return true;
        }

        /// <summary> 공격 소스가 파괴되지 않고 남아 있는지 확인한다 </summary>
        public bool HasValidSource()
        {
            if (Source == null)
            {
                return false;
            }

            if ((Source is UnityEngine.Object unityObject) && (unityObject == null))
            {
                return false;
            }

            return true;
        }
    }
}
