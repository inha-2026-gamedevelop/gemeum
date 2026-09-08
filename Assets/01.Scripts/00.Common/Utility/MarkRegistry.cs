// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> 표식 한 칸. 누가 언제 무엇에 걸었는지 </summary>
    public readonly struct MarkSlot
    {
        public readonly TargetRef Target;
        public readonly double    AppliedServerTime;
        public readonly double    ExpiresServerTime;

        public MarkSlot(TargetRef target, double appliedServerTime, double expiresServerTime)
        {
            Target            = target;
            AppliedServerTime = appliedServerTime;
            ExpiresServerTime = expiresServerTime;
        }

        public bool IsOccupied => Target.IsValid;

        /// <summary> 만료 비교는 부여 시각 이상, 만료 시각 미만으로 통일한다 </summary>
        public bool IsActiveAt(double serverTime)
        {
            return ((Target.IsValid) && (serverTime < ExpiresServerTime));
        }
    }

    /// <summary> 표식 부여가 거부된 이유 </summary>
    public enum MarkRejection
    {
        None          = 0,
        InvalidOwner  = 1, // 신원이 없는 요청
        InvalidTarget = 2, // 대상 netId가 0
        InvalidSlots  = 3, // 슬롯 수가 1 미만
        AlreadyMarked = 4, // 같은 소유자가 같은 대상에 이미 걸어 두었다
    }

    /// <summary> 서버가 소유하는 표식 상태의 유일한 원본. 본체와 부위 효과, 보스 카운터가 모두 여기를 조회한다 </summary>
    public static class MarkRegistry
    {
        /****************************************
        *                Fields
        ****************************************/

        // 소유자(ParticipantId.Value)마다 슬롯 배열 하나. 파티가 세 명이라 순회 비용이 문제되지 않는다
        private static readonly Dictionary<ulong, MarkSlot[]> _slotsByOwner = new Dictionary<ulong, MarkSlot[]>();

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 표식을 건다. 슬롯이 가득 차 있으면 가장 오래된 것을 밀어낸다 </summary>
        public static bool TryApply(
            ParticipantId     owner,
            TargetRef         target,
            double            serverTime,
            double            duration,
            int               slotCount,
            out MarkRejection rejection)
        {
            rejection = MarkRejection.None;

            if (!owner.IsValid)
            {
                rejection = MarkRejection.InvalidOwner;
                return false;
            }

            if (!target.IsValid)
            {
                rejection = MarkRejection.InvalidTarget;
                return false;
            }

            if (slotCount < 1)
            {
                rejection = MarkRejection.InvalidSlots;
                return false;
            }

            MarkSlot[] slots = ResolveSlots(owner.Value, slotCount);

            // 재부여 거부. 매 타격마다 부여 시각을 갱신하면 "종료 전 3초에 걸었다"는 조건이 무너진다.
            // 쿨다운을 표식 지속시간과 같게 두는 전제이며, 더 길게 두려면 만료 뒤의 기록이 따로 필요하다
            for (int i = 0; i < slots.Length; ++i)
            {
                if ((slots[i].IsActiveAt(serverTime)) && (slots[i].Target.Equals(target)))
                {
                    rejection = MarkRejection.AlreadyMarked;
                    return false;
                }
            }

            int chosen = -1;
            int oldest = 0;

            for (int i = 0; i < slots.Length; ++i)
            {
                if (!slots[i].IsActiveAt(serverTime))
                {
                    chosen = i;
                    break;
                }

                if (slots[i].AppliedServerTime < slots[oldest].AppliedServerTime)
                {
                    oldest = i;
                }
            }

            if (chosen < 0)
            {
                chosen = oldest;
            }

            slots[chosen] = new MarkSlot(target, serverTime, serverTime + duration);
            return true;
        }

        /// <summary> 이 소유자가 이 대상에 걸어 둔 표식이 살아 있는가 </summary>
        public static bool IsMarkedBy(ParticipantId owner, TargetRef target, double serverTime)
        {
            if ((!owner.IsValid) || (!target.IsValid))
            {
                return false;
            }

            if (!_slotsByOwner.TryGetValue(owner.Value, out MarkSlot[] slots))
            {
                return false;
            }

            for (int i = 0; i < slots.Length; ++i)
            {
                if ((slots[i].IsActiveAt(serverTime)) && (slots[i].Target.Equals(target)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary> 누구든 이 대상에 표식을 걸어 두었는가 </summary>
        public static bool IsMarked(TargetRef target, double serverTime)
        {
            if (!target.IsValid)
            {
                return false;
            }

            foreach (KeyValuePair<ulong, MarkSlot[]> pair in _slotsByOwner)
            {
                MarkSlot[] slots = pair.Value;

                for (int i = 0; i < slots.Length; ++i)
                {
                    if ((slots[i].IsActiveAt(serverTime)) && (slots[i].Target.Equals(target)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 기준 시각으로부터 window 초 안에 이 대상에 걸린 표식이 있는가.
        /// 누가 걸었는지는 보지 않는다 - 무녀가 둘이어도 카운터는 한 번이다
        /// </summary>
        public static bool IsMarkedWithin(TargetRef target, double serverTime, double window)
        {
            if ((!target.IsValid) || (window <= 0d)
                || (double.IsNaN(serverTime)) || (double.IsInfinity(serverTime)))
            {
                return false;
            }

            foreach (KeyValuePair<ulong, MarkSlot[]> pair in _slotsByOwner)
            {
                MarkSlot[] slots = pair.Value;

                for (int i = 0; i < slots.Length; ++i)
                {
                    if ((!slots[i].IsActiveAt(serverTime)) || (!slots[i].Target.Equals(target)))
                    {
                        continue;
                    }

                    double elapsed = serverTime - slots[i].AppliedServerTime;
                    if ((elapsed >= 0d) && (elapsed <= window))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary> 이 소유자가 지금 유지 중인 표식 수 </summary>
        public static int CountActive(ParticipantId owner, double serverTime)
        {
            if ((!owner.IsValid) || (!_slotsByOwner.TryGetValue(owner.Value, out MarkSlot[] slots)))
            {
                return 0;
            }

            int count = 0;

            for (int i = 0; i < slots.Length; ++i)
            {
                if (slots[i].IsActiveAt(serverTime))
                {
                    ++count;
                }
            }

            return count;
        }

        /// <summary> 슬롯 하나를 읽는다. 비었거나 만료됐으면 false </summary>
        public static bool TryGetSlot(ParticipantId owner, int index, double serverTime, out MarkSlot slot)
        {
            slot = default;

            if ((!owner.IsValid) || (!_slotsByOwner.TryGetValue(owner.Value, out MarkSlot[] slots)))
            {
                return false;
            }

            if ((index < 0) || (index >= slots.Length) || (!slots[index].IsActiveAt(serverTime)))
            {
                return false;
            }

            slot = slots[index];
            return true;
        }

        /// <summary> 이탈이나 역할 교체로 한 사람의 표식을 거둔다 </summary>
        public static void ClearOwner(ParticipantId owner)
        {
            _slotsByOwner.Remove(owner.Value);
        }

        /// <summary> 씬 전환과 런 초기화에서 부른다. 대상 netId는 씬을 넘기면 의미가 없다 </summary>
        public static void ClearAll()
        {
            _slotsByOwner.Clear();
        }

        /// <summary> 도메인 리로드를 끈 환경에서 이전 플레이의 표식이 남지 않게 한다 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            _slotsByOwner.Clear();
        }

        private static MarkSlot[] ResolveSlots(ulong ownerValue, int slotCount)
        {
            if ((_slotsByOwner.TryGetValue(ownerValue, out MarkSlot[] slots)) && (slots.Length == slotCount))
            {
                return slots;
            }

            // 슬롯 수가 바뀌면 지금 것을 옮겨 담는다. 새로 만들면 유지 중인 표식이 조용히 사라진다
            var resized = new MarkSlot[slotCount];

            if (slots != null)
            {
                int copy = (slots.Length < slotCount) ? slots.Length : slotCount;
                for (int i = 0; i < copy; ++i)
                {
                    resized[i] = slots[i];
                }
            }

            _slotsByOwner[ownerValue] = resized;
            return resized;
        }
    }
}
