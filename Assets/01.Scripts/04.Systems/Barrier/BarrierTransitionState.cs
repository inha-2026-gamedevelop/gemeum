// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.Systems
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
        public double  StartServerTime;
        public double  EndServerTime;
        public uint    Revision; // 같은 값 재전송과 실제 변경을 구분한다

        /****************************************
        *                Methods
        ****************************************/

        public bool Equals(BarrierTransitionState other)
        {
            return FromCenter.Equals(other.FromCenter)
                && TargetCenter.Equals(other.TargetCenter)
                && FromRadius.Equals(other.FromRadius)
                && TargetRadius.Equals(other.TargetRadius)
                && StartServerTime.Equals(other.StartServerTime)
                && EndServerTime.Equals(other.EndServerTime)
                && (Revision == other.Revision);
        }

        public override bool Equals(object obj)
        {
            return (obj is BarrierTransitionState other) && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(FromCenter, TargetCenter, FromRadius, TargetRadius, StartServerTime, EndServerTime, Revision);
        }
    }
}
