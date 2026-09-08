// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Systems
{
    /// <summary> 서버 런 소유자가 보관하는 장비 추첨기. 확정 값만 반환한다 </summary>
    public sealed class EquipmentGenerator
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int MAX_OPTIONS = 2; // WeaponCombatSnapshot의 옵션 필드 수

        private readonly Random _random;
        private readonly EquipmentSO.GradeEntry[] _grades;
        private readonly EquipmentSO.OptionEntry[][] _options;
        private readonly double[][] _weights;
        private uint _lastInstanceId;

        /****************************************
        *              Properties
        ****************************************/

        public uint LastInstanceId => _lastInstanceId;

        /****************************************
        *                Methods
        ****************************************/

        // 씬마다 새로 만들면 ID가 겹친다. 서버 런 소유자가 씬 전환 동안 같은 인스턴스를 유지한다
        public EquipmentGenerator(EquipmentSO equipment, DropTableSO dropTable, Random random,
            uint lastInstanceId = 0u)
        {
            if (equipment == null)
            {
                throw new ArgumentNullException(nameof(equipment));
            }

            if (dropTable == null)
            {
                throw new ArgumentNullException(nameof(dropTable));
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            _grades = ReadGrades(equipment);
            _options = ReadOptions(equipment, _grades);
            _weights = ReadWeights(dropTable);
            _random = random;
            _lastInstanceId = lastInstanceId;
        }

        /// <summary> 무기 선택과 보상 지급 여부는 호출 서버가 결정한다 </summary>
        public WeaponCombatSnapshot Generate(WeaponKind weaponKind, int fieldLevel)
        {
            if (((int) weaponKind < 0) || ((int) weaponKind >= _options.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(weaponKind));
            }

            if ((fieldLevel < 0) || (fieldLevel >= _weights.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(fieldLevel));
            }

            if (_lastInstanceId == uint.MaxValue)
            {
                throw new InvalidOperationException("장비 인스턴스 ID를 모두 사용했습니다.");
            }

            int gradeIndex = RollGrade(_weights[fieldLevel]);
            EquipmentSO.GradeEntry grade = _grades[gradeIndex];
            var result = new WeaponCombatSnapshot
            {
                InstanceId  = _lastInstanceId + 1u,
                WeaponKind  = weaponKind,
                Grade       = grade.Grade,
                AttackPower = RollValue(grade.AttackMin, grade.AttackMax),
            };

            if ((grade.OptionCount > 0) && (_random.NextDouble() < grade.OptionChance))
            {
                EquipmentSO.OptionEntry[] pool = _options[(int) weaponKind];
                int first = _random.Next(pool.Length);
                result.Option0Kind = pool[first].Kind;
                result.Option0Value = RollValue(pool[first].ValueMin, pool[first].ValueMax);

                if (grade.OptionCount == MAX_OPTIONS)
                {
                    int second = _random.Next(pool.Length - 1);
                    if (second >= first)
                    {
                        ++second;
                    }

                    result.Option1Kind = pool[second].Kind;
                    result.Option1Value = RollValue(pool[second].ValueMin, pool[second].ValueMax);
                }
            }

            _lastInstanceId = result.InstanceId;
            return result;
        }

        private int RollGrade(double[] weights)
        {
            double total = 0d;
            for (int i = 0; i < weights.Length; ++i)
            {
                total += weights[i];
            }

            double roll = _random.NextDouble() * total;
            int lastPositive = 0;
            for (int i = 0; i < weights.Length; ++i)
            {
                if (weights[i] <= 0d)
                {
                    continue;
                }

                lastPositive = i;
                if (roll < weights[i])
                {
                    return i;
                }

                roll -= weights[i];
            }

            // 부동소수점 경계에서도 가중치 0인 등급으로 넘어가지 않는다
            return lastPositive;
        }

        private float RollValue(float min, float max)
        {
            return (float) (min + (((double) max - min) * _random.NextDouble()));
        }

        private static EquipmentSO.GradeEntry[] ReadGrades(EquipmentSO equipment)
        {
            var grades = new EquipmentSO.GradeEntry[Constants.Combat.ITEM_GRADE_COUNT];
            for (int i = 0; i < grades.Length; ++i)
            {
                if ((!equipment.TryGetGrade((ItemGrade) i, out var entry)) ||
                    (!IsRange(entry.AttackMin, entry.AttackMax)) || (entry.AttackMin <= 0f) ||
                    (!IsFinite(entry.OptionChance)) || (entry.OptionChance < 0f) ||
                    (entry.OptionChance > 1f) || (entry.OptionCount < 0) ||
                    (entry.OptionCount > MAX_OPTIONS) ||
                    ((entry.OptionCount == 0) && (entry.OptionChance != 0f)))
                {
                    throw new ArgumentException($"장비 등급 데이터가 유효하지 않습니다: {(ItemGrade) i}", nameof(equipment));
                }

                grades[i] = entry;
            }

            return grades;
        }

        private static EquipmentSO.OptionEntry[][] ReadOptions(EquipmentSO equipment,
            EquipmentSO.GradeEntry[] grades)
        {
            int requiredCount = 0;
            for (int i = 0; i < grades.Length; ++i)
            {
                if (grades[i].OptionChance > 0f)
                {
                    requiredCount = Math.Max(requiredCount, grades[i].OptionCount);
                }
            }

            var pools = new EquipmentSO.OptionEntry[Constants.Combat.WEAPON_KIND_COUNT][];
            for (int i = 0; i < pools.Length; ++i)
            {
                var pool = new List<EquipmentSO.OptionEntry>();
                AddOptions(pool, equipment.CommonOptions, (WeaponKind) i, true);
                if (equipment.TryGetWeaponOptions((WeaponKind) i, out var weaponOptions))
                {
                    AddOptions(pool, weaponOptions, (WeaponKind) i, false);
                }

                if (pool.Count < requiredCount)
                {
                    throw new ArgumentException($"장비 옵션 풀이 부족합니다: {(WeaponKind) i}", nameof(equipment));
                }

                pools[i] = pool.ToArray();
            }

            return pools;
        }

        private static void AddOptions(List<EquipmentSO.OptionEntry> pool,
            EquipmentSO.OptionEntry[] entries, WeaponKind weaponKind, bool common)
        {
            if (entries == null)
            {
                return;
            }

            for (int i = 0; i < entries.Length; ++i)
            {
                var entry = entries[i];
                if ((!IsRange(entry.ValueMin, entry.ValueMax)) ||
                    (!IsAllowedOption(entry.Kind, weaponKind, common)))
                {
                    throw new ArgumentException($"장비 옵션이 유효하지 않습니다: {entry.Kind}", nameof(entries));
                }

                for (int j = 0; j < pool.Count; ++j)
                {
                    if (pool[j].Kind == entry.Kind)
                    {
                        throw new ArgumentException($"장비 옵션이 중복되었습니다: {entry.Kind}", nameof(entries));
                    }
                }

                pool.Add(entry);
            }
        }

        private static bool IsAllowedOption(EquipmentOptionKind kind, WeaponKind weaponKind, bool common)
        {
            if (common)
            {
                return ((kind == EquipmentOptionKind.CriticalChance) ||
                        (kind == EquipmentOptionKind.AttackSpeed) ||
                        (kind == EquipmentOptionKind.StaminaCostReduction));
            }

            switch (weaponKind)
            {
                case WeaponKind.Sword:
                    return ((kind == EquipmentOptionKind.ParryGaugeGain) ||
                            (kind == EquipmentOptionKind.ParryWindow) ||
                            (kind == EquipmentOptionKind.CriticalMultiplierMin));
                case WeaponKind.Bow:
                    return ((kind == EquipmentOptionKind.ArrowSpeed) ||
                            (kind == EquipmentOptionKind.GuidanceDuration) ||
                            (kind == EquipmentOptionKind.PierceCount));
                case WeaponKind.Bell:
                    return ((kind == EquipmentOptionKind.HealAmount) ||
                            (kind == EquipmentOptionKind.BuffDuration) ||
                            (kind == EquipmentOptionKind.SpiritCostReduction));
                default:
                    return false;
            }
        }

        private static double[][] ReadWeights(DropTableSO dropTable)
        {
            var result = new double[Constants.Combat.FIELD_LEVEL_COUNT][];
            for (int field = 0; field < result.Length; ++field)
            {
                if (!dropTable.TryGetEntry(field, out var entry))
                {
                    throw new ArgumentException($"드랍 구간 데이터가 없습니다: {field}", nameof(dropTable));
                }

                var weights = new double[]
                {
                    entry.WeightCommon, entry.WeightTalisman, entry.WeightSpiritual, entry.WeightDivine,
                };
                double total = 0d;
                for (int i = 0; i < weights.Length; ++i)
                {
                    if ((double.IsNaN(weights[i])) || (double.IsInfinity(weights[i])) || (weights[i] < 0d))
                    {
                        throw new ArgumentException($"드랍 가중치가 유효하지 않습니다: {field}", nameof(dropTable));
                    }

                    total += weights[i];
                }

                if (total <= 0d)
                {
                    throw new ArgumentException($"드랍 가중치 합이 0입니다: {field}", nameof(dropTable));
                }

                result[field] = weights;
            }

            return result;
        }

        private static bool IsRange(float min, float max)
        {
            return ((IsFinite(min)) && (IsFinite(max)) && (min <= max));
        }

        private static bool IsFinite(float value)
        {
            return ((!float.IsNaN(value)) && (!float.IsInfinity(value)));
        }
    }
}
