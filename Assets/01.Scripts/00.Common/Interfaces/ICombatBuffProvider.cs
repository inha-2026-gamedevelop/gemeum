// System
using System;

namespace Geumeum.Common
{
    /// <summary> 한 참가자에게 현재 적용 중인 서버 전투 버프를 공급한다 </summary>
    public interface ICombatBuffProvider
    {
        uint Revision { get; }

        event Action<uint> RevisionChanged;

        bool TryGetDamageMultiplier(ParticipantId participant, out float multiplier);
    }
}
