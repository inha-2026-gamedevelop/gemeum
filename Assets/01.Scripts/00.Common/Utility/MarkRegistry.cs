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

        /// <summary> 게임 시간은 부여 시각 이상, 만료 시각 미만일 때만 유효하다 </summary>
        public bool IsActiveAt(double gameTime)
        {
            return (Target.IsValid) &&
                   IsFinite(AppliedServerTime) &&
                   IsFinite(ExpiresServerTime) &&
                   IsFinite(gameTime) &&
                   (gameTime >= AppliedServerTime) &&
                   (gameTime < ExpiresServerTime);
        }

        private static bool IsFinite(double value)
        {
            return (!double.IsNaN(value)) && (!double.IsInfinity(value));
        }
    }

    /// <summary> 표식 부여가 거부된 이유 </summary>
    public enum MarkRejection
    {
        None          = 0,
        InvalidOwner  = 1, // 신원이 없는 요청
        InvalidTarget = 2, // 대상 netId가 0
        InvalidSlots  = 3, // 슬롯 수가 1 미만
        AlreadyMarked   = 4, // 같은 소유자가 같은 대상에 이미 걸어 두었다
        InvalidTime     = 5, // 게임 시각이 유한하지 않다
        InvalidDuration = 6, // 지속 시간이 0 이하이거나 만료 시각을 표현할 수 없다
        HistoryFull     = 7, // 재부여 이력이 가득 차 새 대상을 기록할 수 없다
    }

    /// <summary> 서버가 소유하는 표식 상태의 유일한 원본. 본체와 부위 효과, 보스 카운터가 모두 여기를 조회한다 </summary>
    public static class MarkRegistry
    {
        /****************************************
        *                Fields
        ****************************************/

        // 소유자(ParticipantId.Value)마다 슬롯 배열 하나. 파티가 세 명이라 순회 비용이 문제되지 않는다
        private const int MAX_REAPPLICATION_HISTORY = 32;

        private readonly struct ReapplicationRecord
        {
            public readonly TargetRef Target;
            public readonly double    BlockedUntilGameTime;

            public ReapplicationRecord(TargetRef target, double blockedUntilGameTime)
            {
                Target              = target;
                BlockedUntilGameTime = blockedUntilGameTime;
            }
        }

        private static readonly Dictionary<ulong, MarkSlot[]> _slotsByOwner =
            new Dictionary<ulong, MarkSlot[]>();

        // 슬롯에서 밀려난 대상도 원래 지속 시간이 끝날 때까지 재부여할 수 없어야 한다.
        // 만료된 기록은 TryApply 때 정리하고, 활성 기록에는 상한을 둬 정적 캐시가 자라지 않게 한다.
        private static readonly Dictionary<ulong, List<ReapplicationRecord>> _reapplicationByOwner =
            new Dictionary<ulong, List<ReapplicationRecord>>();

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 표식을 건다. serverTime에는 정지 시간을 뺀 게임 시간을 넣는다 </summary>
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

            if (!IsFinite(serverTime))
            {
                rejection = MarkRejection.InvalidTime;
                return false;
            }

            double expiresServerTime = serverTime + duration;
            if ((!IsFinite(duration)) || (duration <= 0d)
                || (!IsFinite(expiresServerTime))
                || (expiresServerTime <= serverTime))
            {
                rejection = MarkRejection.InvalidDuration;
                return false;
            }

            List<ReapplicationRecord> history = ResolveHistory(owner.Value);
            PruneHistory(history, serverTime);

            for (int i = 0; i < history.Count; ++i)
            {
                if ((history[i].Target.Equals(target)) &&
                    (serverTime < history[i].BlockedUntilGameTime))
                {
                    rejection = MarkRejection.AlreadyMarked;
                    return false;
                }
            }

            if (history.Count >= MAX_REAPPLICATION_HISTORY)
            {
                rejection = MarkRejection.HistoryFull;
                return false;
            }

            MarkSlot[] slots = ResolveSlots(owner.Value, slotCount);

            // 슬롯 안의 같은 대상도 재부여하지 않는다. 재부여 이력 검사는 슬롯에서 밀려난 대상까지 포함한다.
            // 매 타격마다 부여 시각을 갱신하면 "종료 전 3초에 걸었다"는 조건이 무너진다.
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

            slots[chosen] = new MarkSlot(target, serverTime, expiresServerTime);
            history.Add(new ReapplicationRecord(target, expiresServerTime));
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
                || (!IsFinite(serverTime)) || (!IsFinite(window)))
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

        /// <summary> 기준 창 안에 이 대상을 표식한 참가자를 결과 버퍼에 담는다 </summary>
        public static int CollectOwnersMarkedWithin(TargetRef target, double serverTime, double window,
            List<ParticipantId> destination)
        {
            if (destination == null)
            {
                return 0;
            }

            destination.Clear();
            if ((!target.IsValid) || (window <= 0d) ||
                (!IsFinite(serverTime)) || (!IsFinite(window)))
            {
                return 0;
            }

            foreach (KeyValuePair<ulong, MarkSlot[]> pair in _slotsByOwner)
            {
                MarkSlot[] slots = pair.Value;
                for (int i = 0; i < slots.Length; ++i)
                {
                    MarkSlot slot = slots[i];
                    if ((!slot.Target.Equals(target)) ||
                        (!slot.IsActiveAt(serverTime)) ||
                        ((serverTime - slot.AppliedServerTime) > window))
                    {
                        continue;
                    }

                    destination.Add(new ParticipantId(pair.Key));
                    break;
                }
            }

            destination.Sort((left, right) => left.Value.CompareTo(right.Value));
            return destination.Count;
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
            if (!owner.IsValid)
            {
                return;
            }

            _slotsByOwner.Remove(owner.Value);
            _reapplicationByOwner.Remove(owner.Value);
        }

        /// <summary> 대상이 사라졌을 때 그 대상의 슬롯과 재부여 이력을 거둔다 </summary>
        public static void ClearTarget(TargetRef target)
        {
            if (!target.IsValid)
            {
                return;
            }

            foreach (KeyValuePair<ulong, MarkSlot[]> pair in _slotsByOwner)
            {
                MarkSlot[] slots = pair.Value;

                for (int i = 0; i < slots.Length; ++i)
                {
                    if (slots[i].Target.Equals(target))
                    {
                        slots[i] = default;
                    }
                }
            }

            foreach (KeyValuePair<ulong, List<ReapplicationRecord>> pair in _reapplicationByOwner)
            {
                List<ReapplicationRecord> history = pair.Value;

                for (int i = history.Count - 1; i >= 0; --i)
                {
                    if (history[i].Target.Equals(target))
                    {
                        history.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary> 대상 오브젝트가 파괴될 때 런과 씬 세대에 관계없이 해당 netId를 거둔다 </summary>
        public static void ClearEntity(uint entityNetId)
        {
            if (entityNetId == 0u)
            {
                return;
            }

            foreach (KeyValuePair<ulong, MarkSlot[]> pair in _slotsByOwner)
            {
                MarkSlot[] slots = pair.Value;

                for (int i = 0; i < slots.Length; ++i)
                {
                    if (slots[i].Target.EntityNetId == entityNetId)
                    {
                        slots[i] = default;
                    }
                }
            }

            foreach (KeyValuePair<ulong, List<ReapplicationRecord>> pair in _reapplicationByOwner)
            {
                List<ReapplicationRecord> history = pair.Value;

                for (int i = history.Count - 1; i >= 0; --i)
                {
                    if (history[i].Target.EntityNetId == entityNetId)
                    {
                        history.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary> 씬 전환과 런 초기화에서 부른다. 대상 netId는 씬을 넘기면 의미가 없다 </summary>
        public static void ClearAll()
        {
            _slotsByOwner.Clear();
            _reapplicationByOwner.Clear();
        }

        /// <summary> 도메인 리로드를 끈 환경에서 이전 플레이의 표식이 남지 않게 한다 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            ClearAll();
        }

        private static List<ReapplicationRecord> ResolveHistory(ulong ownerValue)
        {
            if (_reapplicationByOwner.TryGetValue(ownerValue, out List<ReapplicationRecord> history))
            {
                return history;
            }

            history = new List<ReapplicationRecord>(MAX_REAPPLICATION_HISTORY);
            _reapplicationByOwner[ownerValue] = history;
            return history;
        }

        private static void PruneHistory(List<ReapplicationRecord> history, double gameTime)
        {
            for (int i = history.Count - 1; i >= 0; --i)
            {
                if (history[i].BlockedUntilGameTime <= gameTime)
                {
                    history.RemoveAt(i);
                }
            }
        }

        private static bool IsFinite(double value)
        {
            return (!double.IsNaN(value)) && (!double.IsInfinity(value));
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
