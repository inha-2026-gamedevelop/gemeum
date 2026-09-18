// Project
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Systems
{
    /// <summary> 보스 트라이 편성 판정. 서버와 편성 화면이 같은 규칙을 본다 </summary>
    public static class BossTrialRules
    {
        /// <summary> 한 사람이 찍을 수 있는 특성 노드 수의 상한. 특성 포인트 규칙이 실제 한도를 다시 본다 </summary>
        public const int MAX_TALENT_NODES = 16;

        /// <summary> 편성 비용과 거절 사유를 한 번에 계산한다. 성공일 때만 cost가 의미를 갖는다 </summary>
        public static BossTrialLoadoutResult Validate(BossTrialLoadoutSO config,
            in BossTrialLoadout loadout, out int cost)
        {
            cost = 0;

            if (config == null)
            {
                return BossTrialLoadoutResult.DataMissing;
            }

            if ((loadout.StartLevel < 0) || (loadout.StartLevel > Constants.Combat.MAX_LEVEL))
            {
                return BossTrialLoadoutResult.LevelOutOfRange;
            }

            if ((loadout.PotionCount < 0) || (loadout.PotionCount > config.MaxPotions))
            {
                return BossTrialLoadoutResult.PotionLimit;
            }

            if ((loadout.TalentNodeIds != null) && (loadout.TalentNodeIds.Length > MAX_TALENT_NODES))
            {
                return BossTrialLoadoutResult.TooManyTalents;
            }

            if (loadout.EquipmentTier >= 0)
            {
                if (!config.TryGetEquipmentCost(loadout.EquipmentTier, out int equipmentCost))
                {
                    return BossTrialLoadoutResult.TierOutOfRange;
                }

                cost += equipmentCost;

                BossTrialLoadoutResult custom = ValidateCustomEquipment(in loadout);
                if (custom != BossTrialLoadoutResult.Success)
                {
                    cost = 0;
                    return custom;
                }
            }

            cost += (config.PotionCost * loadout.PotionCount);

            if (cost > config.LoadoutPoints)
            {
                cost = 0;
                return BossTrialLoadoutResult.OverBudget;
            }

            return BossTrialLoadoutResult.Success;
        }

        /// <summary> 아무것도 고르지 않은 편성. 제출하지 않은 사람에게 서버가 쓴다 </summary>
        public static BossTrialLoadout CreateEmpty()
        {
            return new BossTrialLoadout
            {
                StartLevel     = 0,
                OverrideWeapon = false,
                EquipmentTier  = -1,
                PotionCount   = 0,
                TalentNodeIds = null,
            };
        }

        /// <summary> 상세 지정한 공격력과 옵션이 장비 데이터 범위 안인지 본다 </summary>
        public static BossTrialLoadoutResult ValidateCustomEquipment(in BossTrialLoadout loadout)
        {
            if (!loadout.UseCustomEquipment)
            {
                return BossTrialLoadoutResult.Success;
            }

            EquipmentSO equipment = (GameDB.IsInitialized) ? GameDB.Equipment : null;
            if (equipment == null)
            {
                return BossTrialLoadoutResult.DataMissing;
            }

            if (!equipment.TryGetGrade(loadout.EquipmentGrade, out EquipmentSO.GradeEntry grade))
            {
                return BossTrialLoadoutResult.TierOutOfRange;
            }

            // 0 이하는 "서버가 굴린다"는 뜻이라 범위 검사를 건너뛴다
            if ((loadout.AttackPower > 0f) &&
                ((loadout.AttackPower < grade.AttackMin) || (loadout.AttackPower > grade.AttackMax)))
            {
                return BossTrialLoadoutResult.OptionInvalid;
            }

            if (!IsOptionAllowed(equipment, loadout.Weapon, loadout.Option0Kind, loadout.Option0Value))
            {
                return BossTrialLoadoutResult.OptionInvalid;
            }

            if (!IsOptionAllowed(equipment, loadout.Weapon, loadout.Option1Kind, loadout.Option1Value))
            {
                return BossTrialLoadoutResult.OptionInvalid;
            }

            return BossTrialLoadoutResult.Success;
        }

        /// <summary>
        /// 쓸 옵션 값을 정한다. 0 이하로 비워 두면 데이터 범위의 중간값을 쓴다 -
        /// 값 없이 종류만 고른 옵션이 0으로 붙으면 옵션이 붙은 것도 안 붙은 것도 아닌 상태가 된다
        /// </summary>
        public static float ResolveOptionValue(EquipmentSO equipment, WeaponKind weapon,
            EquipmentOptionKind kind, float requested)
        {
            if ((kind == EquipmentOptionKind.None) || (requested > 0f) || (equipment == null))
            {
                return requested;
            }

            if ((!TryFindOption(equipment.CommonOptions, kind, out EquipmentSO.OptionEntry entry)) &&
                ((!equipment.TryGetWeaponOptions(weapon, out EquipmentSO.OptionEntry[] options)) ||
                 (!TryFindOption(options, kind, out entry))))
            {
                return requested;
            }

            return ((entry.ValueMin + entry.ValueMax) * 0.5f);
        }

        /// <summary> 이 무기가 가질 수 있는 옵션이고 값이 데이터 범위 안인가. None은 빈 칸이라 허용한다 </summary>
        public static bool IsOptionAllowed(EquipmentSO equipment, WeaponKind weapon,
            EquipmentOptionKind kind, float value)
        {
            if (kind == EquipmentOptionKind.None)
            {
                return true;
            }

            if (equipment == null)
            {
                return false;
            }

            if (TryFindOption(equipment.CommonOptions, kind, out EquipmentSO.OptionEntry entry))
            {
                return IsInRange(value, entry);
            }

            if ((equipment.TryGetWeaponOptions(weapon, out EquipmentSO.OptionEntry[] options)) &&
                (TryFindOption(options, kind, out entry)))
            {
                return IsInRange(value, entry);
            }

            return false;
        }

        private static bool TryFindOption(EquipmentSO.OptionEntry[] options, EquipmentOptionKind kind,
            out EquipmentSO.OptionEntry found)
        {
            found = default;

            if (options == null)
            {
                return false;
            }

            for (int i = 0; i < options.Length; ++i)
            {
                if (options[i].Kind != kind)
                {
                    continue;
                }

                found = options[i];
                return true;
            }

            return false;
        }

        private static bool IsInRange(float value, in EquipmentSO.OptionEntry entry)
        {
            // 0 이하는 서버가 굴리라는 뜻이다
            return ((value <= 0f) || ((value >= entry.ValueMin) && (value <= entry.ValueMax)));
        }
    }
}
