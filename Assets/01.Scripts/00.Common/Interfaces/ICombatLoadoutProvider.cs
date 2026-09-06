namespace Geumeum.Common
{
    /// <summary>
    /// 장비와 특성에서 유효 전투 수치를 만드는 계약. Systems가 구현하고 Combat이 읽는다.
    ///
    /// 클라이언트가 보낸 공격력과 옵션을 그대로 쓰지 않는다.
    /// 서버가 자기 데이터로 만든 값만 피해 계산에 들어간다
    /// </summary>
    public interface ICombatLoadoutProvider
    {
        /// <summary> 장비, 특성, 레벨 중 하나라도 바뀌면 올라간다. 이 값이 같으면 다시 만들지 않는다 </summary>
        uint Revision { get; }

        /// <summary> 이 참가자의 현재 수치를 만든다. 만들 수 없으면 false이고 호출부는 공격을 거부한다 </summary>
        bool TryBuildLoadout(ParticipantId participant, out CombatStatsSnapshot snapshot);
    }
}
