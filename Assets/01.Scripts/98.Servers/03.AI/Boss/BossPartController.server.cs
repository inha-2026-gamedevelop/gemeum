// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.AI
{
    public partial class BossPartController
    {
        public override void OnStartServer()
        {
            ServerInitialize(GameDB.Boss);
        }

        [Server]
        internal void ServerInitialize(BossPatternSO data)
        {
            _data = data;
            float health = (_data == null) ? 0f : Mathf.Max(0f, _data.TailSegmentHealth);
            _snapshot = new BossPartSnapshot
            {
                Part1Health              = health,
                Part2Health              = health,
                Part3Health              = health,
                BrokenMask               = 0,
                ActivePartId             = 0,
                GroggyGeneration         = 0u,
                BrokePartThisGeneration  = false,
                AttackDamageMultiplier   = 1f,
                Revision                 = 1u,
                ChangedServerTime        = NetworkTime.time,
            };

            if (_status != null)
            {
                _status.ServerSetHealthFloorRatio((_data == null) ? 0f : _data.HealthGateRatio);
            }

            ApplyHitboxes(_snapshot);
        }

        [Server]
        internal void ServerBeginGroggy(uint generation)
        {
            if ((_data == null) || (generation == 0u) || AllPartsBroken)
            {
                return;
            }

            BossPartSnapshot next = _snapshot;
            next.GroggyGeneration        = generation;
            next.BrokePartThisGeneration = false;
            next.ActivePartId            = FirstUnbrokenPart(next.BrokenMask);
            Commit(next);
        }

        [Server]
        internal void ServerEndGroggy(uint generation)
        {
            if ((generation == 0u) || (_snapshot.GroggyGeneration != generation))
            {
                return;
            }

            BossPartSnapshot next = _snapshot;
            bool unresolved = (next.ActivePartId != 0) && (!next.IsBroken(next.ActivePartId));
            next.ActivePartId = 0;
            next.AttackDamageMultiplier = unresolved
                ? Mathf.Max(1f, _data.UnbrokenTailAttackMultiplier)
                : 1f;
            Commit(next);
        }

        [Server]
        internal float ServerApplyDamage(byte partId, in DamageInfo damage, Vector3 hitPosition)
        {
            if ((_data == null) || (partId == 0) || (partId != _snapshot.ActivePartId) ||
                _snapshot.BrokePartThisGeneration || _snapshot.IsBroken(partId) ||
                (!IsFinitePositive(damage.Amount)))
            {
                return 0f;
            }

            float previousHealth = _snapshot.GetHealth(partId);
            float scaledDamage = damage.Amount * _data.GroggyTailDamageMultiplier;
            float applied = Mathf.Min(previousHealth, scaledDamage);
            if (!IsFinitePositive(applied))
            {
                return 0f;
            }

            BossPartSnapshot next = _snapshot;
            SetHealth(ref next, partId, previousHealth - applied);
            bool broke = next.GetHealth(partId) <= Constants.Combat.HEALTH_EPSILON;
            if (broke)
            {
                next.BrokenMask = (byte) (next.BrokenMask | (1 << (partId - 1)));
                next.ActivePartId = 0;
                next.BrokePartThisGeneration = true;
                next.AttackDamageMultiplier = 1f;
            }

            Commit(next);
            RecordAppliedDamage(partId, damage, applied);

            // 꼬리 피해는 본체 HP로 옮겨지지 않는다. 실제로 맞은 부위 한 건만 보고한다
            ServerDamageResultHub.ServerReport(damage, netId, (sbyte) partId, true, applied, broke,
                hitPosition);

            if (broke)
            {
                CommitBreak(partId, damage.AttackerNetId, hitPosition);
            }

            return applied;
        }

        [Server]
        internal bool ServerIsPatternSealed(int patternId)
        {
            if ((_data == null) || (patternId <= 0))
            {
                return false;
            }

            for (byte partId = BossTargetPartIds.Tail1; partId <= BossTargetPartIds.Tail3; ++partId)
            {
                if (_snapshot.IsBroken(partId) &&
                    _data.TryGetSealedPattern(partId, out int sealedPatternId) &&
                    (sealedPatternId == patternId))
                {
                    return true;
                }
            }

            return false;
        }

        [Server]
        internal bool ServerIsTransferredCounterPattern(CharacterKind character, int patternId)
        {
            if ((_data == null) || (patternId <= 0))
            {
                return false;
            }

            for (byte partId = BossTargetPartIds.Tail1; partId <= BossTargetPartIds.Tail3; ++partId)
            {
                if (_snapshot.IsBroken(partId) &&
                    _data.TryGetCounterOpportunityTransfer(partId, character,
                        out BossPatternSO.CounterOpportunityTransferEntry transfer) &&
                    (transfer.FallbackPatternId == patternId))
                {
                    return true;
                }
            }

            return false;
        }

        [Server]
        internal bool ServerIsTransferredParryRepeat(int patternId, int repeatIndex)
        {
            if ((_data == null) || (patternId <= 0) || (repeatIndex < 0))
            {
                return false;
            }

            for (byte partId = BossTargetPartIds.Tail1; partId <= BossTargetPartIds.Tail3; ++partId)
            {
                if (_snapshot.IsBroken(partId) &&
                    _data.TryGetCounterOpportunityTransfer(partId, CharacterKind.Chasa,
                        out BossPatternSO.CounterOpportunityTransferEntry transfer) &&
                    (transfer.FallbackPatternId == patternId) &&
                    ((transfer.RepeatIndex < 0) || (transfer.RepeatIndex == repeatIndex)))
                {
                    return true;
                }
            }

            return false;
        }

        [Server]
        private void CommitBreak(byte partId, uint killerNetId, Vector3 hitPosition)
        {
            TargetRef target = CreateTargetRef(partId);
            MarkRegistry.ClearTarget(target);

            if (_data.TryGetSealedPattern(partId, out int sealedPatternId) &&
                GameEventBus.HasInstance)
            {
                GameEventBus.Instance.Publish(new BossPartBrokenEvent(
                    target, hitPosition, killerNetId, sealedPatternId));
            }

            _boss?.ServerHandlePartBroken(partId, AllPartsBroken);
            RpcPlayPartBreak();
        }

        private TargetRef CreateTargetRef(byte partId)
        {
            RunManager run = RunManager.ServerInstance;
            RunSceneDirector director = RunSceneDirector.ServerInstance;
            return TargetRef.Create(
                (run == null) ? default : run.RunId,
                (director == null) ? default : director.Revision,
                netId,
                partId);
        }

        private void RecordAppliedDamage(byte partId, in DamageInfo damage, float applied)
        {
            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (!run.RunId.IsValid) || (!damage.AttackerParticipant.IsValid) ||
                (!IsFinitePositive(applied)))
            {
                return;
            }

            try
            {
                RunStatisticsSinkLocator.Current.RecordDamage(new RunAppliedDamageFact(
                    run.RunId, damage.AttackerParticipant, CreateTargetRef(partId), applied));
            }
            catch (Exception exception)
            {
                Debug.LogError($"[BossPartController] damage fact producer failed: {exception}", this);
            }
        }

        private void Commit(BossPartSnapshot next)
        {
            ++next.Revision;
            if (next.Revision == 0u)
            {
                next.Revision = 1u;
            }

            next.ChangedServerTime = NetworkTime.time;
            _snapshot = next;
            ApplyHitboxes(next);
        }

        private static byte FirstUnbrokenPart(byte brokenMask)
        {
            for (byte id = BossTargetPartIds.Tail1; id <= BossTargetPartIds.Tail3; ++id)
            {
                if ((brokenMask & (1 << (id - 1))) == 0)
                {
                    return id;
                }
            }

            return 0;
        }

        private static void SetHealth(ref BossPartSnapshot snapshot, byte partId, float value)
        {
            value = Mathf.Max(0f, value);
            switch (partId)
            {
                case BossTargetPartIds.Tail1: snapshot.Part1Health = value; break;
                case BossTargetPartIds.Tail2: snapshot.Part2Health = value; break;
                case BossTargetPartIds.Tail3: snapshot.Part3Health = value; break;
            }
        }

        private static bool IsFinitePositive(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value)) && (value > 0f);
        }
    }
}
