// System
using System;
using System.Text;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Systems;

namespace Geumeum.UI
{
    /// <summary> 서버 결과와 게임 데이터를 사용자용 한국어로 바꾼다 </summary>
    public static class FunctionalUiText
    {
        public static string GetWeaponName(WeaponKind weapon)
        {
            switch (weapon)
            {
                case WeaponKind.Sword:
                    return "검";
                case WeaponKind.Bow:
                    return "활";
                case WeaponKind.Bell:
                    return "방울";
                default:
                    return "무기";
            }
        }

        public static string GetCharacterName(CharacterKind character)
        {
            switch (character)
            {
                case CharacterKind.Chasa:
                    return "차사";
                case CharacterKind.Singung:
                    return "신궁";
                case CharacterKind.Munyeo:
                    return "무녀";
                default:
                    return "참가자";
            }
        }

        /// <summary> 든 무기로 부르는 역할 이름. 무기를 바꾸면 특성 트리도 그 역할 것으로 바뀐다 </summary>
        public static string GetWeaponRoleName(WeaponKind weapon)
        {
            switch (weapon)
            {
                case WeaponKind.Sword:
                    return GetCharacterName(CharacterKind.Chasa);
                case WeaponKind.Bow:
                    return GetCharacterName(CharacterKind.Singung);
                case WeaponKind.Bell:
                    return GetCharacterName(CharacterKind.Munyeo);
                default:
                    return "참가자";
            }
        }

        public static string GetGradeName(ItemGrade grade)
        {
            switch (grade)
            {
                case ItemGrade.Common:
                    return "일반";
                case ItemGrade.Talisman:
                    return "부적";
                case ItemGrade.Spiritual:
                    return "영험";
                case ItemGrade.Divine:
                    return "신물";
                default:
                    return "등급 없음";
            }
        }

        public static string GetShopItemName(ShopItemKind item)
        {
            switch (item)
            {
                case ShopItemKind.HealthPotion:
                    return "체력 물약";
                case ShopItemKind.SpiritPotion:
                    return "신력 물약";
                case ShopItemKind.RerollTicket:
                    return "재조정권";
                case ShopItemKind.UpgradeTicket:
                    return "승급권";
                default:
                    return "알 수 없는 물품";
            }
        }

        public static string DescribeWeapon(in WeaponCombatSnapshot weapon)
        {
            if (!weapon.HasEquipment)
            {
                return $"기본 {GetWeaponName(weapon.WeaponKind)}";
            }

            string first = DescribeOption(weapon.Option0Kind, weapon.Option0Value);
            string second = DescribeOption(weapon.Option1Kind, weapon.Option1Value);
            string options = string.IsNullOrEmpty(first) ? string.Empty : $"\n{first}";
            if (!string.IsNullOrEmpty(second))
            {
                options += $"\n{second}";
            }

            return $"{GetGradeName(weapon.Grade)} {GetWeaponName(weapon.WeaponKind)}" +
                $"\n공격력 {weapon.AttackPower:0.#}{options}";
        }

        public static string DescribeSlot(in InventorySlotSnapshot slot)
        {
            switch (slot.Kind)
            {
                case InventoryItemKind.None:
                    return "빈 칸";
                case InventoryItemKind.Equipment:
                    return $"{GetGradeName(slot.Equipment.Grade)} " +
                        GetWeaponName(slot.Equipment.WeaponKind);
                case InventoryItemKind.Consumable:
                    return $"{GetShopItemName(slot.ItemId)}\n{slot.Count}개";
                default:
                    return "확인할 수 없음";
            }
        }

