namespace Geumeum.Common
{
    /// <summary> 인벤토리 한 칸이 무엇을 담고 있는지. 소모품의 실제 종류는 ShopItemKind가 구분한다 </summary>
    public enum InventoryItemKind
    {
        None       = 0,
        Equipment  = 1, // 스택 불가. 한 칸에 하나
        Consumable = 2, // 물약과 권. ShopSO의 CarryLimit까지 한 칸에 쌓는다
    }
}
