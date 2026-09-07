// System
using System;

namespace Geumeum.Common
{
    /// <summary> 런 참가자의 영속 신원. 인증을 마친 <see cref="PlayerPlatformId"/>에서 결정한다 </summary>
    public readonly struct ParticipantId : IEquatable<ParticipantId>
    {
        /****************************************
        *                Fields
        ****************************************/

        public static readonly ParticipantId None = new ParticipantId(0UL);

        public readonly ulong Value;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsValid => (Value != 0UL);

        /****************************************
        *              Constructors
        ****************************************/

        public ParticipantId(ulong value)
        {
            Value = value;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 인증된 플랫폼 신원에서 만든다. 신원이 없으면 None이라 보관 키로 쓸 수 없다 </summary>
        public static ParticipantId FromPlatformId(PlayerPlatformId platformId)
        {
            return new ParticipantId(platformId.Value);
        }

        public bool Equals(ParticipantId other) => (Value == other.Value);

        public override bool Equals(object obj) => ((obj is ParticipantId other) && (Equals(other)));

        public override int GetHashCode() => Value.GetHashCode();

        public override string ToString() => Value.ToString();

        public static bool operator ==(ParticipantId left, ParticipantId right) => left.Equals(right);

        public static bool operator !=(ParticipantId left, ParticipantId right) => (!left.Equals(right));
    }
}