        public static string DescribeTalentValue(TalentEffectKind effect, float value)
        {
            switch (effect)
            {
                case TalentEffectKind.CounterRecoveryReduction:
                case TalentEffectKind.ThirdHitSkillCooldown:
                case TalentEffectKind.MissSkillRecoveryReduction:
                case TalentEffectKind.WedgeFirstHitCooldown:
                case TalentEffectKind.RollShotRecoveryReduction:
                    return $"-{value:0.##}초";
                case TalentEffectKind.CounterThirdCombo:
                case TalentEffectKind.SkillSecondCombo:
                case TalentEffectKind.SideStepSkill:
                    return "해금";
                case TalentEffectKind.PrecisionFollowThrough:
                    return $"공격력 {value * 100f:0.#}%";
                case TalentEffectKind.CounterDurationBonus:
                case TalentEffectKind.ComboWindowBonus:
                case TalentEffectKind.WedgeDurationBonus:
                case TalentEffectKind.BowComboWindowBonus:
                case TalentEffectKind.ParryWindowSeconds:
                case TalentEffectKind.GuidanceDurationSeconds:
                case TalentEffectKind.EchoDurationSeconds:
                case TalentEffectKind.FieldEchoExtendSeconds:
                case TalentEffectKind.FieldTailSeconds:
                    return $"+{value:0.##}초";
                case TalentEffectKind.FieldRadiusBonus:
                case TalentEffectKind.FieldWideningRadiusBonus:
                    return $"+{value:0.##}m";
                case TalentEffectKind.BasicStaminaReduction:
                case TalentEffectKind.RollBasicStaminaReduction:
                case TalentEffectKind.BowBasicStaminaReduction:
                case TalentEffectKind.BowSkillStaminaReduction:
                case TalentEffectKind.PrecisionStaminaReduction:
                case TalentEffectKind.BowRollBasicReduction:
                case TalentEffectKind.WedgeRollReduction:
                case TalentEffectKind.EchoRecallSpiritCost:
                case TalentEffectKind.HealSpiritCost:
                case TalentEffectKind.SkillSpiritCost:
                case TalentEffectKind.FieldHealEchoDiscount:
                    return $"-{value:0.##}";
                case TalentEffectKind.ParryStaminaBonus:
                case TalentEffectKind.PrecisionPierceBonus:
                case TalentEffectKind.ParryGaugeGain:
                case TalentEffectKind.CriticalMultiplierMin:
                case TalentEffectKind.PierceCount:
                case TalentEffectKind.BurstShotCount:
                case TalentEffectKind.BellFirstHitSpiritGain:
                    return $"+{value:0.##}";
                default:
                    return $"{(value >= 0f ? "+" : string.Empty)}{value:0.#}%";
            }
        }

        /// <summary> 특성 노드 이름. SO의 DisplayName이 비면 화면 위치로 부른다 </summary>
        public static string GetTalentNodeName(in TalentTreeSO.NodeEntry node)
        {
            return string.IsNullOrWhiteSpace(node.DisplayName)
                ? $"{node.RowIndex + 1}단계 특성" : node.DisplayName;
        }

