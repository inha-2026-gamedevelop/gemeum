// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Network
{
    /// <summary> 서버가 열리기 전에 데이터와 씬의 필수 구성을 검사한다 </summary>
    public static class GeumeumBootValidator
    {
        public enum SceneProfile
        {
            Main     = 0,
            DevScene = 1,
        }

        private const string EDITOR_ONLY_TAG = "EditorOnly";
        private const int    REQUIRED_BOSS_PHASE_COUNT = 2;
        private const int    REQUIRED_BOSS_PATTERN_COUNT = 10;

        /// <summary> 기존 호출부와의 호환용 데이터 검사. 보스는 별도 호출에서 검사한다 </summary>
        public static void ValidateData(
            BarrierPhaseSO barrier,
            LevelCurveSO levelCurve,
            SpawnConfigSO spawn,
            List<string> failures)
        {
            ValidateDataCore(barrier, levelCurve, spawn, null, SceneProfile.Main, false, failures);
        }

        /// <summary> 씬 프로필에 맞는 런 데이터를 검사한다 </summary>
        public static void ValidateData(
            BarrierPhaseSO barrier,
            LevelCurveSO levelCurve,
            SpawnConfigSO spawn,
            BossPatternSO boss,
            SceneProfile profile,
            List<string> failures)
        {
            ValidateDataCore(barrier, levelCurve, spawn, boss, profile, profile == SceneProfile.Main, failures);
        }

        private static void ValidateDataCore(
            BarrierPhaseSO barrier,
            LevelCurveSO levelCurve,
            SpawnConfigSO spawn,
            BossPatternSO boss,
            SceneProfile profile,
            bool strict,
            List<string> failures)
        {
            if (failures == null)
            {
                return;
            }

            if ((profile != SceneProfile.Main) && (profile != SceneProfile.DevScene))
            {
                failures.Add($"알 수 없는 씬 프로필이다: {profile}");
                return;
            }

            // 호출자가 수집한 참조 오류를 보존하고 이 검사 결과를 이어 붙인다
            ValidateBarrier(barrier, strict, failures);
            ValidateLevelCurve(levelCurve, strict, failures);
            ValidateSpawn(spawn, strict, failures);
            if (strict)
            {
                ValidateBoss(boss, failures);
            }
        }

        /// <summary> 정식 런의 특성과 정산 데이터를 같은 기준으로 검사한다 </summary>
        public static void ValidateProgressionData(CombatConfigSO combat,
            TalentTreeSO talent, ScoreRuleSO score, SceneProfile profile,
            List<string> failures)
        {
            if ((failures == null) || (profile != SceneProfile.Main))
            {
                return;
            }

            if (combat != null)
            {
                PositiveFinite("최소 자원 비용", combat.MinimumResourceCost, failures);
                PositiveFinite("최대 공격 속도 배율", combat.MaximumAttackSpeedMultiplier, failures);
                PositiveFinite("연사 간격", combat.BurstShotInterval, failures);
                PositiveFinite("공명진 반경", combat.BellFieldRadius, failures);
                PositiveFinite("공명진 지속 시간", combat.BellFieldDuration, failures);
                PositiveFinite("공명진 체류 판정 간격", combat.BellFieldSampleInterval, failures);
                PositiveFinite("공명진 이탈 유지 시간", combat.BellFieldTailDuration, failures);

                // 상한이 기본값보다 낮으면 장비 401이 붙는 순간 유지시간이 거꾸로 줄어든다
                if (combat.BellFieldTailMaximum < combat.BellFieldTailDuration)
                {
                    failures.Add("공명진 이탈 유지 상한이 기본 유지 시간보다 짧다");
                }

                if (combat.BellFieldDamageMultiplier < 1f)
                {
                    failures.Add("공명진 공격력 계수가 1 미만이다");
                }
            }

            if (talent == null)
            {
                failures.Add("특성 데이터가 없다");
            }
            else if (!talent.ValidateDefinition(out string talentFailure))
            {
                failures.Add("특성 데이터가 유효하지 않다: " + talentFailure);
            }

            if (score == null)
            {
                failures.Add("결과 점수 데이터가 없다");
            }
            else if (!score.ValidateEvaluationGrades(out string scoreFailure))
            {
                failures.Add("결과 평가 데이터가 유효하지 않다: " + scoreFailure);
            }
        }

        /// <summary> 거래와 정밀 사격이 소비하는 생산 데이터를 검사한다 </summary>
        public static void ValidatePlayerServices(CombatConfigSO combat, ShopSO shop, List<string> failures)
        {
            if (failures == null)
            {
                return;
            }

            if (combat == null)
            {
                failures.Add("플레이어 전투 데이터가 없다");
            }
            else
            {
                PositiveFinite("정밀 사격 준비 시간", combat.PrecisionShotHoldDuration, failures);
                Range("정밀 사격 이동 배율", combat.PrecisionShotMoveSpeedMultiplier, 0f, 1f, failures);
                NonNegativeFinite("정밀 사격 스태미나 비용", combat.PrecisionShotStaminaCost, failures);
                PositiveFinite("상호작용 거리", combat.InteractRange, failures);
                NonNegativeFinite("상호작용 비전투 시간", combat.InteractOutOfCombatSeconds, failures);
                PositiveFinite("장비 양도 제안 시간", combat.EquipmentTransferOfferDuration, failures);
            }

            if ((shop == null) || (shop.Items == null) || (shop.Items.Length == 0))
            {
                failures.Add("객주 상품 데이터가 없다");
                return;
            }

            if (shop.OpenPhase != RunPhase.Shrink2)
            {
                failures.Add("객주는 Shrink2에 열려야 한다");
            }

            var kinds = new HashSet<ShopItemKind>();
            for (int i = 0; i < shop.Items.Length; ++i)
            {
                ShopSO.ItemEntry item = shop.Items[i];
                if ((!Enum.IsDefined(typeof(ShopItemKind), item.Kind)) || ((int)item.Kind == 0)
                    || (!kinds.Add(item.Kind)))
                {
                    failures.Add($"객주 상품 종류가 잘못되었거나 중복되었다: {item.Kind}");
                }

                if ((item.Price <= 0) || (item.MaxStack <= 0)
                    || ((item.CarryLimit != -1) && (item.CarryLimit <= 0)))
                {
                    failures.Add($"객주 상품 가격 또는 보관 한도가 잘못되었다: {item.Kind}");
                }

                NonNegativeFinite($"객주 {item.Kind} 효과량", item.EffectValue, failures);
                if ((item.Kind == ShopItemKind.HealthPotion) || (item.Kind == ShopItemKind.SpiritPotion))
                {
                    PositiveFinite($"객주 {item.Kind} 회복량", item.EffectValue, failures);
                }
            }
        }

        private static void ValidateBarrier(BarrierPhaseSO barrier, bool strict, List<string> failures)
        {
            if (barrier == null)
            {
                failures.Add("결계 데이터가 없다");
                return;
            }

            if (strict)
            {
                if (!barrier.ValidateDefinition(out string barrierFailure))
                {
                    failures.Add($"결계 목표 원 정의 오류: {barrierFailure}");
                }
                PositiveFinite("결계 필드 제한 시간", barrier.FieldTimeLimitSeconds, failures);
                PositiveFinite("보스 입구 대기 시간", barrier.BossEntryCountdownSeconds, failures);
                if (barrier.BossEntryCountdownSeconds > barrier.FieldTimeLimitSeconds)
                {
                    failures.Add("보스 입구 대기 시간이 필드 제한 시간을 넘는다");
                }

                BarrierPhaseSO.PhaseEntry finalField;
                if ((barrier.Phases != null) && barrier.TryGetPhase(RunPhase.Shrink3, out finalField)
                    && (barrier.FieldTimeLimitSeconds - barrier.BossEntryCountdownSeconds < finalField.StartSeconds))
                {
                    failures.Add("보스 입구 자동 대기가 마지막 필드 구간보다 먼저 시작한다");
                }
                PositiveFinite("결계 최대 반경", barrier.MaxRadius, failures);
                NonNegativeFinite("결계 축소 시간", barrier.ShrinkDuration, failures);
                NonNegativeFinite("결계 경고 선행 시간", barrier.WarningLeadTime, failures);
                PositiveFinite("결계 피해 틱 간격", barrier.DamageTickInterval, failures);
                Range("결계 쇠약 회복 배율", barrier.WeakenedHealingMultiplier, 0f, 1f, failures);

                IReadOnlyList<BarrierPhaseSO.PhaseEntry> phases = barrier.Phases;
                if (phases != null)
                {
                    for (int i = 0; i < phases.Count; ++i)
                    {
                        BarrierPhaseSO.PhaseEntry phase = phases[i];
                        NonNegativeFinite($"결계 {phase.Phase} 시작 시간", phase.StartSeconds, failures);
                        NonNegativeFinite($"결계 {phase.Phase} 종료 시간", phase.EndSeconds, failures);
                        Range($"결계 {phase.Phase} 반경 비율", phase.RadiusRatio, 0f, 1f, failures);
                        NonNegativeFinite($"결계 {phase.Phase} 초당 피해", phase.DamagePerSecond, failures);
                    }
                }
            }

            RunPhaseTimeline timeline = RunTimelineBuilder.Build(barrier);
            string error;
            if (!timeline.Validate(barrier.FieldTimeLimitSeconds, out error))
            {
                failures.Add("결계 타임라인이 잘못되었다: " + error);
            }
        }

        private static void ValidateLevelCurve(LevelCurveSO levelCurve, bool strict, List<string> failures)
        {
            if (levelCurve == null)
            {
                failures.Add("레벨 곡선 데이터가 없다");
                return;
            }

            LevelCurveSO.LevelEntry startEntry;
            if (!levelCurve.TryGetLevel(Constants.Combat.START_LEVEL, out startEntry))
            {
                failures.Add($"레벨 곡선에 시작 레벨 {Constants.Combat.START_LEVEL}이 없다");
            }
            else if ((!IsFinite(startEntry.MaxHealth)) || (startEntry.MaxHealth <= 0f))
            {
                failures.Add("시작 레벨 최대 HP가 유효하지 않다");
            }

            if (!strict)
            {
                return;
            }

            if (levelCurve.TalentPointPerLevel <= 0)
            {
                failures.Add("레벨 곡선의 레벨당 특성 포인트가 양수가 아니다");
            }

            int expectedLevelCount = levelCurve.MaxLevel - Constants.Combat.START_LEVEL + 1;
            if (levelCurve.LevelCount == 0)
            {
                failures.Add("레벨 곡선 배열이 비어 있다");
                return;
            }

            if (levelCurve.LevelCount != expectedLevelCount)
            {
                failures.Add($"레벨 곡선 항목 수가 {expectedLevelCount}개가 아니다: {levelCurve.LevelCount}");
            }

            for (int level = Constants.Combat.START_LEVEL; level <= levelCurve.MaxLevel; ++level)
            {
                LevelCurveSO.LevelEntry entry;
                if (!levelCurve.TryGetLevel(level, out entry))
                {
                    failures.Add($"레벨 곡선에 레벨 {level}이 없다");
                    continue;
                }

                PositiveFinite($"레벨 {entry.Level} 최대 HP", entry.MaxHealth, failures);
                if (level == levelCurve.MaxLevel)
                {
                    if (entry.RequiredExperience != 0)
                    {
                        failures.Add($"레벨 {level}의 다음 레벨 경험치가 0이 아니다: {entry.RequiredExperience}");
                    }
                }
                else if (entry.RequiredExperience <= 0)
                {
                    failures.Add($"레벨 {level}의 필요 경험치가 양수가 아니다: {entry.RequiredExperience}");
                }
            }
        }

        private static void ValidateSpawn(SpawnConfigSO spawn, bool strict, List<string> failures)
        {
            if (spawn == null)
            {
                failures.Add("스폰 데이터가 없다");
                return;
            }

            if (strict && !spawn.ValidateDefinition(out string spawnFailure))
            {
                failures.Add($"스폰 몰이 정의 오류: {spawnFailure}");
            }

            RunPhase[] requiredPhases = { RunPhase.Farming, RunPhase.Shrink1, RunPhase.Shrink2 };
            for (int i = 0; i < requiredPhases.Length; ++i)
            {
                SpawnConfigSO.PhaseEntry phase;
                if (!spawn.TryGetPhase(requiredPhases[i], out phase))
                {
                    failures.Add($"스폰 데이터에 {requiredPhases[i]} 구간이 없다");
                    continue;
                }

                if (phase.ConcurrentMax <= 0)
                {
                    failures.Add($"스폰 {requiredPhases[i]} 동시 상한이 양수가 아니다");
                }

                if (!strict)
                {
                    continue;
                }

                if ((phase.ConcurrentMin < 0) || (phase.ConcurrentMin > phase.ConcurrentMax))
                {
                    failures.Add($"스폰 {requiredPhases[i]} 동시 하한이 범위를 벗어났다");
                }

                NonNegativeFinite($"스폰 {requiredPhases[i]} 리스폰 간격", phase.RespawnInterval, failures);
            }
        }

        private static void ValidateBoss(BossPatternSO boss, List<string> failures)
        {
            if (boss == null)
            {
                failures.Add("보스 패턴 데이터가 없다");
                return;
            }

            PositiveFinite("보스 최대 HP", boss.MaxHealth, failures);
            NonNegativeFinite("보스 페이즈 전환 잠금 시간", boss.TransitionLockDuration, failures);
            PositiveFinite("보스 제한 시간", boss.BossTimeLimitSeconds, failures);
            NonNegativeFinite("보스 광폭화 시작 시간", boss.EnrageStartSeconds, failures);
            NonNegativeFinite("보스 광폭화 경고 시간", boss.EnrageWarningSeconds, failures);
            PositiveFinite("보스 1페이즈 공격 간격", boss.Phase1AttackIntervalSeconds, failures);
            PositiveFinite("보스 2페이즈 공격 간격", boss.Phase2AttackIntervalSeconds, failures);
            PositiveFinite("보스 광폭화 공격 간격", boss.EnragedAttackIntervalSeconds, failures);
            PositiveFinite("보스 광폭화 피해 배율", boss.EnrageDamageMultiplier, failures);
            PositiveFinite("보스 그로기 게이지 최대치", boss.GroggyGaugeMax, failures);
            NonNegativeFinite("보스 표식 창", boss.BrandWindow, failures);
            PositiveFinite("보스 그로기 시간", boss.GroggyDuration, failures);
            NonNegativeFinite("보스 기상 경고 시간", boss.GroggyWakeWarningDuration, failures);
            NonNegativeFinite("보스 그로기 면역 시간", boss.GroggyImmunityDuration, failures);
            NonNegativeFinite("보스 표식 쿨다운", boss.BrandCooldown, failures);
            NonNegativeFinite("보스 그로기 본체 배율", boss.GroggyBodyDamageMultiplier, failures);
            NonNegativeFinite("보스 그로기 꼬리 배율", boss.GroggyTailDamageMultiplier, failures);
            PositiveFinite("보스 꼬리 부위 HP", boss.TailSegmentHealth, failures);
            Range("보스 HP 게이트 비율", boss.HealthGateRatio, 0f, 1f, failures);
            NonNegativeFinite("보스 미파괴 꼬리 공격 배율", boss.UnbrokenTailAttackMultiplier, failures);
            PositiveFinite("보스 원혼 HP", boss.WonhonHealth, failures);
            PositiveFinite("보스 최종 구간 제한 시간", boss.FinalGauntletWindowSeconds, failures);

            if (boss.TailSegmentCount <= 0)
            {
                failures.Add("보스 꼬리 부위 수가 양수가 아니다");
            }

            if (IsFinite(boss.EnrageStartSeconds) && IsFinite(boss.BossTimeLimitSeconds) &&
                (boss.EnrageStartSeconds > boss.BossTimeLimitSeconds))
            {
                failures.Add("보스 광폭화 시작 시간이 제한 시간을 넘었다");
            }

            if (IsFinite(boss.EnrageWarningSeconds) && IsFinite(boss.EnrageStartSeconds) &&
                (boss.EnrageWarningSeconds > boss.EnrageStartSeconds))
            {
                failures.Add("보스 광폭화 경고 시간이 시작 시간보다 늦다");
            }

            if (IsFinite(boss.GroggyWakeWarningDuration) && IsFinite(boss.GroggyDuration) &&
                (boss.GroggyWakeWarningDuration > boss.GroggyDuration))
            {
                failures.Add("보스 기상 경고 시간이 그로기 시간보다 길다");
            }

            IReadOnlyList<BossPatternSO.PatternEntry> patterns = boss.Patterns;
            if ((patterns == null) || (patterns.Count == 0))
            {
                failures.Add("보스 패턴 배열이 비어 있다");
                return;
            }

            Dictionary<int, BossPatternSO.PatternEntry> patternById =
                new Dictionary<int, BossPatternSO.PatternEntry>();
            for (int i = 0; i < patterns.Count; ++i)
            {
                BossPatternSO.PatternEntry pattern = patterns[i];
                if (pattern.PatternId <= 0)
                {
                    failures.Add($"보스 패턴 id가 양수가 아니다: {pattern.PatternId}");
                }
                else if (patternById.ContainsKey(pattern.PatternId))
                {
                    failures.Add($"보스 패턴 id가 중복되었다: {pattern.PatternId}");
                }
                else
                {
                    patternById.Add(pattern.PatternId, pattern);
                }

                NonNegativeFinite($"보스 패턴 {pattern.PatternId} 예고 시간", pattern.TelegraphDuration, failures);
                NonNegativeFinite($"보스 패턴 {pattern.PatternId} 활성 시간", pattern.ActiveDuration, failures);
                NonNegativeFinite($"보스 패턴 {pattern.PatternId} 쿨다운", pattern.Cooldown, failures);
                NonNegativeFinite($"보스 패턴 {pattern.PatternId} 피해량", pattern.Damage, failures);
            }

            for (int patternId = 1; patternId <= REQUIRED_BOSS_PATTERN_COUNT; ++patternId)
            {
                if (!patternById.ContainsKey(patternId))
                {
                    failures.Add($"보스 패턴 id {patternId}가 없다");
                }
            }

            ValidateBossPhases(boss, patternById, failures);
            ValidateGaugeReductions(boss, patternById, failures);
            if (!boss.ValidateAdvancedDefinition(out string advancedFailure))
            {
                failures.Add($"보스 확장 패턴 정의 오류: {advancedFailure}");
            }
        }

        private static void ValidateBossPhases(
            BossPatternSO boss,
            Dictionary<int, BossPatternSO.PatternEntry> patternById,
            List<string> failures)
        {
            IReadOnlyList<BossPatternSO.PhaseEntry> phases = boss.Phases;
            if ((phases == null) || (phases.Count == 0))
            {
                failures.Add("보스 페이즈 배열이 비어 있다");
                return;
            }

            if (phases.Count != REQUIRED_BOSS_PHASE_COUNT)
            {
                failures.Add($"보스 페이즈 수가 {REQUIRED_BOSS_PHASE_COUNT}개가 아니다: {phases.Count}");
            }

            HashSet<int> seenPhaseIds = new HashSet<int>();
            Dictionary<int, BossPatternSO.PhaseEntry> phaseById =
                new Dictionary<int, BossPatternSO.PhaseEntry>();
            for (int i = 0; i < phases.Count; ++i)
            {
                BossPatternSO.PhaseEntry phase = phases[i];
                bool isValidPhaseId = (phase.PhaseIndex >= 1) &&
                                      (phase.PhaseIndex <= REQUIRED_BOSS_PHASE_COUNT);
                if (!isValidPhaseId)
                {
                    failures.Add($"보스 페이즈 id가 범위를 벗어났다: {phase.PhaseIndex}");
                }

                if (!seenPhaseIds.Add(phase.PhaseIndex))
                {
                    failures.Add($"보스 페이즈 id가 중복되었다: {phase.PhaseIndex}");
                }
                else if (isValidPhaseId)
                {
                    phaseById.Add(phase.PhaseIndex, phase);
                }

                Range($"보스 페이즈 {phase.PhaseIndex} 진입 HP 비율", phase.EnterHealthRatio, 0f, 1f, failures);
                if (IsFinite(phase.EnterHealthRatio) && (phase.EnterHealthRatio <= 0f))
                {
                    failures.Add($"보스 페이즈 {phase.PhaseIndex} 진입 HP 비율이 양수가 아니다");
                }

                int[] patternIds = phase.PatternIds;
                if ((patternIds == null) || (patternIds.Length == 0))
                {
                    failures.Add($"보스 페이즈 {phase.PhaseIndex}의 패턴 id가 비어 있다");
                    continue;
                }

                HashSet<int> seenPatternIds = new HashSet<int>();
                bool hasParryablePattern = false;
                for (int j = 0; j < patternIds.Length; ++j)
                {
                    int patternId = patternIds[j];
                    if (!seenPatternIds.Add(patternId))
                    {
                        failures.Add($"보스 페이즈 {phase.PhaseIndex}의 패턴 id가 중복되었다: {patternId}");
                    }

                    BossPatternSO.PatternEntry pattern;
                    if (!patternById.TryGetValue(patternId, out pattern))
                    {
                        failures.Add($"보스 페이즈 {phase.PhaseIndex}가 없는 패턴 id를 참조한다: {patternId}");
                    }
                    else if (pattern.IsParryable)
                    {
                        hasParryablePattern = true;
                    }
                }

                if (!hasParryablePattern)
                {
                    failures.Add($"보스 페이즈 {phase.PhaseIndex}에 패링 가능한 패턴이 없다");
                }
            }

            for (int phaseId = 1; phaseId <= REQUIRED_BOSS_PHASE_COUNT; ++phaseId)
            {
                if (!phaseById.ContainsKey(phaseId))
                {
                    failures.Add($"보스 페이즈 id {phaseId}가 없다");
                }
            }

            BossPatternSO.PhaseEntry phaseOne;
            BossPatternSO.PhaseEntry phaseTwo;
            if (phaseById.TryGetValue(1, out phaseOne) && phaseById.TryGetValue(2, out phaseTwo) &&
                IsFinite(phaseOne.EnterHealthRatio) && IsFinite(phaseTwo.EnterHealthRatio) &&
                (phaseTwo.EnterHealthRatio >= phaseOne.EnterHealthRatio))
            {
                failures.Add("보스 페이즈 진입 HP 비율이 내림차순이 아니다");
            }
        }

        private static void ValidateGaugeReductions(
            BossPatternSO boss,
            Dictionary<int, BossPatternSO.PatternEntry> patternById,
            List<string> failures)
        {
            IReadOnlyList<BossPatternSO.GaugeReductionEntry> reductions = boss.GaugeReductions;
            if ((reductions == null) || (reductions.Count == 0))
            {
                failures.Add("보스 직업별 게이지 감소 배열이 비어 있다");
                return;
            }

            HashSet<CharacterKind> seenCharacters = new HashSet<CharacterKind>();
            for (int i = 0; i < reductions.Count; ++i)
            {
                BossPatternSO.GaugeReductionEntry reduction = reductions[i];
                if (!Enum.IsDefined(typeof(CharacterKind), reduction.Character))
                {
                    failures.Add($"보스 게이지 감소 직업 id가 유효하지 않다: {reduction.Character}");
                }

                if (!seenCharacters.Add(reduction.Character))
                {
                    failures.Add($"보스 게이지 감소 직업이 중복되었다: {reduction.Character}");
                }

                PositiveFinite($"보스 {reduction.Character} 게이지 감소량", reduction.Amount, failures);
                int[] patternIds = reduction.CounterPatternIds;
                if ((patternIds == null) || (patternIds.Length == 0))
                {
                    failures.Add($"보스 {reduction.Character}의 카운터 패턴 id가 비어 있다");
                    continue;
                }

                HashSet<int> seenPatternIds = new HashSet<int>();
                for (int j = 0; j < patternIds.Length; ++j)
                {
                    int patternId = patternIds[j];
                    if (!seenPatternIds.Add(patternId))
                    {
                        failures.Add($"보스 {reduction.Character}의 카운터 패턴 id가 중복되었다: {patternId}");
                    }

                    if (!patternById.ContainsKey(patternId))
                    {
                        failures.Add($"보스 {reduction.Character}가 없는 카운터 패턴 id를 참조한다: {patternId}");
                    }
                }
            }

            CharacterKind[] requiredCharacters =
            {
                CharacterKind.Chasa,
                CharacterKind.Singung,
                CharacterKind.Munyeo,
            };
            for (int i = 0; i < requiredCharacters.Length; ++i)
            {
                if (!seenCharacters.Contains(requiredCharacters[i]))
                {
                    failures.Add($"보스 게이지 감소 데이터에 {requiredCharacters[i]}가 없다");
                }
            }
        }

        /// <summary> 출시 경로 씬을 Main 프로필로, 개발 씬을 DevScene 프로필로 분류한다 </summary>
        public static SceneProfile ResolveSceneProfile(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                return SceneProfile.DevScene;
            }

            string name = sceneName.Replace('\\', '/');
            int slashIndex = name.LastIndexOf('/');
            if (slashIndex >= 0)
            {
                name = name.Substring(slashIndex + 1);
            }

            if (name.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - ".unity".Length);
            }

            if ((name == Constants.Scene.BOOT) ||
                (name == Constants.Scene.TITLE) ||
                (name == Constants.Scene.LOBBY) ||
                (name == Constants.Scene.FIELD) ||
                (name == Constants.Scene.BOSS_ENTRANCE) ||
                (name == Constants.Scene.BOSS_ARENA) ||
                (name == Constants.Scene.RESULT) ||
                (name == Constants.Scene.MAIN))
            {
                return SceneProfile.Main;
            }

            return SceneProfile.DevScene;
        }

        /// <summary> 현재 프로필에서 서버에 필요한 씬 구성요소를 검사한다 </summary>
        public static void ValidateScene(SceneProfile profile, List<string> failures)
        {
            if (failures == null)
            {
                return;
            }

            if ((profile != SceneProfile.Main) && (profile != SceneProfile.DevScene))
            {
                failures.Add($"알 수 없는 씬 프로필이다: {profile}");
                return;
            }

            RequireInScene<GeumeumNetworkManager>("GeumeumNetworkManager", failures);
            RequireInScene<GameDBBootstrap>("GameDBBootstrap", failures);
            RequireInScene<GameEventBus>("GameEventBus", failures);
            RequireInScene<AuthorityTickScheduler>("AuthorityTickScheduler", failures);

            if (profile == SceneProfile.DevScene)
            {
                return;
            }

            RequireInScene<LobbyController>("LobbyController", failures);
            RequireInScene<RunManager>("RunManager", failures);
            RequireInScene<LobbyRoster>("LobbyRoster", failures);
            RequireInScene<LobbyRoleCoordinator>("LobbyRoleCoordinator", failures);
            RequireSessionServices(failures);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 프레임 측정기는 개발/에디터에서만 출시 씬 검사를 보강한다
            RequireInScene<FrameRecorder>("FrameRecorder", failures);
#endif
        }

        private static void RequireSessionServices(List<string> failures)
        {
            // 서버 시작 전 1회 검사다. Network는 Systems 구체 타입 대신 Common 계약을 검사한다
            MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include);
            bool hasGrowth = false;
            bool hasWallet = false;
            bool hasStatistics = false;
            bool hasResultFacts = false;
            bool hasResultScore = false;

            for (int i = 0; i < behaviours.Length; ++i)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (!IsUsableSessionService(behaviour))
                {
                    continue;
                }

                hasGrowth |= (behaviour is IGrowthProvider);
                hasWallet |= (behaviour is IPartyWallet);
                hasStatistics |= (behaviour is IRunStatisticsSink);
                hasResultFacts |= (behaviour is IRunResultFactsProvider);
                hasResultScore |= (behaviour is IRunResultProvider);
            }

            if (!hasGrowth)
            {
                failures.Add("씬에 파티 성장 공급자(IGrowthProvider)가 없다");
            }

            if (!hasWallet)
            {
                failures.Add("씬에 파티 지갑(IPartyWallet)이 없다");
            }

            if ((!hasStatistics) || (!hasResultFacts) || (!hasResultScore))
            {
                failures.Add("씬에 런 통계, 결과 사실 또는 점수 공급자가 없다");
            }
        }

        private static bool IsUsableSessionService(MonoBehaviour behaviour)
        {
            if ((behaviour == null) || (!behaviour.enabled))
            {
                return false;
            }

#if UNITY_EDITOR
            Transform tagged = behaviour.transform;
            while (tagged != null)
            {
                if (tagged.CompareTag(EDITOR_ONLY_TAG))
                {
                    return false;
                }

                tagged = tagged.parent;
            }
#endif

            if (behaviour.gameObject.activeInHierarchy)
            {
                return true;
            }

            // Mirror disables spawnable scene identities during scene post-processing,
            // before NetworkManager.OnStartServer validates and spawns them
            Mirror.NetworkIdentity identity =
                behaviour.GetComponentInParent<Mirror.NetworkIdentity>(true);
            if ((identity == null) || (!identity.enabled) || (identity.sceneId == 0) ||
                identity.gameObject.activeSelf || (!identity.gameObject.scene.IsValid()) ||
                (!identity.gameObject.scene.isLoaded))
            {
                return false;
            }

            // Only the scene identity itself may be inactive. Disabled children or
            // parents still represent intentionally inactive service objects
            Transform current = behaviour.transform;
            while (current != identity.transform)
            {
                if ((current == null) || (!current.gameObject.activeSelf))
                {
                    return false;
                }

                current = current.parent;
            }

            current = identity.transform.parent;
            while (current != null)
            {
                if (!current.gameObject.activeSelf)
                {
                    return false;
                }

                current = current.parent;
            }

            return true;
        }

        private static void RequireInScene<T>(string label, List<string> failures)
            where T : Component
        {
            T found = UnityEngine.Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
            if (found == null)
            {
                failures.Add($"씬에 {label}가 없다");
                return;
            }

#if UNITY_EDITOR
            Transform current = found.transform;
            while (current != null)
            {
                if (current.CompareTag(EDITOR_ONLY_TAG))
                {
                    failures.Add($"씬의 {label}가 EditorOnly 오브젝트 아래에 있다");
                    break;
                }

                current = current.parent;
            }
#endif
        }

        private static bool IsFinite(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }

        private static void PositiveFinite(string label, float value, List<string> failures)
        {
            if ((!IsFinite(value)) || (value <= 0f))
            {
                failures.Add($"{label}이 유한한 양수가 아니다: {value}");
            }
        }

        private static void NonNegativeFinite(string label, float value, List<string> failures)
        {
            if ((!IsFinite(value)) || (value < 0f))
            {
                failures.Add($"{label}이 유한한 음이 아닌 값이 아니다: {value}");
            }
        }

        private static void Range(
            string label,
            float value,
            float minimum,
            float maximum,
            List<string> failures)
        {
            if ((!IsFinite(value)) || (value < minimum) || (value > maximum))
            {
                failures.Add($"{label}이 유한한 범위를 벗어났다: {value}");
            }
        }
    }
}
