// System
using System;

namespace Geumeum.Common
{
    /// <summary>
    /// 레벨, 장비, 특성을 모두 반영한 유효 전투 수치.
    ///
    /// 공격을 수락하는 순간 이 값을 떠 두고 그 공격은 끝까지 같은 값으로 계산한다.
    /// 그러지 않으면 이미 날아간 화살의 피해가 도중에 바뀐 장비를 따라간다.
    /// Revision이 바뀔 때만 다시 만들고 매 명중마다 SO와 인벤토리를 다시 뒤지지 않는다
    /// </summary>
    [Serializable]
    public struct CombatStatsSnapshot : IEquatable<CombatStatsSnapshot>
    {
        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 레벨, 장비, 특성 중 하나라도 바뀌면 올라간다. 0은 아직 만들지 않은 값이다 </summary>
        public uint Revision;

        public float MaxHealth;
        public float MeleeAttackPower;
        public float RangedAttackPower;

        // TODO(민성): 특수옵션에서 오는 배수와 치명 관련 값은 Systems가 장비 계산을 확정할 때
        // 여기에 필드로 추가한다. 지금은 옵션 없는 기본 계산만 담는다

        /****************************************
        *              Properties
        ****************************************/

        public bool IsValid => (Revision != 0u);

        /****************************************
        *                Methods
        ****************************************/

        public bool Equals(CombatStatsSnapshot other)
        {
            return ((Revision == other.Revision) &&
                    (MaxHealth.Equals(other.MaxHealth)) &&
                    (MeleeAttackPower.Equals(other.MeleeAttackPower)) &&
                    (RangedAttackPower.Equals(other.RangedAttackPower)));
        }

        public override bool Equals(object obj) => ((obj is CombatStatsSnapshot other) && (Equals(other)));

        public override int GetHashCode() => Revision.GetHashCode();
    }
}
