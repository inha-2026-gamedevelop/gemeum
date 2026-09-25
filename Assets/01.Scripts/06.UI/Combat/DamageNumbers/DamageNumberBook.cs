// System
using System;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.UI
{
    /// <summary> 피해 결과 하나를 장부에 넣은 결과 </summary>
    public enum DamageNumberAction : byte
    {
        Ignored  = 0, // 표시하지 않는다(0 피해, 화면 밖·가린 새 숫자, 상한에서 밀려남)
        Spawned  = 1, // 빈 칸에 새 숫자
        Merged   = 2, // 기존 숫자에 더했다. 위치·수명은 그대로
        Replaced = 3, // 상한이라 오래된 숫자 칸을 새 숫자로 덮었다
    }

    /// <summary>
    /// 화면에 떠 있는 데미지 숫자의 순수 장부(COMBAT_STYLE_DESIGN S10 합산·밀도 규칙)
    /// 고정 크기 칸만 쓰고 할당하지 않는다. 표시(MonoBehaviour)는 칸 번호로 따라간다
    /// 이것은 표시 축약이다. 서버 피해 집계와 무관하다
    /// </summary>
    public sealed class DamageNumberBook
    {
        /****************************************
        *                Types
        ****************************************/

        public struct Slot
        {
            public bool            IsActive;
            public DamageNumberKey Key;

            /// <summary> 반올림 전 실제 피해 합계 </summary>
            public float Amount;

            /// <summary> 합쳐진 피해 중 가장 높은 표현 단계 </summary>
            public ImpactTier Tier;

            /// <summary> 숫자가 처음 생긴 시각. 합산으로 늦추지 않는다 </summary>
            public float SpawnTime;

            /// <summary> 생성 순서. 같은 시각의 오래된 숫자를 가른다 </summary>
            public long Order;
        }

        /****************************************
        *                Fields
        ****************************************/

        private readonly Slot[] _slots;
        private int  _activeCount = 0;
        private long _nextOrder   = 0;

        /****************************************
        *              Properties
        ****************************************/

        public int Capacity    => _slots.Length;
        public int ActiveCount => _activeCount;

        /****************************************
        *              Constructor
        ****************************************/

        public DamageNumberBook(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "데미지 숫자 칸은 1개 이상이어야 한다");
            }

            _slots = new Slot[capacity];
        }

        /****************************************
        *              Public API
        ****************************************/

        public ref readonly Slot Get(int index)
        {
            return ref _slots[index];
        }

        /// <summary>
        /// 피해 결과 하나를 장부에 넣는다
        /// canSpawn이 false(화면 밖·카메라 뒤·지형 가림)면 기존 숫자에 합치는 것만 허용하고 새 숫자는 만들지 않는다
        /// </summary>
        public DamageNumberAction Add(in DamageResult result, ImpactTier tier, float now,
            in DamageNumberRules rules, bool canSpawn, out int slotIndex)
        {
            slotIndex = -1;

            if (!DamageNumberFormatter.IsDisplayable(result.Amount))
            {
                return DamageNumberAction.Ignored;
            }

            DamageNumberKey key = DamageNumberKey.From(result);

            // 1. 창 안의 같은 키는 한 숫자로 합친다
            if (key.CanMerge)
            {
                int recent = FindSameKey(key, now, rules.MergeWindowSeconds, true);
                if (recent >= 0)
                {
                    Merge(recent, result.Amount, tier);
                    slotIndex = recent;
                    return DamageNumberAction.Merged;
                }
            }

            if (!canSpawn)
            {
                return DamageNumberAction.Ignored;
            }

            int maxOnScreen    = Math.Min(rules.MaxOnScreen, _slots.Length);
            bool isTargetFull  = (CountForTarget(key.TargetNetId) >= rules.MaxPerTarget);
            bool isScreenFull  = (_activeCount >= maxOnScreen);

            // 2. 상한 안이면 빈 칸에 새 숫자
            if ((!isTargetFull) && (!isScreenFull))
            {
                int free = FindFree();
                if (free >= 0)
                {
                    Write(free, key, result.Amount, tier, now);
                    slotIndex = free;
                    return DamageNumberAction.Spawned;
                }
            }

            // 3. 상한이면 같은 키 숫자에 창과 무관하게 합친다. 수명은 늘리지 않는다
            if (key.CanMerge)
            {
                int same = FindSameKey(key, now, 0f, false);
                if (same >= 0)
                {
                    Merge(same, result.Amount, tier);
                    slotIndex = same;
                    return DamageNumberAction.Merged;
                }
            }

            // 4. 합칠 수 없으면 오래된 일반 숫자를 교체한다. 대상 상한이 찼으면 그 대상 안에서 고른다
            bool hasScope    = isTargetFull;
            uint scopeTarget = key.TargetNetId;
            int  victim      = FindOldest(hasScope, scopeTarget, true);

            // 일반 숫자가 없으면 치명만 치명을 밀어낼 수 있다. 일반은 치명을 밀어내지 않는다
            if ((victim < 0) && key.IsCritical)
            {
                victim = FindOldest(hasScope, scopeTarget, false);
            }

            if (victim < 0)
            {
                return DamageNumberAction.Ignored;
            }

            _slots[victim].IsActive = false;
            --_activeCount;

            Write(victim, key, result.Amount, tier, now);
            slotIndex = victim;
            return DamageNumberAction.Replaced;
        }

        /// <summary> 수명이 끝난 칸을 비운다 </summary>
        public void Release(int index)
        {
            if (!_slots[index].IsActive)
            {
                return;
            }

            _slots[index] = default;
            --_activeCount;
        }

        public void Clear()
        {
            Array.Clear(_slots, 0, _slots.Length);
            _activeCount = 0;
        }

        public int CountForTarget(uint targetNetId)
        {
            int count = 0;

            for (int i = 0; i < _slots.Length; ++i)
            {
                if (_slots[i].IsActive && (_slots[i].Key.TargetNetId == targetNetId))
                {
                    ++count;
                }
            }

            return count;
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Merge(int index, float amount, ImpactTier tier)
        {
            // 반올림 전 실제 값으로 더한다. 처치로 단계가 올라도 치명 여부는 키에 남는다
            _slots[index].Amount += amount;

            if (tier > _slots[index].Tier)
            {
                _slots[index].Tier = tier;
            }
        }

        private void Write(int index, DamageNumberKey key, float amount, ImpactTier tier, float now)
        {
            _slots[index] = new Slot
            {
                IsActive  = true,
                Key       = key,
                Amount    = amount,
                Tier      = tier,
                SpawnTime = now,
                Order     = _nextOrder++,
            };

            ++_activeCount;
        }

        private int FindFree()
        {
            for (int i = 0; i < _slots.Length; ++i)
            {
                if (!_slots[i].IsActive)
                {
                    return i;
                }
            }

            return -1;
        }

        // useWindow가 참이면 생성 뒤 window 초 안의 숫자만 본다. 여럿이면 가장 최근 것
        private int FindSameKey(in DamageNumberKey key, float now, float window, bool useWindow)
        {
            int  found      = -1;
            long foundOrder = long.MinValue;

            for (int i = 0; i < _slots.Length; ++i)
            {
                if ((!_slots[i].IsActive) || (!_slots[i].Key.Equals(key)))
                {
                    continue;
                }

                if (useWindow && ((now - _slots[i].SpawnTime) > window))
                {
                    continue;
                }

                if (_slots[i].Order > foundOrder)
                {
                    found      = i;
                    foundOrder = _slots[i].Order;
                }
            }

            return found;
        }

        private int FindOldest(bool hasScope, uint targetNetId, bool normalOnly)
        {
            int  found      = -1;
            long foundOrder = long.MaxValue;

            for (int i = 0; i < _slots.Length; ++i)
            {
                if (!_slots[i].IsActive)
                {
                    continue;
                }

                if (hasScope && (_slots[i].Key.TargetNetId != targetNetId))
                {
                    continue;
                }

                if (normalOnly && _slots[i].Key.IsCritical)
                {
                    continue;
                }

                if (_slots[i].Order < foundOrder)
                {
                    found      = i;
                    foundOrder = _slots[i].Order;
                }
            }

            return found;
        }
    }
}