        /// <summary>
        /// 효과 종류의 한국어 이름
        ///
        /// 수치는 전부 SO(Value)에서 읽고 여기서는 이름만 맡는다. SO에 설명 필드가 없어
        /// 이름만 여기 두며, 문구는 CombatStatCalculator와 무기 런타임의 실제 적용 지점을
        /// 확인해 적었다. 은퇴한 효과는 어떤 무기에도 붙지 않으므로 이름을 두지 않는다
        /// </summary>
        public static string GetTalentEffectName(TalentEffectKind effect)
        {
            switch (effect)
            {
                case TalentEffectKind.CriticalChancePercent:      return "치명타 확률";
                case TalentEffectKind.AttackSpeedPercent:         return "공격 속도";
                case TalentEffectKind.StaminaCostPercent:         return "기력 소모";

                case TalentEffectKind.ParryGaugeGain:             return "패링 게이지 획득";
                case TalentEffectKind.ParryWindowSeconds:         return "패링 판정 시간";
                case TalentEffectKind.CriticalMultiplierMin:      return "최소 치명타 배율";
                case TalentEffectKind.ThirdComboDamagePercent:    return "3타 공격력";

                case TalentEffectKind.ArrowSpeedPercent:          return "화살 속도";
                case TalentEffectKind.GuidanceDurationSeconds:    return "유도 지속 시간";
                case TalentEffectKind.PierceCount:                return "관통 수";
                case TalentEffectKind.BurstShotCount:             return "연사 수";
                case TalentEffectKind.ArrowHitRadiusPercent:      return "화살 적중 반경";
                case TalentEffectKind.RangedDamagePercent:        return "원거리 공격력";

                // 검 트리
                case TalentEffectKind.BasicStaminaReduction:      return "기본 공격 기력 소모";
                case TalentEffectKind.CounterDurationBonus:       return "반격 유지 시간";
                case TalentEffectKind.ParryStaminaBonus:          return "패링 성공 시 기력";
                case TalentEffectKind.CounterRecoveryReduction:   return "반격 후 경직";
                case TalentEffectKind.CounterThirdCombo:          return "반격에서 3타 연계";
                case TalentEffectKind.SecondComboDamagePercent:   return "2타 공격력";
                case TalentEffectKind.ComboWindowBonus:           return "연계 입력 시간";
                case TalentEffectKind.ThirdHitSkillCooldown:      return "3타 적중 시 스킬 대기";
                case TalentEffectKind.SkillSecondCombo:           return "스킬에서 2타 연계";
                case TalentEffectKind.RollBasicStaminaReduction:  return "구르기 직후 기본 공격 기력";
                case TalentEffectKind.MissSkillRecoveryReduction: return "빗나간 스킬 후 경직";
                case TalentEffectKind.RollSkillDamagePercent:     return "구르기 직후 스킬 공격력";
                case TalentEffectKind.SideStepSkill:              return "옆으로 파고드는 스킬";

                // 활 트리
                case TalentEffectKind.BowBasicStaminaReduction:   return "기본 사격 기력 소모";
                case TalentEffectKind.BowSkillStaminaReduction:   return "활 스킬 기력 소모";
                case TalentEffectKind.WedgeDurationBonus:         return "쐐기 유지 시간";
                case TalentEffectKind.WedgeFirstHitCooldown:      return "쐐기 첫 적중 시 스킬 대기";
                case TalentEffectKind.WedgeNormalBurstPercent:    return "쐐기 대상 일반 사격 공격력";
                case TalentEffectKind.PrecisionStaminaReduction:  return "정밀 사격 기력 소모";
                case TalentEffectKind.WedgePrecisionBurstPercent: return "쐐기 대상 정밀 사격 공격력";
                case TalentEffectKind.PrecisionPierceBonus:       return "정밀 사격 관통";
                case TalentEffectKind.PrecisionFollowThrough:     return "정밀 사격 추가타";
                case TalentEffectKind.BowRollBasicReduction:      return "구르기 직후 기본 사격 기력";
                case TalentEffectKind.BowComboWindowBonus:        return "사격 연계 입력 시간";
                case TalentEffectKind.WedgeRollReduction:         return "쐐기 설치 직후 구르기 기력";
                case TalentEffectKind.RollShotRecoveryReduction:  return "구르기 사격 후 경직";

                // 방울 트리
                case TalentEffectKind.HealAmountPercent:          return "치유량";
                case TalentEffectKind.SpiritCostPercent:          return "신력 소모";
                case TalentEffectKind.BellFirstHitSpiritGain:     return "첫 적중 신력 회복";
                case TalentEffectKind.EchoRecallSpiritCost:       return "잔향 회수 신력 소모";
                case TalentEffectKind.EchoDurationSeconds:        return "잔향 유지 시간";
                case TalentEffectKind.EchoFieldDamagePercent:     return "잔향 공명진 공격력";
                case TalentEffectKind.FieldEchoExtendSeconds:     return "공명진 지속 시간 연장";
                case TalentEffectKind.HealSpiritCost:             return "치유 신력 소모";
                case TalentEffectKind.FieldHealEchoDiscount:      return "공명진 치유 후 잔향 회수 신력";
                case TalentEffectKind.FieldHealAfterglowPercent:  return "공명진 치유 잔광 추가 치유";
                case TalentEffectKind.SkillSpiritCost:            return "스킬 신력 소모";
                case TalentEffectKind.FieldRadiusBonus:           return "공명진 반경";
                case TalentEffectKind.FieldTailSeconds:           return "공명진 이탈 유지 시간";
                case TalentEffectKind.FieldWideningRadiusBonus:   return "넓어지는 공명진 반경";

                default:
                    return "알 수 없는 효과";
            }
        }

