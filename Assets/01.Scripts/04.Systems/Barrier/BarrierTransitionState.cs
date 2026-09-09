// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 결계 축소 한 구간의 전환 정보. 중심과 반경을 하나로 묶어 같은 t로 보간되게 한다.
    /// 둘을 따로 SyncVar로 보내면 도착 순서가 엇갈려 "그려진 원"과 "때리는 원"이 어긋난다
    /// </summary>
    [Serializable]
    public struct BarrierTransitionState : IEquatable<BarrierTransitionState>
    {
        /****************************************
        *                Fields
        ****************************************/

        public Vector3 FromCenter;
        public Vector3 TargetCenter;
        public float   FromRadius;
        public float   TargetRadius;
        public double  WarningGameplayTime;
        public double  StartGameplayTime;
        public double  EndGameplayTime;
        public uint    Revision; // 같은 값 재전송과 실제 변경을 구분한다
        public RunPhase CurrentPhase;
        public RunPhase TargetPhase;
        public bool     IsActive;

        /****************************************
        *                Methods
        ****************************************/

        public bool Equals(BarrierTransitionState other)
        {
            return FromCenter.Equals(other.FromCenter)
                && TargetCenter.Equals(other.TargetCenter)
                && FromRadius.Equals(other.FromRadius)
                && TargetRadius.Equals(other.TargetRadius)
                && WarningGameplayTime.Equals(other.WarningGameplayTime)
                && StartGameplayTime.Equals(other.StartGameplayTime)
                && EndGameplayTime.Equals(other.EndGameplayTime)
                && (Revision == other.Revision)
                && (CurrentPhase == other.CurrentPhase)
                && (TargetPhase == other.TargetPhase)
                && (IsActive == other.IsActive);
        }

        public override bool Equals(object obj)
        {
            return (obj is BarrierTransitionState other) && Equals(other);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(FromCenter);
            hash.Add(TargetCenter);
            hash.Add(FromRadius);
            hash.Add(TargetRadius);
            hash.Add(WarningGameplayTime);
            hash.Add(StartGameplayTime);
            hash.Add(EndGameplayTime);
            hash.Add(Revision);
            hash.Add(CurrentPhase);
            hash.Add(TargetPhase);
            hash.Add(IsActive);
            return hash.ToHashCode();
        }
    }
}
