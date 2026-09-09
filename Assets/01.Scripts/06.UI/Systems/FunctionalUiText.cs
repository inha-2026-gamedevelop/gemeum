// 프로젝트 내부
using Geumeum.Common;
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
                case TalentEffectKind.ParryWindowSeconds:
                case TalentEffectKind.GuidanceDurationSeconds:
                    return $"+{value:0.##}초";
                case TalentEffectKind.ParryGaugeGain:
                case TalentEffectKind.CriticalMultiplierMin:
                case TalentEffectKind.PierceCount:
                case TalentEffectKind.BurstShotCount:
                    return $"+{value:0.##}";
                default:
                    return $"{(value >= 0f ? "+" : string.Empty)}{value:0.#}%";
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
                    return "인벤토리에 빈자리가 없습니다.";
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
                    return "앞 행의 특성을 먼저 선택해 주세요.";
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
                    return "인벤토리에 빈자리가 없습니다.";
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
