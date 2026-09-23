using System;

namespace Geumeum.Network
{
    /// <summary>계정 API의 방 목록 응답. 자격 증명은 포함하지 않는다.</summary>
    [Serializable]
    public sealed class AccountRoomList
    {
        public AccountRoom[] rooms;
        public string build_id;
    }

    /// <summary>계정 API의 방 상태. 서버 응답의 snake_case 필드명을 유지한다.</summary>
    [Serializable]
    public sealed class AccountRoom
    {
        public string room_id;
        public string name;
        public string build_id;
        public string state;
        public string hosting;
        public string address;
        public int port;
        public string owner_id;
        public string owner_nickname;
        public AccountRoomMember[] members;
        public int max_players;
        public bool fill_with_bots;
    }

    [Serializable]
    public sealed class AccountRoomMember
    {
        public string account_id;
        public string nickname;
        public bool is_owner;
        public bool connected;
        public int? preference;
        public bool ready;
    }

    /// <summary>UI에 표시해도 되는 고정된 방 작업 오류만 전달한다.</summary>
    public sealed class AccountRoomException : Exception
    {
        public string Code { get; }

        public AccountRoomException(string code, string message) : base(message)
        {
            Code = code;
        }
    }
}
