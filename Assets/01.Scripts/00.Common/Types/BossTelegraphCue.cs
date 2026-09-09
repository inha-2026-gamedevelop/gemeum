// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 보스 공격 예고 1건의 서버 확정 값. 판정과 클라이언트 표현이 이 값을 함께 쓴다.
    /// Mirror Weaver가 읽어 쓸 수 있도록
    /// public 값 필드의 mutable struct로 둔다, 서버는 생성 뒤 복사본을 바꾸지 않는다
    /// </summary>
    [Serializable]
    public struct BossTelegraphCue
    {
        /// <summary> 보스별 단조 증가 공격 ID, 0은 "값 없음" 예약이라 쓰지 않는다 </summary>
        public uint AttackInstanceId;

        /// <summary> 같은 공격 안의 0-based 타격 순서. AttackInstanceId는 반복 내내 같다 </summary>
        public byte RepeatIndex;

        /// <summary> 같은 키를 다시 쓸 때 늦게 온 상태를 버리는 단조 증가 값 </summary>
        public uint Revision;

        /// <summary> 서버에서 아직 취소되지 않은 예고인지 여부 </summary>
        public bool IsActive;

        /// <summary> NetworkTime.time 기준 예고 시작 시각 </summary>
        public double StartServerTime;

        /// <summary> 로컬 GameDB.Boss 조회 키 </summary>
        public int PatternId;

        /// <summary> 개인 대상 netId, 지면/공용 cue면 0 </summary>
        public uint TargetNetId;

        /// <summary> 판정과 표현이 공유하는 서버 확정 원본 위치, 방향 </summary>
        public Vector3 Origin;
        public Vector3 Direction;

        /// <summary> NetworkTime.time 기준 절대 impact 시각 </summary>
        public double ImpactServerTime;

        /// <summary> 취소됐거나 늦게 도착한 예고는 재생하지 않는다 </summary>
        public bool IsPendingAt(double serverTime)
        {
            return ((IsActive) && (AttackInstanceId != 0u)
                && (!double.IsNaN(serverTime)) && (!double.IsInfinity(serverTime))
                && (!double.IsInfinity(StartServerTime)) && (!double.IsInfinity(ImpactServerTime))
                && (StartServerTime <= serverTime) && (serverTime < ImpactServerTime));
        }

        public bool HasSameKey(in BossTelegraphCue other)
        {
            return (AttackInstanceId == other.AttackInstanceId) &&
                   (RepeatIndex == other.RepeatIndex) &&
                   (TargetNetId == other.TargetNetId);
        }
    }
}
