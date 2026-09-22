using Geumeum.Combat;
using Geumeum.Common;
using Geumeum.Network;
using Mirror;

namespace Geumeum.Systems
{
    public partial class InventoryController
    {
        [Server]
        internal bool ServerReadBotInventory(uint botNetId, uint runId, uint sceneRevision, out BotInventorySnapshot snapshot)
        {
            snapshot = new BotInventorySnapshot { BotNetId = botNetId, RunId = runId, SceneRevision = sceneRevision };
            var run = RunManager.ServerInstance;
            var director = RunSceneDirector.ServerInstance;
            if (run == null || director == null || run.RunId.Value != runId || director.Revision.Value != sceneRevision ||
                RunSceneDirector.IsServerTransitioning || !NetworkServer.spawned.TryGetValue(botNetId, out var target)) { return false; }
            PlayerStatus requester = GetComponent<PlayerStatus>();
            PlayerStatus bot = target.GetComponent<PlayerStatus>();
            if (requester == null || bot == null || requester.Participant.Kind != ParticipantKind.Human ||
                bot.Participant.Kind != ParticipantKind.Bot ||
                !ParticipantDirectory.Actors.TryGetValue(requester.Participant, out var owner) || owner != netIdentity ||
                !ParticipantDirectory.Actors.TryGetValue(bot.Participant, out var registeredBot) || registeredBot != target)
            { return false; }
            InventoryController inventory = target.GetComponent<InventoryController>();
            if (inventory == null) { return false; }
            snapshot.Slots = new InventorySlotSnapshot[InventorySnapshot.SLOT_COUNT];
            if (!inventory.ServerCopySlots(snapshot.Slots)) { return false; }
            snapshot.Equipped = inventory.EquippedWeapon;
            snapshot.InventoryRevision = inventory.Revision;
            snapshot.Available = true;
            return true;
        }

    }
}