        /// <summary>
        /// 효과가 실제로 언제 걸리는지까지 적은 상세 설명
        ///
        /// 이름과 수치만으로는 "고른 호흡 -1"이 무엇을 줄이는지 알 수 없다. 발동 조건은
        /// CombatStatCalculator와 무기 런타임의 실제 분기를 읽어 적었고, 창 길이처럼
        /// 노드가 아니라 CombatConfigSO가 가진 값은 그때그때 읽어 쓴다
        /// </summary>
        public static string GetTalentEffectDetail(TalentEffectKind effect, float value)
        {
            CombatConfigSO combat = ResolveCombatConfig();
            string amount = Format(Math.Abs(value));

            switch (effect)
            {
                // 검 - 「고른 호흡」 계열
                case TalentEffectKind.BasicStaminaReduction:
                    return $"평타를 낼 때 드는 기력이 언제나 {amount} 줄어든다.";
                case TalentEffectKind.RollBasicStaminaReduction:
                    return $"구르기 직후 {WindowText(combat, w => w.CombatTokenDuration)} 안에 내는 " +
                        $"평타의 기력이 {amount} 더 줄어든다. 창이 지나면 사라진다.";
                case TalentEffectKind.ComboWindowBonus:
                    return $"평타 연계를 받아 주는 시간이 {amount}초 늘어난다" +
                        BaseText(combat, w => w.MeleeComboWindowDuration, "초") + ".";
                case TalentEffectKind.SecondComboDamagePercent:
                    return $"평타 2단의 공격력이 {amount}% 늘어난다. 1단과 3단은 그대로다.";
                case TalentEffectKind.ThirdHitSkillCooldown:
                    return $"평타 3단이 적중하면 검 R의 남은 재사용 대기가 {amount}초 줄어든다.";

                // 검 - 반격 표식 계열. 표식 수가 조건이라 수를 정확히 적는다
                case TalentEffectKind.CounterDurationBonus:
                    return $"패링으로 얻은 반격 표식이 사라지기까지의 시간이 {amount}초 늘어난다" +
                        BaseText(combat, w => w.CounterDuration, "초") + ".";
                case TalentEffectKind.ParryStaminaBonus:
                    return $"패링에 성공해 기력을 돌려받을 때 {amount}만큼 더 회복한다.";
                case TalentEffectKind.CounterRecoveryReduction:
                    return $"반격 표식을 2개 이상 쌓고 쓴 R이 적중했을 때만 그 후딜이 {amount}초 " +
                        "줄어든다. 표식이 1개 이하이거나 빗나가면 줄지 않는다.";
                case TalentEffectKind.CounterThirdCombo:
                    return "반격 표식 3개로 쓴 R이 적중하면 이어지는 평타가 3단부터 시작한다. " +
                        $"{WindowText(combat, w => w.CombatTokenDuration)} 안에 내야 하고, " +
                        "그 사이에 피격되면 사라진다.";
                case TalentEffectKind.SkillSecondCombo:
                    return "반격 표식 없이 쓴 일반 R이 적중하면 이어지는 평타가 2단부터 시작한다. " +
                        $"{WindowText(combat, w => w.CombatTokenDuration)} 안에 내야 한다.";
                case TalentEffectKind.MissSkillRecoveryReduction:
                    return $"반격 표식 없이 쓴 일반 R이 빗나갔을 때만 후딜이 {amount}초 줄어든다. " +
                        "적중했거나 반격이었으면 줄지 않는다.";
                case TalentEffectKind.RollSkillDamagePercent:
                    return $"구르기 후 {WindowText(combat, w => w.RollSkillWindow)} 안에 쓴 " +
                        $"일반 검 R의 공격력이 {amount}% 늘어난다. 반격 R에는 붙지 않는다.";
                case TalentEffectKind.SideStepSkill:
                    return "반격 표식 없이 R을 쓸 때 옆으로 움직이고 있으면 앞이 아니라 " +
                        "그 옆 방향으로 파고든다.";

                // 활 - 기력과 연계
                case TalentEffectKind.BowBasicStaminaReduction:
                    return $"평사격의 기력이 언제나 {amount} 줄어든다.";
                case TalentEffectKind.BowSkillStaminaReduction:
                    return $"활 R의 기력이 {amount} 줄어든다.";
                case TalentEffectKind.PrecisionStaminaReduction:
                    return $"정밀 사격의 기력이 {amount} 줄어든다.";
                case TalentEffectKind.BowRollBasicReduction:
                    return $"구르기 직후 {WindowText(combat, w => w.CombatTokenDuration)} 안에 내는 " +
                        $"평사격의 기력이 {amount} 더 줄어든다.";
                case TalentEffectKind.RollShotRecoveryReduction:
                    return $"구르기 직후 {WindowText(combat, w => w.CombatTokenDuration)} 안에 낸 " +
                        $"평사격의 후딜이 {amount}초 줄어든다.";
                case TalentEffectKind.BowComboWindowBonus:
                    return $"사격 연계 입력을 받아 주는 시간이 {amount}초 늘어난다.";
                case TalentEffectKind.PrecisionPierceBonus:
                    return $"화살이 뚫고 지나가는 적이 {amount}명 늘어난다. 활 자체 상한까지만 오른다.";

                // 활 - 쐐기. 터뜨린 사격 종류에 따라 붙는 노드가 갈린다
                case TalentEffectKind.WedgeDurationBonus:
                    return $"박아 둔 쐐기가 대상에게 남아 있는 시간이 {amount}초 늘어난다" +
                        BaseText(combat, w => w.WedgeDuration, "초") + ".";
                case TalentEffectKind.WedgeFirstHitCooldown:
                    return $"쐐기를 박은 뒤 그 쐐기에 처음 적중했을 때 활 R의 남은 재사용 대기가 " +
                        $"{amount}초 줄어든다. 두 번째 적중부터는 줄지 않는다.";
                case TalentEffectKind.WedgeNormalBurstPercent:
                    return $"평사격으로 쐐기를 터뜨렸을 때 그 폭발 피해가 {amount}% 늘어난다" +
                        WedgeHitsText(combat) + ".";
                case TalentEffectKind.WedgePrecisionBurstPercent:
                    return $"정밀 사격으로 쐐기를 터뜨렸을 때 그 폭발 피해가 {amount}% 늘어난다. " +
                        "정밀 사격은 한 발로 쐐기를 채운다.";
                case TalentEffectKind.PrecisionFollowThrough:
                    return "정밀 사격이 쐐기를 터뜨리면 그 화살이 이어서 관통해 맞히는 다음 적에게 " +
                        $"기본 위력의 {Format(value * 100f)}%만큼 한 번 더 들어간다. " +
                        "화살이 새로 나가지도, 터뜨린 대상이 그 자리에서 더 맞지도 않는다.";
                case TalentEffectKind.WedgeRollReduction:
                    return $"쐐기를 설치한 뒤 {WindowText(combat, w => w.CombatTokenDuration)} 안의 " +
                        $"구르기 기력이 {amount} 줄어든다.";

                // 방울 - 신력과 치유
                case TalentEffectKind.HealAmountPercent:
                    return $"치유가 회복시키는 양이 {amount}% 늘어난다.";
                case TalentEffectKind.HealSpiritCost:
                    return $"치유의 신력이 {amount} 줄어든다.";
                case TalentEffectKind.SkillSpiritCost:
                    return $"공명진(R)의 신력이 {amount} 줄어든다.";
                case TalentEffectKind.EchoRecallSpiritCost:
                    return $"잔향 회수의 신력이 {amount} 줄어든다.";
                case TalentEffectKind.BellFirstHitSpiritGain:
                    return $"한 번 휘둘러 처음 맞힌 순간에만 신력을 {amount} 회복한다. " +
                        "몇 명을 맞혔는지와는 상관없다.";
                case TalentEffectKind.EchoDurationSeconds:
                    return $"적에게 걸어 둔 잔향이 남아 있는 시간이 {amount}초 늘어난다.";

                // 방울 - 공명진 연계. 조건이 까다로워 빠짐없이 적는다
                case TalentEffectKind.EchoFieldDamagePercent:
                    return $"회수하는 적이 자기 공명진 안에 있을 때만 그 회수 피해가 {amount}% " +
                        "늘어난다. 시전자가 진 안인지와는 별개 조건이다.";
                case TalentEffectKind.FieldEchoExtendSeconds:
                    return "시전자가 자기 공명진 안에 선 채로 잔향을 거둬 실제 피해를 냈다면 " +
                        $"그 공명진의 지속 시간이 {amount}초 늘어난다. 한 진에 한 번만 늘어난다. " +
                        "거둔 적이 진 안일 필요는 없다.";
                case TalentEffectKind.FieldHealEchoDiscount:
                    return "자기 공명진 안의 부상자를 실제로 회복시키면 이후 " +
                        $"{WindowText(combat, w => w.BellFieldHealDiscountWindow)} 안에 쓰는 " +
                        $"잔향 회수 한 번의 신력이 {amount} 줄어든다. 한 번 쓰면 사라지고, " +
                        "초과 치유나 진 밖 치유로는 열리지 않는다.";
                case TalentEffectKind.FieldHealAfterglowPercent:
                    return "자기 공명진 안의 부상자를 실제로 회복시키면 그 진 안에 있는 아군 " +
                        $"모두를 기본 치유량의 {amount}%만큼 추가로 회복시킨다. 처음 치유한 " +
                        "대상만이 아니다. 한 진에 한 번만 쓸 수 있고, 이 추가 치유는 " +
                        "다시 연계를 열지 않는다.";
                case TalentEffectKind.FieldRadiusBonus:
                    return $"공명진의 반경이 {amount}m 넓어진다. 지속 시간은 그대로다.";
                case TalentEffectKind.FieldTailSeconds:
                    return $"진에서 벗어난 뒤에도 버프가 남아 있는 시간이 {amount}초 늘어난다. " +
                        "상한을 넘지는 않는다.";
                case TalentEffectKind.FieldWideningRadiusBonus:
                    return $"공명진의 반경이 {amount}m 더 넓어지는 대신 진의 지속 시간이 " +
                        $"{PenaltyText(combat)} 짧아진다.";

                // 아래는 현재 트리에 없지만 같은 계약으로 붙을 수 있는 효과들이다
                case TalentEffectKind.CriticalChancePercent:
                    return $"모든 공격의 치명타 확률이 {amount}%p 늘어난다.";
                case TalentEffectKind.AttackSpeedPercent:
                    return $"공격의 선딜과 후딜이 {amount}% 빨라진다.";
                case TalentEffectKind.StaminaCostPercent:
                    return $"기력을 쓰는 모든 행동의 비용이 {amount}% 줄어든다.";
                case TalentEffectKind.SpiritCostPercent:
                    return $"신력을 쓰는 모든 행동의 비용이 {amount}% 줄어든다.";
                case TalentEffectKind.ParryGaugeGain:
                    return $"패링에 성공할 때 쌓이는 게이지가 {amount} 늘어난다.";
                case TalentEffectKind.ParryWindowSeconds:
                    return $"패링으로 인정되는 시간이 {amount}초 늘어난다.";
                case TalentEffectKind.CriticalMultiplierMin:
                    return $"치명타 배율의 하한이 {amount}배가 된다. 여러 개를 사도 가장 큰 값만 쓴다.";
                case TalentEffectKind.ThirdComboDamagePercent:
                    return $"평타 3단의 공격력이 {amount}% 늘어난다.";
                case TalentEffectKind.ArrowSpeedPercent:
                    return $"화살이 날아가는 속도가 {amount}% 빨라진다.";
                case TalentEffectKind.ArrowHitRadiusPercent:
                    return $"화살의 적중 판정 반경이 {amount}% 넓어진다.";
                case TalentEffectKind.RangedDamagePercent:
                    return $"원거리 공격력이 {amount}% 늘어난다.";
                case TalentEffectKind.GuidanceDurationSeconds:
                    return $"화살 유도가 유지되는 시간이 {amount}초 늘어난다.";
                case TalentEffectKind.PierceCount:
                    return $"화살이 뚫고 지나가는 적이 {amount}명 늘어난다.";
                case TalentEffectKind.BurstShotCount:
                    return $"한 번에 나가는 화살이 {amount}발 늘어난다.";

                default:
                    return "효과 설명이 아직 없다.";
            }
        }

