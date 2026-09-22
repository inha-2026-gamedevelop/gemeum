// System
using System;

namespace Geumeum.Common
{
    /// <summary> 결과 화면이 읽는 참가자 한 명의 서버 확정 기여와 마지막 상태 </summary>
    [Serializable]
    public struct RunParticipantResultSnapshot
    {
        public ulong ParticipantIdValue;
        public ParticipantKind ParticipantKind;

        public bool          HasCharacter;
        public CharacterKind Character;

        public float DamageDealt;
        public float HealingDone;

        public int RescueCount;
        public int SwallowedRescueCount;
        public int DeathCount;

        public int   ParryCounterCount;
        public int   PrecisionCounterCount;
        public int   ResonanceCounterCount;
        public float CounterReductionTotal;

        public float RemainingHealth;

        public ParticipantId Participant => new ParticipantId(ParticipantKind, ParticipantIdValue);
    }
}
