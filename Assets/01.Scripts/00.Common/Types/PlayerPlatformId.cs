// System
using System;

namespace Geumeum.Common
{
    /// <summary>
    /// 재접속에도 유지되는 플레이어 신원. Steam 빌드에서는 SteamID64, 개발 KCP 경로에서는 실행 인자로 준 대체 값이다
    /// connectionId는 재접속마다 새로 발급되어 같은 사람을 알아볼 수 없으므로 슬롯 키로 이 값을 쓴다
    /// </summary>
    public readonly struct PlayerPlatformId : IEquatable<PlayerPlatformId>
    {
        /****************************************
        *                Fields
        ****************************************/

        public static readonly PlayerPlatformId None = new PlayerPlatformId(0UL);

        public readonly ulong Value;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsValid => (Value != 0UL);

        /****************************************
        *              Constructors
        ****************************************/

        public PlayerPlatformId(ulong value)
        {
            Value = value;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 문자열에서 읽는다. Steam은 conn.address가 SteamID64 문자열로 온다 </summary>
        public static bool TryParse(string raw, out PlayerPlatformId id)
        {
            if ((!string.IsNullOrWhiteSpace(raw)) && (ulong.TryParse(raw.Trim(), out ulong value)) && (value != 0UL))
            {
                id = new PlayerPlatformId(value);
                return true;
            }

            id = None;
            return false;
        }

        public bool Equals(PlayerPlatformId other) => (Value == other.Value);

        public override bool Equals(object obj) => ((obj is PlayerPlatformId other) && (Equals(other)));

        public override int GetHashCode() => Value.GetHashCode();

        public override string ToString() => Value.ToString();

        public static bool operator ==(PlayerPlatformId left, PlayerPlatformId right) => left.Equals(right);

        public static bool operator !=(PlayerPlatformId left, PlayerPlatformId right) => (!left.Equals(right));
    }
}