        /// <summary> 창 길이처럼 노드가 아니라 CombatConfigSO가 가진 값 </summary>
        private static string WindowText(CombatConfigSO combat, Func<CombatConfigSO, float> read)
        {
            return (combat == null) ? "짧은 시간" : $"{Format(read(combat))}초";
        }

        private static string BaseText(CombatConfigSO combat, Func<CombatConfigSO, float> read,
            string unit)
        {
            return (combat == null) ? string.Empty : $"(기본 {Format(read(combat))}{unit})";
        }

        private static string WedgeHitsText(CombatConfigSO combat)
        {
            return (combat == null) ? string.Empty
                : $". 평사격은 {combat.WedgeHits}번 맞혀야 쐐기가 찬다";
        }

        private static string PenaltyText(CombatConfigSO combat)
        {
            return (combat == null) ? "그만큼" : $"{Format(combat.BellFieldWideningDurationPenalty)}초";
        }

        private static CombatConfigSO ResolveCombatConfig()
        {
            return GameDB.IsInitialized ? GameDB.Combat : null;
        }

        private static string Format(float value)
        {
            return value.ToString("0.##");
        }

        /// <summary>
        /// 특성 노드 하나의 상세 설명. 효과 수치, 포인트 비용, 선행 특성, 현재 상태를 담는다
        ///
        /// 수치는 SO에서만 읽고 선택 가능 여부는 서버와 같은 TalentRules로 판정한다.
        /// 화면이 따로 계산하면 서버가 거부할 노드를 "선택 가능"으로 보여주게 된다
        /// </summary>
        public static string BuildTalentTooltip(TalentTreeSO tree,
            in TalentAllocationSnapshot allocation, int totalPoints,
            in TalentTreeSO.NodeEntry node)
        {
            if (tree == null)
            {
                return "특성 정보를 확인하고 있습니다.";
            }

            StringBuilder builder = new StringBuilder();
            builder.Append(GetTalentEffectName(node.EffectKind)).Append(' ')
                .Append(DescribeTalentValue(node.EffectKind, node.Value));

            // 이름과 수치만으로는 언제 걸리는 효과인지 알 수 없다
            builder.Append('\n').Append(GetTalentEffectDetail(node.EffectKind, node.Value));

            int remaining = totalPoints - allocation.SpentPoints;
            builder.Append('\n').Append($"필요 포인트 {node.PointCost} · 남은 포인트 {remaining}");
            builder.Append('\n').Append(DescribePrerequisites(tree, allocation, node));

            TalentAllocationResult allowed = TalentRules.CanAllocate(tree, allocation,
                totalPoints, node.NodeId, out _);
            builder.Append('\n').Append("상태: ").Append(DescribeTalentNodeState(allowed));

            return builder.ToString();
        }

