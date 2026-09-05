namespace Geumeum.Common
{
    /// <summary>
    /// 플레이어 캐릭터. 로비에서 중복 없이 고르고, 무기는 이 값으로 정해져 런 진입 시 자동 지급된다.
    /// 직렬화된 값이므로 숫자를 재배열하지 않는다 - WeaponKind와 값이 1:1로 짝을 이룬다
    /// </summary>
    public enum CharacterKind
    {
        Chasa   = 0, // 차사, 검
        Singung = 1, // 신궁, 활
        Munyeo  = 2, // 무녀, 방울
    }
}
