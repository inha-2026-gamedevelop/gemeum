using System.Collections.Generic;
using Geumeum.Common;
using Geumeum.Combat;
using Mirror;
using UnityEngine;

namespace Geumeum.Network
{
    /// <summary> 사람 역할을 우선 배정한 뒤 빈 역할을 서버 참가자로 채운다 </summary>
    public static class BotParty
    {
        public static bool TryFill()
        {
            LobbyRoster roster = LobbyRoster.ServerInstance;
            NetworkManager manager = NetworkManager.singleton;
            if ((!NetworkServer.active) || (roster == null) || (manager == null) ||
                (roster.SlotCount < 1) || (roster.SlotCount > Constants.Network.MAX_PLAYERS)) { return false; }
            var used = new HashSet<WeaponKind>();
            for (int i = 0; i < roster.SlotCount; ++i)
            {
                LobbySlot slot = roster.GetSlot(i);
                if ((!slot.HasWeapon) || (!used.Add(slot.Weapon))) { return false; }
            }
            var created = new List<ParticipantId>();
            try
            {
                foreach (WeaponKind weapon in new[] { WeaponKind.Sword, WeaponKind.Bow, WeaponKind.Bell })
                {
                    if (used.Contains(weapon)) { continue; }
                    Transform start = manager.GetStartPosition();
                    Vector3 position = start == null ? Vector3.zero : start.position;
                    CharacterKind character = weapon == WeaponKind.Sword ? CharacterKind.Chasa :
                        weapon == WeaponKind.Bow ? CharacterKind.Singung : CharacterKind.Munyeo;
                    if (!PlayerBotSpawner.TrySpawn(manager.playerPrefab, position, Quaternion.identity,
                        weapon, character, out NetworkIdentity bot))
                    {
                        foreach (ParticipantId id in created) { PlayerBotSpawner.TryDespawn(id); }
                        return false;
                    }
                    created.Add(bot.GetComponent<PlayerStatus>().Participant);
                }
                return true;
            }
            catch (System.Exception exception)
            {
                foreach (ParticipantId id in created) { PlayerBotSpawner.TryDespawn(id); }
                Debug.LogException(exception);
                return false;
            }
        }

        public static void Clear()
        {
            var ids = new List<ParticipantId>();
            foreach (var pair in ParticipantDirectory.Actors)
            { if (pair.Key.Kind == ParticipantKind.Bot) { ids.Add(pair.Key); } }
            foreach (ParticipantId id in ids) { PlayerBotSpawner.TryDespawn(id); }
        }
    }
}
