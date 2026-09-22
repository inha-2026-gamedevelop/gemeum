using Geumeum.Common;
using Geumeum.Network;
using Mirror;

namespace Geumeum.Systems
{
    public partial class InventoryController
    {
        private uint _botReadRequest;
        private double _nextBotRead;
        private BotInventorySnapshot _observedBotInventory;

        public void RequestBotInventory(uint botNetId, uint runId, uint sceneRevision)
        {
            if (!isOwned || _botReadRequest == uint.MaxValue) { return; }
            // 선택 대상을 바꾸면 이전 내용을 지우고, 같은 대상 갱신 중에는 확정된 내용을 유지한다
            if (_observedBotInventory.BotNetId != botNetId) { _observedBotInventory = default; }
            CmdReadBotInventory(++_botReadRequest, botNetId, runId, sceneRevision);
        }

        public bool TryGetBotInventory(uint botNetId, uint runId, uint sceneRevision, out BotInventorySnapshot snapshot)
        {
            snapshot = _observedBotInventory;
            return isOwned && snapshot.Available && snapshot.BotNetId == botNetId &&
                snapshot.RunId == runId && snapshot.SceneRevision == sceneRevision;
        }

        [Command]
        private void CmdReadBotInventory(uint requestId, uint botNetId, uint runId, uint sceneRevision)
        {
            if (connectionToClient == null || NetworkTime.time < _nextBotRead) { return; }
            _nextBotRead = NetworkTime.time + 0.2d;
            ServerReadBotInventory(botNetId, runId, sceneRevision, out BotInventorySnapshot snapshot);
            TargetBotInventory(connectionToClient, requestId, snapshot);
        }

        [TargetRpc]
        private void TargetBotInventory(NetworkConnectionToClient connection, uint requestId, BotInventorySnapshot snapshot)
        {
            if (requestId != _botReadRequest) { return; }
            _observedBotInventory = snapshot;
        }
    }
}