        /// <summary> 선행 특성 이름과 보유 여부. 트리가 정한 선행 규칙을 그대로 따른다 </summary>
        private static string DescribePrerequisites(TalentTreeSO tree,
            in TalentAllocationSnapshot allocation, in TalentTreeSO.NodeEntry node)
        {
            StringBuilder builder = new StringBuilder();

            foreach (int required in tree.EnumeratePrerequisites(node))
            {
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                if (!tree.TryGetNode(required, out TalentTreeSO.NodeEntry prerequisite))
                {
                    // 정의가 깨진 선행이다. 조용히 빼면 못 사는 이유가 화면에서 사라진다
                    builder.Append($"알 수 없는 특성({required})");
                    continue;
                }

                builder.Append(GetTalentNodeName(prerequisite))
                    .Append(allocation.Contains(required) ? "(보유)" : "(필요)");
            }

            return (builder.Length == 0) ? "선행 특성 없음" : "선행 특성: " + builder;
        }

        private static string DescribeTalentNodeState(TalentAllocationResult allowed)
        {
            switch (allowed)
            {
                case TalentAllocationResult.Success:
                    return "선택 가능";
                case TalentAllocationResult.AlreadyAllocated:
                    return "획득함";
                case TalentAllocationResult.MissingPrerequisite:
                    return "잠김 · 선행 특성을 먼저 선택해야 합니다";
                case TalentAllocationResult.InsufficientPoints:
                    return "포인트 부족";
                case TalentAllocationResult.WeaponMismatch:
                    return "잠김 · 다른 무기의 특성입니다";
                default:
                    return "잠김 · 지금은 선택할 수 없습니다";
            }
        }

