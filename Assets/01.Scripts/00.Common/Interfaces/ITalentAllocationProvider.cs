// System
using System;

namespace Geumeum.Common
{
    /// <summary> Systems 전투 수치 공급자가 현재 특성 배분을 읽는 창구 </summary>
    public interface ITalentAllocationProvider
    {
        uint Revision { get; }

        event Action<uint> RevisionChanged;

        bool TryGetAllocation(ParticipantId participant,
            out TalentAllocationSnapshot allocation);

        bool ServerCanResetForWeapon(WeaponKind weapon);

        void ServerApplyWeaponReset(WeaponKind weapon);

        void ServerPublishAllocation();
    }
}
