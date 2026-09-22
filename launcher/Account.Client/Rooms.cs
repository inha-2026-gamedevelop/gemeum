namespace Geumeum.Accounts;

public sealed class AccountRoom
{
    public string RoomId { get; set; } = "";
    public string Name { get; set; } = "";
    public string BuildId { get; set; } = "";
    public string State { get; set; } = "";
    public string Hosting { get; set; } = "dedicated";
    public string Address { get; set; } = "";
    public int Port { get; set; }
    public string OwnerId { get; set; } = "";
    public string OwnerNickname { get; set; } = "";
    public List<RoomMember> Members { get; set; } = new();
    public int MaxPlayers { get; set; } = 3;
    public bool FillWithBots { get; set; }
    public string Occupancy => $"{Members.Count}/{MaxPlayers}명";
    public string StateLabel => State switch { "waiting" => Members.Count >= MaxPlayers ? "정원 마감" : "파티 모집 중", "hosting" or "queued" or "starting" => "출발 준비 중", "ready" => "게임 접속 중", "running" => "게임 진행 중", _ => "종료" };
    public override string ToString() => $"{Name} · {State switch { "hosting" or "queued" or "starting" => "준비 중", "ready" => "참가 가능", "running" => "플레이 중 · 재접속만 가능", _ => "종료" }}";
}

public sealed class RoomMember
{
    public string AccountId { get; set; } = "";
    public string Nickname { get; set; } = "";
    public bool IsOwner { get; set; }
    public bool Connected { get; set; }
    public int? Preference { get; set; }
    public bool Ready { get; set; }
    public string WeaponLabel => Preference switch { 0 => "검 · 차사", 1 => "활 · 신궁", 2 => "방울 · 무녀", _ => "무기 미선택" };
    public string Display => $"{Nickname}{(IsOwner ? " · 방장" : "")} · {(Connected ? "접속 중" : "입장 대기")}";
}

public sealed class RoomList
{
    public List<AccountRoom> Rooms { get; set; } = new();
    public string BuildId { get; set; } = "";
}