        public static string GetInventoryResult(InventoryResult result)
        {
            switch (result)
            {
                case InventoryResult.Success:
                    return "장비 변경이 완료되었습니다.";
                case InventoryResult.Full:
                case InventoryResult.StackFull:
                    return "배낭에 빈자리가 없습니다.";
                case InventoryResult.StaleRevision:
                case InventoryResult.ItemChanged:
                    return "보유 물품이 바뀌었습니다. 다시 선택해 주세요.";
                case InventoryResult.NoEquippedWeapon:
                    return "해제할 장비가 없습니다.";
                case InventoryResult.SlotEmpty:
                    return "선택한 칸이 비어 있습니다.";
                case InventoryResult.InvalidState:
                    return "지금은 장비를 바꿀 수 없습니다.";
                case InventoryResult.DuplicateItem:
                    return "이미 처리된 장비입니다.";
                default:
                    return "장비 요청을 처리하지 못했습니다.";
            }
        }

        public static string GetTalentResult(TalentAllocationResult result)
        {
            switch (result)
            {
                case TalentAllocationResult.Success:
                    return "특성이 반영되었습니다.";
                case TalentAllocationResult.MissingPrerequisite:
                    return "선으로 이어진 앞 특성을 먼저 선택해 주세요.";
                case TalentAllocationResult.AlreadyAllocated:
                    return "이미 선택한 특성입니다.";
                case TalentAllocationResult.InsufficientPoints:
                    return "사용할 수 있는 특성 포인트가 부족합니다.";
                case TalentAllocationResult.WeaponMismatch:
                    return "현재 무기에 맞는 특성만 선택할 수 있습니다.";
                case TalentAllocationResult.StaleRevision:
                    return "특성 상태가 바뀌었습니다. 다시 선택해 주세요.";
                default:
                    return "특성 요청을 처리하지 못했습니다.";
            }
        }

        public static string GetTransactionResult(in ShopTransactionResult result)
        {
            if (!result.Succeeded)
            {
                return GetTransactionRejection(result.Rejection);
            }

            switch (result.Operation)
            {
                case TransactionOperation.Purchase:
                    return $"{GetShopItemName(result.ItemKind)} 구매가 완료되었습니다.";
                case TransactionOperation.UsePotion:
                    return $"{GetShopItemName(result.ItemKind)}을 사용했습니다. " +
                        $"회복량 {result.AppliedAmount:0.#}";
                case TransactionOperation.RerollTicket:
                    return "장비 재조정이 완료되었습니다.";
                case TransactionOperation.UpgradeTicket:
                    return "장비 승급이 완료되었습니다.";
                default:
                    return "요청이 완료되었습니다.";
            }
        }

