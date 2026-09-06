// System
using System;

namespace Geumeum.Common
{
    /// <summary>
    /// 지금 들고 있는 무기의 확정 값. 장비를 만든 Systems와 피해를 내는 Combat이 이것 하나로 이야기한다.
    ///
    /// ScriptableObject, GameObject, 배열을 넣지 않는다. Mirror가 그대로 실을 수 있어야 하고,
    /// 참조를 넣으면 씬이 바뀔 때 죽은 참조가 남는다
    /// </summary>
    [Serializable]
    public struct WeaponCombatSnapshot : IEquatable<WeaponCombatSnapshot>
    {
        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 장비 인스턴스 ID. 0은 런 시작에 배분한 기본 무기다 </summary>
        public uint InstanceId;

        public WeaponKind WeaponKind;
        public ItemGrade  Grade;

        /// <summary> 등급 범위에서 서버가 굴린 공격력. 기본 수치를 대체한다 </summary>
        public float AttackPower;

        // 옵션은 최대 두 개다. 배열을 쓰지 않는 이유는 위 요약과 같다
        public EquipmentOptionKind Option0Kind;
        public float               Option0Value;
        public EquipmentOptionKind Option1Kind;
        public float               Option1Value;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 장비에서 온 무기인지. 시작 무기는 인스턴스가 없다 </summary>
        public bool HasEquipment => (InstanceId != 0u);

        /****************************************
        *                Methods
        ****************************************/

        public bool Equals(WeaponCombatSnapshot other)
        {
            return ((InstanceId == other.InstanceId) &&
                    (WeaponKind == other.WeaponKind) &&
                    (Grade == other.Grade) &&
                    (AttackPower.Equals(other.AttackPower)) &&
                    (Option0Kind == other.Option0Kind) &&
                    (Option0Value.Equals(other.Option0Value)) &&
                    (Option1Kind == other.Option1Kind) &&
                    (Option1Value.Equals(other.Option1Value)));
        }

        public override bool Equals(object obj) => ((obj is WeaponCombatSnapshot other) && (Equals(other)));

        public override int GetHashCode()
        {
            return (InstanceId.GetHashCode() ^ ((int) WeaponKind) ^ ((int) Grade << 8) ^ AttackPower.GetHashCode());
        }
    }
}
