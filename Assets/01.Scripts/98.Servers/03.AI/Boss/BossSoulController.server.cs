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
    public partial class BossSoulController
    {
        public override void OnStartServer()
        {
            ServerInitialize(GameDB.Boss);
        }

        [Server]
        internal void ServerInitialize(BossPatternSO data)
        {
            _data = data;
            _snapshot = new BossSoulSnapshot
            {
                State             = BossFinalState.Dormant,
                Revision          = 1u,
                ChangedServerTime = NetworkTime.time,
            };
            ApplyHitboxes(_snapshot);
        }

        [Server]
        internal void ServerBeginFinal(double gameplayTime)
        {
            if ((_data == null) || (_snapshot.State != BossFinalState.Dormant) ||
                (!IsFinite(gameplayTime)))
            {
                return;
            }

            float health = Mathf.Max(0f, _data.WonhonHealth);
            BossSoulSnapshot next = _snapshot;
            next.State                      = BossFinalState.SoulWindow;
            next.Soul1Health                = health;
            next.Soul2Health                = health;
            next.Soul3Health                = health;
            next.RemovedMask                = 0;
            next.FailureBranchCount          = 0;
            next.WindowDeadlineGameplayTime = gameplayTime + _data.FinalGauntletWindowSeconds;
            Commit(next);
            _boss?.ServerHandleSoulStateChanged(next);
        }

        [Server]
        internal void ServerTick(double gameplayTime)
        {
            if ((_data == null) || (!IsFinite(gameplayTime)))
            {
                return;
            }

            if (_snapshot.State == BossFinalState.SoulWindow)
            {
                ResolveMarkedSouls(gameplayTime);
                if ((_snapshot.State == BossFinalState.SoulWindow) &&
                    (gameplayTime >= _snapshot.WindowDeadlineGameplayTime))
                {
                    if (AllSoulsRemoved())
                    {
                        CommitKillable();
                        return;
                    }

                    BossSoulSnapshot next = _snapshot;
                    next.State = BossFinalState.FailureAttack;
                    next.FailureBranchCount = next.RemainingCount;
                    Commit(next);
                    _boss?.ServerHandleSoulStateChanged(next);
                }
            }
        }

        [Server]
        internal float ServerApplyDamage(byte soulId, in DamageInfo damage)
        {
            if ((!CanDamageSouls()) || _snapshot.IsRemoved(soulId) ||
                (!IsFinitePositive(damage.Amount)))
            {
                return 0f;
            }

            float previous = _snapshot.GetHealth(soulId);
            float applied = Mathf.Min(previous, damage.Amount);
            if (!IsFinitePositive(applied))
            {
                return 0f;
            }

            BossSoulSnapshot next = _snapshot;
            SetHealth(ref next, soulId, previous - applied);
            bool removed = next.GetHealth(soulId) <= Constants.Combat.HEALTH_EPSILON;
            if (removed)
            {
                next.RemovedMask = (byte) (next.RemovedMask | (1 << (soulId - 1)));
            }

            Commit(next);
            RecordDamage(soulId, damage, applied);

            if (removed)
            {
                MarkRegistry.ClearTarget(CreateTargetRef(soulId));
                TryCommitImmediateSuccess();
            }

            return applied;
        }

        [Server]
        internal bool ServerTryCounterSoul(byte soulId, CharacterKind character,
            ParticipantId actor, uint actionId)
        {
            if ((!CanDamageSouls()) || (!actor.IsValid) || (actionId == 0u) ||
                _snapshot.IsRemoved(soulId) || (!_data.TryGetSoulCounter(soulId, out CharacterKind expected)) ||
                (expected != character))
            {
                return false;
            }

            float applied = _snapshot.GetHealth(soulId);
            if (!IsFinitePositive(applied))
            {
                return false;
            }

            BossSoulSnapshot next = _snapshot;
            SetHealth(ref next, soulId, 0f);
            next.RemovedMask = (byte) (next.RemovedMask | (1 << (soulId - 1)));
            Commit(next);
            RecordCounter(soulId, character, actor, actionId, applied);
            MarkRegistry.ClearTarget(CreateTargetRef(soulId));
            TryCommitImmediateSuccess();
            return true;
        }

        [Server]
        internal void ServerNotifyFailureAttackEnded()
        {
            if (_snapshot.State != BossFinalState.FailureAttack)
            {
                return;
            }

            if (AllSoulsRemoved())
            {
                CommitKillable();
                return;
            }

            BossSoulSnapshot next = _snapshot;
            next.State = BossFinalState.SoulWindow;
            next.FailureBranchCount = 0;
            next.WindowDeadlineGameplayTime = RunTimeLocator.Current.GameplayElapsedSeconds +
                _data.FinalGauntletWindowSeconds;
            Commit(next);
            _boss?.ServerHandleSoulStateChanged(next);
        }

        private void ResolveMarkedSouls(double gameplayTime)
        {
            for (byte soulId = 1; soulId <= 3; ++soulId)
            {
                if (_snapshot.IsRemoved(soulId) ||
                    (!_data.TryGetSoulCounter(soulId, out CharacterKind character)) ||
                    (character != CharacterKind.Munyeo))
                {
                    continue;
                }

                TargetRef target = CreateTargetRef(soulId);
                if (MarkRegistry.CollectOwnersMarkedWithin(target, gameplayTime,
                    _data.BrandWindow, _markOwners) == 0)
                {
                    continue;
                }

                float applied = _snapshot.GetHealth(soulId);
                ParticipantId first = _markOwners[0];
                uint actionId = _snapshot.Revision;
                if (!ServerTryCounterSoul(soulId, CharacterKind.Munyeo, first, actionId))
                {
                    continue;
                }

                for (int i = 1; i < _markOwners.Count; ++i)
                {
                    RecordCounter(soulId, CharacterKind.Munyeo, _markOwners[i],
                        actionId, applied);
                }
            }
        }

        private void TryCommitImmediateSuccess()
        {
            // D10: 이미 시작한 백귀야행은 마지막 원혼이 사라져도 끝까지 실행한다
            if ((_snapshot.State == BossFinalState.SoulWindow) && AllSoulsRemoved())
            {
                CommitKillable();
            }
        }

        private void CommitKillable()
        {
            BossSoulSnapshot next = _snapshot;
            next.State = BossFinalState.Killable;
            next.FailureBranchCount = 0;
            Commit(next);
            _boss?.ServerHandleSoulStateChanged(next);
        }

        private bool CanDamageSouls()
        {
            return (_snapshot.State == BossFinalState.SoulWindow) ||
                   (_snapshot.State == BossFinalState.FailureAttack);
        }

        private bool AllSoulsRemoved()
        {
            return ((_snapshot.RemovedMask & ALL_SOULS_REMOVED_MASK) == ALL_SOULS_REMOVED_MASK);
        }

        private TargetRef CreateTargetRef(byte soulId)
        {
            RunManager run = RunManager.ServerInstance;
            RunSceneDirector director = RunSceneDirector.ServerInstance;
            return TargetRef.Create(
                (run == null) ? default : run.RunId,
                (director == null) ? default : director.Revision,
                netId,
                BossTargetPartIds.SoulTargetPartId(soulId));
        }

        private void RecordDamage(byte soulId, in DamageInfo damage, float applied)
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
                    run.RunId, damage.AttackerParticipant, CreateTargetRef(soulId), applied));
            }
            catch (Exception exception)
            {
                Debug.LogError($"[BossSoulController] damage fact producer failed: {exception}", this);
            }
        }

        private void RecordCounter(byte soulId, CharacterKind character, ParticipantId actor,
            uint actionId, float applied)
        {
            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (!run.RunId.IsValid) || (!actor.IsValid) ||
                (!IsFinitePositive(applied)))
            {
                return;
            }

            RunCounterKind kind = (character == CharacterKind.Chasa)
                ? RunCounterKind.Parry
                : ((character == CharacterKind.Singung)
                    ? RunCounterKind.Precision
                    : RunCounterKind.Resonance);
            try
            {
                RunStatisticsSinkLocator.Current.RecordCounter(new RunCounterFact(
                    run.RunId, kind, actor, CreateTargetRef(soulId), actionId, applied));
            }
            catch (Exception exception)
            {
                Debug.LogError($"[BossSoulController] counter fact producer failed: {exception}", this);
            }
        }

        private void Commit(BossSoulSnapshot next)
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

        private static void SetHealth(ref BossSoulSnapshot snapshot, byte soulId, float value)
        {
            value = Mathf.Max(0f, value);
            switch (soulId)
            {
                case 1: snapshot.Soul1Health = value; break;
                case 2: snapshot.Soul2Health = value; break;
                case 3: snapshot.Soul3Health = value; break;
            }
        }

        private static bool IsFinite(double value)
        {
            return (!double.IsNaN(value)) && (!double.IsInfinity(value));
        }

        private static bool IsFinitePositive(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value)) && (value > 0f);
        }
    }
}
