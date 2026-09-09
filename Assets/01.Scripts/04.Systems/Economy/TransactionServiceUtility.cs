// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Systems
{
    /// <summary> 상점과 양도가 공유하는 호출자, 런, 생명, 거리 검증 </summary>
    internal static class TransactionServiceUtility
    {
        public static TransactionRejection ValidateHeader(NetworkConnectionToClient sender,
            in TransactionRequestHeader header, TransactionOperation operation)
        {
            if ((sender == null) || (sender.identity == null))
            {
                return TransactionRejection.InvalidRequest;
            }

            RunManager run = RunManager.ServerInstance;
            RunSceneDirector director = RunSceneDirector.ServerInstance;
            if ((run == null) || (director == null) || (!run.RunId.IsValid) ||
                (!director.Revision.IsValid))
            {
                return TransactionRejection.InvalidState;
            }

            return TransactionRequestRules.ValidateHeader(header, operation, run.RunId,
                director.Revision, sender.identity.netId, run.IsPaused,
                RunSceneDirector.IsServerTransitioning);
        }

        public static bool TryGetPlayer(NetworkConnectionToClient connection,
            out NetworkIdentity identity, out ParticipantId participant,
            out InventoryController inventory)
        {
            identity    = null;
            participant = ParticipantId.None;
            inventory   = null;

            if ((connection == null) || (connection.identity == null))
            {
                return false;
            }

            identity = connection.identity;
            var participantSource = identity.GetComponent<IGrowthParticipant>();
            inventory = identity.GetComponent<InventoryController>();
            if ((participantSource == null) || (!participantSource.Participant.IsValid) ||
                (inventory == null))
            {
                return false;
            }

            participant = participantSource.Participant;
            return true;
        }

        public static bool TryGetPlayer(uint netId, out NetworkIdentity identity,
            out ParticipantId participant, out InventoryController inventory)
        {
            identity    = null;
            participant = ParticipantId.None;
            inventory   = null;

            if ((netId == 0u) || (!NetworkServer.spawned.TryGetValue(netId, out identity)))
            {
                return false;
            }

            var participantSource = identity.GetComponent<IGrowthParticipant>();
            inventory = identity.GetComponent<InventoryController>();
            if ((participantSource == null) || (!participantSource.Participant.IsValid) ||
                (inventory == null))
            {
                return false;
            }

            participant = participantSource.Participant;
            return true;
        }

        public static TransactionRejection EvaluatePlayers(NetworkIdentity first,
            IDamageable firstLife, NetworkIdentity second, IDamageable secondLife)
        {
            if ((first == null) || (second == null) || (first == second) ||
                (!GameDB.IsInitialized) || (GameDB.Combat == null))
            {
                return TransactionRejection.InvalidRequest;
            }

            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (run.Phase == RunPhase.Lobby) ||
                (run.Phase == RunPhase.Result))
            {
                return TransactionRejection.InvalidState;
            }

            if (run.IsPaused || RunSceneDirector.IsServerTransitioning)
            {
                return TransactionRejection.Paused;
            }

            if (first.gameObject.scene.handle != second.gameObject.scene.handle)
            {
                return TransactionRejection.SceneMismatch;
            }

            if ((!IsAliveReference(firstLife)) || (!IsAliveReference(secondLife)))
            {
                return TransactionRejection.InvalidRequest;
            }

            if (firstLife.IsDead || secondLife.IsDead)
            {
                return TransactionRejection.Dead;
            }

            CombatConfigSO combat = GameDB.Combat;
            float distanceSquared = (first.transform.position - second.transform.position).sqrMagnitude;
            if (distanceSquared > (combat.InteractRange * combat.InteractRange))
            {
                return TransactionRejection.OutOfRange;
            }

            double now = RunTimeLocator.Current.GameplayElapsedSeconds;
            if (CombatClock.IsInCombat(first.netId, now, combat.InteractOutOfCombatSeconds) ||
                CombatClock.IsInCombat(second.netId, now, combat.InteractOutOfCombatSeconds))
            {
                return TransactionRejection.InCombat;
            }

            return TransactionRejection.None;
        }

        private static bool IsAliveReference(IDamageable life)
        {
            return ((life != null) && ((life as UnityEngine.Object) != null));
        }

        public static TransactionRejection EvaluatePlayer(NetworkIdentity player)
        {
            if ((player == null) || (!GameDB.IsInitialized) || (GameDB.Combat == null))
            {
                return TransactionRejection.InvalidRequest;
            }

            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (run.Phase == RunPhase.Lobby) ||
                (run.Phase == RunPhase.Result))
            {
                return TransactionRejection.InvalidState;
            }

            if (run.IsPaused || RunSceneDirector.IsServerTransitioning)
            {
                return TransactionRejection.Paused;
            }

            var life = player.GetComponent<IDamageable>();
            if (life == null)
            {
                return TransactionRejection.InvalidRequest;
            }

            if (life.IsDead)
            {
                return TransactionRejection.Dead;
            }

            CombatConfigSO combat = GameDB.Combat;
            if (CombatClock.IsInCombat(player.netId,
                RunTimeLocator.Current.GameplayElapsedSeconds,
                combat.InteractOutOfCombatSeconds))
            {
                return TransactionRejection.InCombat;
            }

            return TransactionRejection.None;
        }
    }
}
