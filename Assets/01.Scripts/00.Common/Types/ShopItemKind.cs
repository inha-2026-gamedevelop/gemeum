namespace Geumeum.Common
{
    /// <summary> 객주 상품 종류 </summary>
    public enum ShopItemKind
    {
        None = 0,

        HealthPotion  = 1, // 회복 물약
        SpiritPotion  = 2, // 신력 물약
        RerollTicket  = 3, // 스탯 재조정권, 같은 설계도로 재생성
        UpgradeTicket = 4, // 등급 상승권, 상위 등급으로 교체 후 재생성
    }
}