        public static string GetTransactionRejection(TransactionRejection rejection)
        {
            switch (rejection)
            {
                case TransactionRejection.None:
                    return "요청이 완료되었습니다.";
                case TransactionRejection.Paused:
                    return "런이 멈춘 동안에는 이용할 수 없습니다.";
                case TransactionRejection.SceneMismatch:
                    return "같은 장소에 있을 때 이용할 수 있습니다.";
                case TransactionRejection.Dead:
                    return "행동할 수 있는 상태에서 이용해 주세요.";
                case TransactionRejection.InCombat:
                    return "전투가 끝난 뒤 잠시 기다려 주세요.";
                case TransactionRejection.OutOfRange:
                    return "대상에게 더 가까이 다가가 주세요.";
                case TransactionRejection.ShopClosed:
                    return "객주가 아직 문을 열지 않았습니다.";
                case TransactionRejection.InsufficientCurrency:
                    return "공용 노잣돈이 부족합니다.";
                case TransactionRejection.InventoryFull:
                    return "배낭에 빈자리가 없습니다.";
                case TransactionRejection.StaleWalletRevision:
                case TransactionRejection.StaleInventoryRevision:
                    return "보유 상태가 바뀌었습니다. 다시 시도해 주세요.";
                case TransactionRejection.MaxGrade:
                    return "이미 가장 높은 등급의 장비입니다.";
                case TransactionRejection.OfferConflict:
                    return "이미 진행 중인 양도 제안이 있습니다.";
                case TransactionRejection.OfferNotFound:
                case TransactionRejection.OfferExpired:
                    return "양도 제안이 끝났습니다.";
                case TransactionRejection.ResourceFull:
                    return "이미 자원이 가득 찼습니다.";
                case TransactionRejection.RecoveryBlocked:
                    return "지금은 회복할 수 없습니다.";
                case TransactionRejection.InvalidItem:
                    return "선택한 물품을 사용할 수 없습니다.";
                default:
                    return "요청을 처리하지 못했습니다. 상태를 확인해 주세요.";
            }
        }

        public static string GetOutcome(RunOutcome outcome)
        {
            switch (outcome)
            {
                case RunOutcome.Victory:
                    return "토벌 성공";
                case RunOutcome.TimeExpired:
                    return "시간이 다해 토벌에 실패했습니다.";
                case RunOutcome.PartyWipe:
                    return "모든 참가자가 쓰러졌습니다.";
                case RunOutcome.PlayerLeft:
                    return "참가자 이탈로 런이 종료되었습니다.";
                default:
                    return "런 결과";
            }
        }

        public static string GetEvaluationGrade(RunEvaluationGrade grade)
        {
            switch (grade)
            {
                case RunEvaluationGrade.D:
                    return "D 등급";
                case RunEvaluationGrade.C:
                    return "C 등급";
                case RunEvaluationGrade.B:
                    return "B 등급";
                case RunEvaluationGrade.A:
                    return "A 등급";
                case RunEvaluationGrade.S:
                    return "S 등급";
                default:
                    return "평가 없음";
            }
        }

        private static string DescribeOption(EquipmentOptionKind option, float value)
        {
            string name;
            string unit;
            switch (option)
            {
                case EquipmentOptionKind.None:
                    return string.Empty;
                case EquipmentOptionKind.CriticalChance:
                    name = "치명 확률";
                    unit = "%";
                    break;
                case EquipmentOptionKind.AttackSpeed:
                    name = "공격 속도";
                    unit = "%";
                    break;
                case EquipmentOptionKind.StaminaCostReduction:
                    name = "스태미나 절약";
                    unit = "%";
                    break;
                case EquipmentOptionKind.CounterDuration:
                    return $"반격 표식 유지 +{value:0.##}초";
                case EquipmentOptionKind.ParryGaugeGain:
                    name = "패링 게이지";
                    unit = string.Empty;
                    break;
                case EquipmentOptionKind.ParryWindow:
                    name = "패링 판정";
                    unit = "초";
                    break;
                case EquipmentOptionKind.CriticalMultiplierMin:
                    name = "치명 배율";
                    unit = string.Empty;
                    break;
                case EquipmentOptionKind.ArrowSpeed:
                    name = "화살 속도";
                    unit = "%";
                    break;
                case EquipmentOptionKind.GuidanceDuration:
                    name = "유도 시간";
                    unit = "초";
                    break;
                case EquipmentOptionKind.PierceCount:
                    name = "관통";
                    unit = "회";
                    break;
                case EquipmentOptionKind.HealAmount:
                    name = "치유량";
                    unit = "%";
                    break;
                case EquipmentOptionKind.BuffDuration:
                    name = "지원 시간";
                    unit = "%";
                    break;
                case EquipmentOptionKind.SpiritCostReduction:
                    name = "신력 절약";
                    unit = "%";
                    break;
                default:
                    return string.Empty;
            }

            return $"{name} +{value:0.##}{unit}";
        }
    }
}
