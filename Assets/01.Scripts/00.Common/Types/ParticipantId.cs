// System
using System;

namespace Geumeum.Common
{
    /// <summary> 참가자 종류와 신원. 사람은 인증된 플랫폼 ID, 봇은 서버 발급 번호를 쓴다 </summary>
    public readonly struct ParticipantId : IEquatable<ParticipantId>, IComparable<ParticipantId>
    {
        /****************************************
        *                Fields
        ****************************************/

        public static readonly ParticipantId None = new ParticipantId(0UL);

        public readonly ulong Value;
        public readonly ParticipantKind Kind;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsValid => ((Value != 0UL) &&
            ((Kind == ParticipantKind.Human) || (Kind == ParticipantKind.Bot)));

        /****************************************
        *              Constructors
        ****************************************/

        public ParticipantId(ulong value) : this(ParticipantKind.Human, value)
        {
        }

        public ParticipantId(ParticipantKind kind, ulong value)
        {
            Value = value;
            Kind = kind;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 인증된 플랫폼 신원에서 만든다. 신원이 없으면 None이라 보관 키로 쓸 수 없다 </summary>
        public static ParticipantId FromPlatformId(PlayerPlatformId platformId)
        {
            return new ParticipantId(platformId.Value);
        }

        public bool Equals(ParticipantId other) => ((Kind == other.Kind) && (Value == other.Value));

        public int CompareTo(ParticipantId other)
        {
            int kind = Kind.CompareTo(other.Kind);
            return (kind != 0) ? kind : Value.CompareTo(other.Value);
        }

        public override bool Equals(object obj) => ((obj is ParticipantId other) && (Equals(other)));

        public override int GetHashCode() => (Value.GetHashCode() ^ ((int) Kind * 397));

        public override string ToString() => $"{Kind}:{Value}";

        public static bool operator ==(ParticipantId left, ParticipantId right) => left.Equals(right);

        public static bool operator !=(ParticipantId left, ParticipantId right) => (!left.Equals(right));
    }
}
