// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary>
    /// 서버가 확정한 피해 결과를 공격자별로 모아 두는 순수 대기열(COMBAT_STYLE_DESIGN 8.1)
    /// NetworkServer 없이 동작해 EditMode에서 검증한다. 누가 받을지와 언제 보낼지는 ServerDamageResultHub와 PlayerAttack이 정한다
    /// </summary>
    public sealed class DamageResultQueue
    {
        /****************************************
        *              Constants
        ****************************************/

        /// <summary> 공격자 한 명이 쌓아 둘 수 있는 최대 건수. 넘치면 가장 오래된 결과부터 버린다 </summary>
        public const int MAX_PENDING_PER_ATTACKER = 256;

        // 사건 번호 0은 "없음"으로 남겨 둔다
        private const uint FIRST_EVENT_ID = 1u;

        /****************************************
        *                Types
        ****************************************/

        private sealed class Pending
        {
            public readonly Queue<DamageResult> Items = new Queue<DamageResult>();
            public int Dropped;
        }

        /****************************************
        *                Fields
        ****************************************/

        private readonly Dictionary<uint, Pending> _pendingByAttacker = new Dictionary<uint, Pending>();
        private readonly int _capacity;

        private uint _nextEventId;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 다음에 발급할 사건 번호 </summary>
        public uint NextEventId => _nextEventId;

        /****************************************
        *                Methods
        ****************************************/

        public DamageResultQueue()
            : this(MAX_PENDING_PER_ATTACKER, FIRST_EVENT_ID)
        {
        }

        /// <summary> 테스트가 상한과 번호 순환을 좁혀 검증할 수 있게 연다 </summary>
        public DamageResultQueue(int capacity, uint firstEventId)
        {
            _capacity    = Mathf.Max(1, capacity);
            _nextEventId = (firstEventId == 0u) ? FIRST_EVENT_ID : firstEventId;
        }

        /// <summary>
        /// 실제로 줄어든 HP 한 건을 결과로 만들어 공격자 대기열에 넣는다
        /// 가해자가 없거나 적용량이 DAMAGE_EPSILON 이하이면 만들지 않는다
        /// </summary>
        public bool TryEnqueue(in DamageInfo damage, uint targetNetId, sbyte partIndex, bool targetIsBoss,
            float appliedAmount, bool isKill, Vector3 targetCenter, Vector3 attackerPosition,
            double gameplayTime, double serverTime, out DamageResult result)
        {
            result = default;

            uint attackerNetId = damage.AttackerNetId;
            if ((attackerNetId == DamageInfo.NO_ATTACKER) || (!IsFinite(appliedAmount)) ||
                (appliedAmount <= Constants.Combat.DAMAGE_EPSILON))
            {
                return false;
            }

            result = new DamageResult
            {
                EventId       = TakeNextEventId(),
                AttackerNetId = attackerNetId,
                ExecutionId   = damage.ExecutionId,
                Source        = damage.Source,
                Detail        = damage.Detail,
                TargetNetId   = targetNetId,
                PartIndex     = partIndex,
                TargetIsBoss  = targetIsBoss,
                Amount        = appliedAmount,
                IsCritical    = damage.IsCritical,
                IsKill        = isKill,
                HitPoint      = damage.HasHitPoint ? damage.HitPoint : targetCenter,
                HitDirection  = ResolveHitDirection(attackerPosition, targetCenter),
                GameplayTime  = gameplayTime,
                ServerTime    = serverTime,
            };

            Pending pending = GetOrCreate(attackerNetId);
            if (pending.Items.Count >= _capacity)
            {
                // 표현은 최근 것이 중요하다. 늦은 결과는 수신 쪽에서도 버려진다
                pending.Items.Dequeue();
                ++pending.Dropped;
            }

            pending.Items.Enqueue(result);
            return true;
        }

        /// <summary> 한 공격자의 결과를 buffer 크기만큼 꺼낸다. 남은 것은 다음 호출에 나온다 </summary>
        public int Drain(uint attackerNetId, DamageResult[] buffer, out int droppedSinceLastDrain)
        {
            droppedSinceLastDrain = 0;
            if ((buffer == null) || (!_pendingByAttacker.TryGetValue(attackerNetId, out Pending pending)))
            {
                return 0;
            }

            droppedSinceLastDrain = pending.Dropped;
            pending.Dropped       = 0;

            int count = 0;
            while ((count < buffer.Length) && (pending.Items.Count > 0))
            {
                buffer[count] = pending.Items.Dequeue();
                ++count;
            }

            return count;
        }

        /// <summary> 아직 보내지 않은 결과 수 </summary>
        public int GetPendingCount(uint attackerNetId)
        {
            return _pendingByAttacker.TryGetValue(attackerNetId, out Pending pending)
                ? pending.Items.Count
                : 0;
        }

        /// <summary>
        /// 한 공격자의 대기열을 버린다. 연결 해제·씬 전환·런 리셋에 쓴다
        /// 씬마다 netId가 새로 발급되므로 항목째 지워 지난 netId가 쌓이지 않게 한다
        /// </summary>
        public void ClearAttacker(uint attackerNetId)
        {
            _pendingByAttacker.Remove(attackerNetId);
        }

        /// <summary> 모든 대기열을 버린다. 사건 번호는 되돌리지 않는다 </summary>
        public void Clear()
        {
            _pendingByAttacker.Clear();
        }

        /// <summary> 가해자에서 대상으로 향하는 수평 방향(정규화). 같은 자리거나 유한하지 않으면 zero </summary>
        public static Vector3 ResolveHitDirection(Vector3 from, Vector3 to)
        {
            Vector3 planar = new Vector3(to.x - from.x, 0f, to.z - from.z);
            float sqrMagnitude = planar.sqrMagnitude;
            if ((!IsFinite(sqrMagnitude)) || (sqrMagnitude <= Mathf.Epsilon))
            {
                return Vector3.zero;
            }

            return planar / Mathf.Sqrt(sqrMagnitude);
        }

        private uint TakeNextEventId()
        {
            uint eventId = _nextEventId;
            _nextEventId = (_nextEventId == uint.MaxValue) ? FIRST_EVENT_ID : (_nextEventId + 1u);
            return eventId;
        }

        private Pending GetOrCreate(uint attackerNetId)
        {
            if (!_pendingByAttacker.TryGetValue(attackerNetId, out Pending pending))
            {
                // 공격자마다 한 번 만들고 비울 때까지 같은 큐를 재사용한다
                pending = new Pending();
                _pendingByAttacker.Add(attackerNetId, pending);
            }

            return pending;
        }

        private static bool IsFinite(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }
    }
}
