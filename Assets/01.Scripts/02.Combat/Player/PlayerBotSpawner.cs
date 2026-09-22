// Unity
using UnityEngine;

// Third party
using Mirror;

// Project
using Geumeum.Common;
using Geumeum.Network;

namespace Geumeum.Combat
{
    /// <summary> 서버 수동 스폰 진입점. 로비 자동 충원과 씬 간 복구는 호출자가 담당한다 </summary>
    public static class PlayerBotSpawner
    {
        public static bool TrySpawn(GameObject playerPrefab, Vector3 position,
            Quaternion rotation, WeaponKind weapon, CharacterKind character,
            out NetworkIdentity bot)
        {
            bot = null;
            if ((!NetworkServer.active) || (playerPrefab == null) ||
                (PlayerStatus.ServerPlayers.Count >= Constants.Network.MAX_PLAYERS) ||
                (!playerPrefab.TryGetComponent(out NetworkIdentity _)) ||
                (!playerPrefab.TryGetComponent(out PlayerStatus _)) ||
                (!playerPrefab.TryGetComponent(out PlayerRunAgent _)) ||
                (!ParticipantDirectory.TryAllocateBot(out ParticipantId participant)))
            {
                return false;
            }

            GameObject instance = Object.Instantiate(playerPrefab, position, rotation);
            NetworkIdentity identity = instance.GetComponent<NetworkIdentity>();
            PlayerStatus status = instance.GetComponent<PlayerStatus>();
            PlayerRunAgent agent = instance.GetComponent<PlayerRunAgent>();
            try
            {
                NetworkServer.Spawn(instance);
                if ((!identity.isServer) || (!status.ServerSetParticipantId(participant)) ||
                    (!agent.TryCommitRole(weapon, character)))
                {
                    NetworkServer.Destroy(instance);
                    return false;
                }

                bot = identity;
                return true;
            }
            catch
            {
                NetworkServer.Destroy(instance);
                throw;
            }
        }

        public static bool TryDespawn(ParticipantId participant)
        {
            if ((!NetworkServer.active) || (participant.Kind != ParticipantKind.Bot) ||
                (!ParticipantDirectory.TryGet(participant, out NetworkIdentity actor)) ||
                (actor.connectionToClient != null))
            {
                return false;
            }

            NetworkServer.Destroy(actor.gameObject);
            return true;
        }

        public static bool TryRestore(GameObject prefab, RunParticipantSnapshot snapshot,
            out NetworkIdentity bot)
        {
            bot = null;
            if ((!NetworkServer.active) || (prefab == null) || (snapshot == null) ||
                (snapshot.Participant.Kind != ParticipantKind.Bot) ||
                ParticipantDirectory.TryGet(snapshot.Participant, out _)) { return false; }
            GameObject instance = Object.Instantiate(prefab, snapshot.Position,
                Quaternion.Euler(0f, snapshot.YawDegrees, 0f));
            try
            {
                NetworkServer.Spawn(instance);
                if ((!instance.TryGetComponent(out PlayerStatus status)) ||
                    (!status.ServerSetParticipantId(snapshot.Participant)) ||
                    (!instance.TryGetComponent(out PlayerRunAgent agent)) ||
                    (!agent.RestoreRunState(snapshot)))
                { NetworkServer.Destroy(instance); return false; }
                bot = instance.GetComponent<NetworkIdentity>();
                return true;
            }
            catch { NetworkServer.Destroy(instance); throw; }
        }
    }
}
