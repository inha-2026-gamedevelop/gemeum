namespace Geumeum.Common
{
    /// <summary> 서버가 확인한 같은 파티 봇의 읽기 전용 배낭. 사람 배낭은 포함하지 않는다 </summary>
    public struct BotInventorySnapshot
    {
        public bool Available;
        public uint RunId;
        public uint SceneRevision;
        public uint BotNetId;
        public uint InventoryRevision;
        public WeaponCombatSnapshot Equipped;
        public InventorySlotSnapshot[] Slots;
    }
}
