// System
using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Systems.Tests
{
    public sealed class RunStatisticsTests
    {
        private const BindingFlags PRIVATE_INSTANCE =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private GameObject _host;
        private RunStatistics _statistics;
        private ScoreRuleSO _scoreRule;
        private WalletStub _wallet;

        [SetUp]
        public void SetUp()
        {
            RunStatisticsSinkLocator.ResetStatic();
            RunResultFactsProviderLocator.ResetStatic();
            RunResultProviderLocator.ResetStatic();
            RunResultParticipantSourceLocator.ResetStatic();
            PartyWalletLocator.ResetStatic();

            _host = new GameObject("RunStatisticsTests", typeof(Mirror.NetworkIdentity));
            _statistics = _host.AddComponent<RunStatistics>();
            _scoreRule = ScriptableObject.CreateInstance<ScoreRuleSO>();
            Set(_statistics, "_scoreRule", _scoreRule);

            _wallet = new WalletStub(321);
            PartyWalletLocator.Register(_wallet);
        }

        [TearDown]
        public void TearDown()
        {
            PartyWalletLocator.ResetStatic();
            RunResultParticipantSourceLocator.ResetStatic();
            RunResultProviderLocator.ResetStatic();
            RunResultFactsProviderLocator.ResetStatic();
            RunStatisticsSinkLocator.ResetStatic();

            if (_scoreRule != null)
            {
                UnityEngine.Object.DestroyImmediate(_scoreRule);
            }

            if (_host != null)
            {
                UnityEngine.Object.DestroyImmediate(_host);
            }
        }

        [Test]
        public void BeginAndFreezeCaptureUntouchedLiveParticipant()
        {
            RunId run = new RunId(1u);
            ParticipantSourceStub source = new ParticipantSourceStub(
                new ParticipantId(10UL), CharacterKind.Chasa, 100f);
            RunResultParticipantSourceLocator.Register(source);

            Assert.IsTrue(_statistics.BeginRun(run));

            RunResultSnapshot result = Header(run);
            Assert.IsTrue(_statistics.TryFreeze(ref result));

            Assert.AreEqual(RunResultFactsState.Authoritative, result.FactsState);
            Assert.AreEqual(321, result.RemainingCurrency);
            Assert.AreEqual(100f, result.RemainingHealthSum);
            Assert.AreEqual(1, result.Participants.Length);
            Assert.AreEqual(10UL, result.Participants[0].ParticipantIdValue);
            Assert.AreEqual(CharacterKind.Chasa, result.Participants[0].Character);
            Assert.AreEqual(RunPartyKind.HumansOnly, result.PartyKind);
        }

        [Test]
        public void SameNumberHumanAndBotKeepSeparateDeathsHealingAndResults()
        {
            RunId run = new RunId(1u);
            var human = new ParticipantId(10UL);
            var bot = new ParticipantId(ParticipantKind.Bot, 10UL);
            RegisterSource(human, CharacterKind.Chasa, 80f);
            RegisterSource(bot, CharacterKind.Munyeo, 60f);
            Assert.IsTrue(_statistics.BeginRun(run));
            Assert.IsTrue(_statistics.RecordLife(RunPlayerLifeFact.Death(run, human, 1u)));
            Assert.IsTrue(_statistics.RecordLife(RunPlayerLifeFact.Death(run, bot, 1u)));
            Assert.IsTrue(_statistics.RecordHealing(new RunAppliedHealingFact(run, bot, human, 11f)));
            RunResultSnapshot result = Header(run);
            Assert.IsTrue(_statistics.TryFreeze(ref result));
            Assert.AreEqual(2, result.Participants.Length);
            Assert.AreEqual(human, result.Participants[0].Participant);
            Assert.AreEqual(bot, result.Participants[1].Participant);
            Assert.AreEqual(11f, result.Participants[1].HealingDone);
            Assert.AreEqual(2, result.DeathCount);
            Assert.AreEqual(RunPartyKind.BotAssisted, result.PartyKind);
        }

        [Test]
        public void ActualFactsExcludeSelfHealingAndRejectDuplicateKeys()
        {
            RunId run = new RunId(2u);
            ParticipantId first = new ParticipantId(10UL);
            ParticipantId second = new ParticipantId(20UL);
            RegisterSource(first, CharacterKind.Chasa, 80f);
            RegisterSource(second, CharacterKind.Munyeo, 70f);
            Assert.IsTrue(_statistics.BeginRun(run));

            TargetRef target = TargetRef.Create(run, new SceneRevision(3u), 50u,
                TargetRef.BODY_PART_ID);
            Assert.IsTrue(_statistics.RecordDamage(new RunAppliedDamageFact(
                run, first, target, 17f)));
            Assert.IsTrue(_statistics.RecordHealing(new RunAppliedHealingFact(
                run, second, first, 11f)));
            Assert.IsTrue(_statistics.RecordHealing(new RunAppliedHealingFact(
                run, second, second, 9f)));

            RunPlayerLifeFact death = RunPlayerLifeFact.Death(run, first, 4u);
            RunPlayerLifeFact rescue = RunPlayerLifeFact.Rescue(run, second, first, 4u);
            Assert.IsTrue(_statistics.RecordLife(death));
            Assert.IsFalse(_statistics.RecordLife(death));
            Assert.IsTrue(_statistics.RecordLife(rescue));
            Assert.IsFalse(_statistics.RecordLife(rescue));

            RunCounterFact resonance = new RunCounterFact(run, RunCounterKind.Resonance,
                second, target, 8u, 25f);
            Assert.IsTrue(_statistics.RecordCounter(resonance));
            Assert.IsFalse(_statistics.RecordCounter(resonance));

            EnemyDiedEvent enemy = EnemyDiedEvent.ForEnemy(99u, EnemyKind.Heokkaebi,
                Vector3.zero, 10u, 0, 1f);
            Assert.IsTrue(_statistics.RecordEnemyDeath(enemy));
            Assert.IsFalse(_statistics.RecordEnemyDeath(enemy));

            RunResultSnapshot result = Header(run);
            Assert.IsTrue(_statistics.TryFreeze(ref result));

            RunParticipantResultSnapshot firstResult = Find(result, first);
            RunParticipantResultSnapshot secondResult = Find(result, second);
            Assert.AreEqual(17f, firstResult.DamageDealt);
            Assert.AreEqual(1, firstResult.DeathCount);
            Assert.AreEqual(11f, secondResult.HealingDone);
            Assert.AreEqual(1, secondResult.RescueCount);
            Assert.AreEqual(1, secondResult.ResonanceCounterCount);
            Assert.AreEqual(25f, secondResult.CounterReductionTotal);
            Assert.AreEqual(1, result.EnemyKills);
            Assert.AreEqual(1, result.DeathCount);
        }

        [Test]
        public void UndefinedLifeAndCounterKindsAreRejectedWithoutContribution()
        {
            RunId run = new RunId(7u);
            ParticipantId actor = new ParticipantId(70UL);
            ParticipantId targetParticipant = new ParticipantId(71UL);
            RegisterSource(actor, CharacterKind.Chasa, 100f);
            RegisterSource(targetParticipant, CharacterKind.Munyeo, 100f);
            Assert.IsTrue(_statistics.BeginRun(run));

            RunPlayerLifeFact life = new RunPlayerLifeFact(run,
                (RunPlayerLifeFactKind) 99, actor, targetParticipant, 1u);
            TargetRef target = TargetRef.Create(run, new SceneRevision(1u), 72u,
                TargetRef.BODY_PART_ID);
            RunCounterFact counter = new RunCounterFact(run, (RunCounterKind) 99,
                actor, target, 1u, 10f);

            Assert.IsFalse(life.IsValid);
            Assert.IsFalse(counter.IsValid);
            Assert.IsFalse(_statistics.RecordLife(life));
            Assert.IsFalse(_statistics.RecordCounter(counter));

            RunResultSnapshot result = Header(run);
            Assert.IsTrue(_statistics.TryFreeze(ref result));
            RunParticipantResultSnapshot actorResult = Find(result, actor);
            Assert.AreEqual(0, actorResult.RescueCount);
            Assert.AreEqual(0, actorResult.SwallowedRescueCount);
            Assert.AreEqual(0, actorResult.ParryCounterCount);
            Assert.AreEqual(0, actorResult.PrecisionCounterCount);
            Assert.AreEqual(0, actorResult.ResonanceCounterCount);
            Assert.AreEqual(0f, actorResult.CounterReductionTotal);
        }

        [Test]
        public void DisconnectKeepsLastHealthAndContribution()
        {
            RunId run = new RunId(3u);
            ParticipantId participant = new ParticipantId(30UL);
            ParticipantSourceStub source = RegisterSource(participant, CharacterKind.Singung, 100f);
            Assert.IsTrue(_statistics.BeginRun(run));

            TargetRef target = TargetRef.Create(run, new SceneRevision(2u), 60u,
                TargetRef.BODY_PART_ID);
            Assert.IsTrue(_statistics.RecordDamage(new RunAppliedDamageFact(
                run, participant, target, 12f)));
            Assert.IsTrue(_statistics.RecordParticipantState(
                new RunParticipantStateFact(run, participant, 88f)));
            RunResultParticipantSourceLocator.Unregister(source);

            RunResultSnapshot result = Header(run);
            Assert.IsTrue(_statistics.TryFreeze(ref result));

            Assert.AreEqual(88f, result.RemainingHealthSum);
            Assert.AreEqual(12f, result.Participants[0].DamageDealt);
            Assert.AreEqual(CharacterKind.Singung, result.Participants[0].Character);
        }

        [Test]
        public void FrozenResultRejectsLateFactsAndReturnsSameSnapshot()
        {
            RunId run = new RunId(4u);
            ParticipantId participant = new ParticipantId(40UL);
            ParticipantSourceStub source = RegisterSource(participant, CharacterKind.Chasa, 60f);
            Assert.IsTrue(_statistics.BeginRun(run));

            RunResultSnapshot first = Header(run);
            Assert.IsTrue(_statistics.TryFreeze(ref first));

            source.Health = 1f;
            Assert.IsFalse(_statistics.RecordParticipantState(
                new RunParticipantStateFact(run, participant, 1f)));

            RunResultSnapshot second = Header(run);
            Assert.IsTrue(_statistics.TryFreeze(ref second));
            Assert.AreEqual(first.ResultRevision, second.ResultRevision);
            Assert.AreEqual(60f, second.RemainingHealthSum);
        }

        [Test]
        public void SecondRunStartsWithIndependentCountersAndRevision()
        {
            ParticipantId participant = new ParticipantId(50UL);
            ParticipantSourceStub source = RegisterSource(participant, CharacterKind.Chasa, 90f);
            RunId firstRun = new RunId(5u);
            Assert.IsTrue(_statistics.BeginRun(firstRun));

            TargetRef firstTarget = TargetRef.Create(firstRun, new SceneRevision(1u), 70u,
                TargetRef.BODY_PART_ID);
            Assert.IsTrue(_statistics.RecordDamage(new RunAppliedDamageFact(
                firstRun, participant, firstTarget, 30f)));
            RunResultSnapshot first = Header(firstRun);
            Assert.IsTrue(_statistics.TryFreeze(ref first));

            _statistics.ResetRun(firstRun);
            source.Health = 100f;
            RunId secondRun = new RunId(6u);
            Assert.IsTrue(_statistics.BeginRun(secondRun));
            Assert.IsFalse(_statistics.RecordDamage(new RunAppliedDamageFact(
                firstRun, participant, firstTarget, 5f)));

            RunResultSnapshot second = Header(secondRun);
            Assert.IsTrue(_statistics.TryFreeze(ref second));

            Assert.AreEqual(0f, second.Participants[0].DamageDealt);
            Assert.AreEqual(100f, second.RemainingHealthSum);
            Assert.Greater(second.ResultRevision, first.ResultRevision);
        }

        [Test]
        public void ScoreUsesCombinedLimitAndClampsOverflow()
        {
            Set(_scoreRule, "_evaluationGrades", new[]
            {
                new ScoreRuleSO.EvaluationGradeEntry
                {
                    Grade = RunEvaluationGrade.C,
                    MinimumScore = 0,
                    Message = "C",
                },
                new ScoreRuleSO.EvaluationGradeEntry
                {
                    Grade = RunEvaluationGrade.S,
                    MinimumScore = 10000,
                    Message = "S",
                },
            });

            RunResultSnapshot facts = new RunResultSnapshot
            {
                FactsState          = RunResultFactsState.Authoritative,
                BossKilled          = true,
                FieldElapsedSeconds = 1800f,
                BossElapsedSeconds  = 600f,
                ScoreTimeLimitSeconds = 2640f,
                RemainingCurrency   = 100,
                RemainingHealthSum  = 150f,
            };

            int score = _statistics.CalculateScore(facts);
            Assert.AreEqual(_scoreRule.CalculateFinalScore(true, 0, 240f, 100, 150f, 0), score);
            Assert.AreEqual(RunEvaluationGrade.S, _statistics.CalculateGrade(score));
            Assert.AreEqual("S", _statistics.GetEvaluationMessage(RunEvaluationGrade.S));
            Assert.IsTrue(_scoreRule.ValidateEvaluationGrades(out string validationFailure),
                validationFailure);
            Assert.AreEqual(int.MaxValue, _scoreRule.CalculateFinalScore(
                false, 0, 0f, int.MaxValue, 0f, 0));
        }

        [Test]
        public void LocalStoreKeepsDistinctRunsAndDeduplicatesSameRevision()
        {
            string directory = Path.Combine(".claude", "docs", "2026-09-09", "tests",
                Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, RunLeaderboardStore.FILE_NAME);

            try
            {
                RunResultSnapshot first = EligibleRecord(11u, 1u);
                RunResultSnapshot second = EligibleRecord(12u, 2u);

                Assert.IsTrue(RunLeaderboardStore.TryStore(path, first, out string firstFailure),
                    firstFailure);
                Assert.IsTrue(RunLeaderboardStore.TryStore(path, first, out string duplicateFailure),
                    duplicateFailure);
                Assert.IsTrue(RunLeaderboardStore.TryStore(path, second, out string secondFailure),
                    secondFailure);
                Assert.IsTrue(RunLeaderboardStore.TryLoadResults(path,
                    out RunResultSnapshot[] records, out string loadFailure), loadFailure);
                Assert.AreEqual(2, records.Length);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        [Test]
        public void LegacyRecordsRemainUnknownAndBotRecordsCanBeQueriedSeparately()
        {
            string directory = Path.Combine(".claude", "docs", "2026-09-19", "tests",
                Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, RunLeaderboardStore.FILE_NAME);
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(path, "{\"SchemaVersion\":1,\"Records\":[{\"Participants\":[{\"ParticipantIdValue\":10}]}]}");
                Assert.IsTrue(RunLeaderboardStore.TryLoadResults(path, out var legacy, out _));
                Assert.AreEqual(RunPartyKind.UnknownLegacy, legacy[0].PartyKind);
                Assert.AreEqual(new ParticipantId(10UL), legacy[0].Participants[0].Participant);
                var record = EligibleRecord(99u, 1u);
                record.PartyKind = RunPartyKind.BotAssisted;
                record.Participants = new[] { new RunParticipantResultSnapshot
                {
                    ParticipantKind = ParticipantKind.Bot, ParticipantIdValue = 10UL,
                } };
                Assert.IsTrue(RunLeaderboardStore.TryStore(path, record, out string failure), failure);
                Assert.IsTrue(RunLeaderboardStore.TryLoadResults(path, RunPartyKind.BotAssisted,
                    out var bots, out _));
                Assert.AreEqual(1, bots.Length);
                Assert.AreEqual(new ParticipantId(ParticipantKind.Bot, 10UL), bots[0].Participants[0].Participant);
                Assert.IsTrue(RunLeaderboardStore.TryLoadResults(path, RunPartyKind.HumansOnly,
                    out var humans, out _));
                Assert.IsEmpty(humans);
                Assert.IsTrue(RunLeaderboardStore.TryLoadResults(path, RunPartyKind.UnknownLegacy,
                    out var old, out _));
                Assert.AreEqual(1, old.Length);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        [Test]
        public void LocalStoreKeepsSameRunAndRevisionFromDifferentSessions()
        {
            string directory = Path.Combine(".claude", "docs", "2026-09-09", "tests",
                Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, RunLeaderboardStore.FILE_NAME);

            try
            {
                RunResultSnapshot first = EligibleRecord(11u, 1u);
                RunResultSnapshot second = first;
                second.SessionIdValue = 78UL;

                Assert.IsTrue(RunLeaderboardStore.TryStore(path, first, out string firstFailure),
                    firstFailure);
                Assert.IsTrue(RunLeaderboardStore.TryStore(path, second, out string secondFailure),
                    secondFailure);
                Assert.IsTrue(RunLeaderboardStore.TryLoadResults(path,
                    out RunResultSnapshot[] records, out string loadFailure), loadFailure);
                Assert.AreEqual(2, records.Length);
                Assert.AreNotEqual(records[0].SessionIdValue, records[1].SessionIdValue);
                Assert.AreEqual(records[0].RunIdValue, records[1].RunIdValue);
                Assert.AreEqual(records[0].ResultRevision, records[1].ResultRevision);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        [Test]
        public void LocalStoreAcceptsAuthoritativeZeroPointD()
        {
            string directory = Path.Combine(".claude", "docs", "2026-09-09", "tests",
                Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, RunLeaderboardStore.FILE_NAME);

            try
            {
                RunResultSnapshot result = EligibleRecord(13u, 3u);

                Assert.AreEqual(0, result.Score);
                Assert.AreEqual(RunEvaluationGrade.D, result.EvaluationGrade);
                Assert.IsTrue(result.HasAuthoritativeScore);
                Assert.IsTrue(RunLeaderboardStore.TryStore(path, result, out string failure),
                    failure);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        [Test]
        public void ScoreBoundaryRejectsProviderThatDisappearedAndPreservesFacts()
        {
            GameObject runHost = new GameObject("RunScoreProviderLossTests",
                typeof(Mirror.NetworkIdentity), typeof(RunManager));
            string directory = Path.Combine(".claude", "docs", "2026-09-09", "tests",
                Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, RunLeaderboardStore.FILE_NAME);

            try
            {
                ScoreProviderStub provider = new ScoreProviderStub(
                    2, 0, RunEvaluationGrade.D, "D");
                RunResultProviderLocator.Register(provider);
                RunResultProviderLocator.Unregister(provider);

                RunResultSnapshot result = EligibleRecord(14u, 4u);
                RunParticipantResultSnapshot[] participants = result.Participants;
                LogAssert.Expect(LogType.Error, new Regex(
                    "^\\[RunManager\\] 결과 확정 시 점수 provider가 없다:"));
                result = ApplyScore(runHost.GetComponent<RunManager>(), result,
                    RunOutcome.Victory);

                Assert.AreEqual(RunResultFactsState.Authoritative, result.FactsState);
                Assert.AreSame(participants, result.Participants);
                Assert.AreEqual(0, result.ScoreRuleVersion);
                Assert.AreEqual(0, result.Score);
                Assert.AreEqual(RunEvaluationGrade.None, result.EvaluationGrade);
                Assert.AreEqual("점수 설정 오류로 이번 런의 점수와 등급을 확정하지 못했다.",
                    result.EvaluationMessage);
                Assert.IsFalse(result.HasAuthoritativeScore);
                Assert.IsFalse(RunLeaderboardStore.TryStore(path, result, out string failure));
                Assert.AreEqual("점수 규칙 판이 없는 승리 결과는 기록할 수 없다", failure);
                Assert.IsFalse(File.Exists(path));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(runHost);

                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        [Test]
        public void ScoreBoundaryRejectsNonPositiveVersion()
        {
            GameObject runHost = new GameObject("RunScoreVersionTests",
                typeof(Mirror.NetworkIdentity), typeof(RunManager));

            try
            {
                RunResultProviderLocator.Register(new ScoreProviderStub(
                    0, 0, RunEvaluationGrade.D, "D"));
                RunResultSnapshot result = EligibleRecord(15u, 5u);

                LogAssert.Expect(LogType.Error, new Regex(
                    "^\\[RunManager\\] 점수 규칙 판이 유효하지 않다:"));
                result = ApplyScore(runHost.GetComponent<RunManager>(), result,
                    RunOutcome.Victory);

                Assert.AreEqual(RunResultFactsState.Authoritative, result.FactsState);
                Assert.AreEqual(0, result.ScoreRuleVersion);
                Assert.IsFalse(result.HasAuthoritativeScore);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(runHost);
            }
        }

        [Test]
        public void ScoreBoundaryRejectsUndefinedVictoryGrade()
        {
            GameObject runHost = new GameObject("RunScoreGradeTests",
                typeof(Mirror.NetworkIdentity), typeof(RunManager));
            string directory = Path.Combine(".claude", "docs", "2026-09-09", "tests",
                Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, RunLeaderboardStore.FILE_NAME);

            try
            {
                RunResultProviderLocator.Register(new ScoreProviderStub(
                    2, 0, (RunEvaluationGrade) 99, "잘못된 등급"));
                RunResultSnapshot result = EligibleRecord(16u, 6u);

                LogAssert.Expect(LogType.Error, new Regex(
                    "^\\[RunManager\\] 승리 점수 등급이 유효하지 않다:"));
                result = ApplyScore(runHost.GetComponent<RunManager>(), result,
                    RunOutcome.Victory);

                Assert.AreEqual(RunResultFactsState.Authoritative, result.FactsState);
                Assert.AreEqual(2, result.ScoreRuleVersion);
                Assert.AreEqual(RunEvaluationGrade.None, result.EvaluationGrade);
                Assert.IsFalse(result.HasAuthoritativeScore);
                Assert.IsFalse(RunLeaderboardStore.TryStore(path, result, out string failure));
                Assert.AreEqual("유효한 승리 등급이 없는 결과는 기록할 수 없다", failure);
                Assert.IsFalse(File.Exists(path));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(runHost);

                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        [Test]
        public void LocalStoreSerializesProcessLocalTasksWithoutLosingResults()
        {
            string directory = Path.Combine(".claude", "docs", "2026-09-09", "tests",
                Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, RunLeaderboardStore.FILE_NAME);
            const int writerCount = 3;
            const int recordsPerWriter = 4;
            string[] failures = new string[writerCount * recordsPerWriter];

            try
            {
                using (ManualResetEventSlim startGate = new ManualResetEventSlim(false))
                {
                    Task[] writers = new Task[writerCount];
                    for (int writer = 0; writer < writerCount; ++writer)
                    {
                        int capturedWriter = writer;
                        writers[writer] = Task.Run(() =>
                        {
                            startGate.Wait();
                            for (int record = 0; record < recordsPerWriter; ++record)
                            {
                                int index = (capturedWriter * recordsPerWriter) + record;
                                RunResultSnapshot result = EligibleRecord(
                                    (uint) (100 + index), (uint) (1000 + index));
                                if (!RunLeaderboardStore.TryStore(path, result,
                                    out string failure))
                                {
                                    failures[index] = failure;
                                }
                            }
                        });
                    }

                    startGate.Set();
                    Assert.DoesNotThrow(() => Task.WaitAll(writers));
                }

                for (int i = 0; i < failures.Length; ++i)
                {
                    Assert.IsNull(failures[i], failures[i]);
                }

                Assert.IsTrue(RunLeaderboardStore.TryLoadResults(path,
                    out RunResultSnapshot[] records, out string loadFailure), loadFailure);
                Assert.AreEqual(writerCount * recordsPerWriter, records.Length);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        [Test]
        public void RecorderParsesDevelopmentRecordPathArgument()
        {
            string expected = Path.Combine("isolated m2", RunLeaderboardStore.FILE_NAME);
            Assert.IsTrue(RunResultRecorder.TryParseRecordPathArgument(new[]
            {
                "Geumeum.exe",
                "-result-record-path",
                expected,
            }, out string actual));
            Assert.AreEqual(expected, actual);

            Assert.IsFalse(RunResultRecorder.TryParseRecordPathArgument(new[]
            {
                "Geumeum.exe",
                "-result-record-path",
            }, out _));
        }

        [Test]
        public void RecorderKeepsFailedResultAndRetriesWithoutDuplicate()
        {
            string directory = Path.Combine(".claude", "docs", "2026-09-09", "tests",
                Guid.NewGuid().ToString("N"));
            string blockingFile = Path.Combine(directory, "not-a-directory");
            string validPath = Path.Combine(directory, RunLeaderboardStore.FILE_NAME);
            GameObject recorderHost = null;

            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(blockingFile, "block");
                recorderHost = new GameObject("RunResultRecorderTests",
                    typeof(Mirror.NetworkIdentity), typeof(Geumeum.Network.RunManager),
                    typeof(RunResultRecorder));
                RunResultRecorder recorder = recorderHost.GetComponent<RunResultRecorder>();
                Set(recorder, "_recordPathOverride",
                    Path.Combine(blockingFile, RunLeaderboardStore.FILE_NAME));

                LogAssert.Expect(LogType.Error, new Regex(
                    "^\\[RunResultRecorder\\] 리더보드 기록 디렉터리를 준비하지 못했다:"));
                recorder.GetType().GetMethod("HandleResultChanged", PRIVATE_INSTANCE)
                    .Invoke(recorder, new object[] { EligibleRecord(21u, 1u) });
                Assert.IsTrue(recorder.HasPendingRecord);

                Set(recorder, "_recordPathOverride", validPath);
                Assert.IsTrue(recorder.TryFlushPending(), recorder.LastFailure);
                Assert.IsFalse(recorder.HasPendingRecord);
                Assert.IsTrue(RunLeaderboardStore.TryLoadResults(validPath,
                    out RunResultSnapshot[] records, out string loadFailure), loadFailure);
                Assert.AreEqual(1, records.Length);
            }
            finally
            {
                if (recorderHost != null)
                {
                    UnityEngine.Object.DestroyImmediate(recorderHost);
                }

                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        private ParticipantSourceStub RegisterSource(ParticipantId participant,
            CharacterKind character, float health)
        {
            ParticipantSourceStub source = new ParticipantSourceStub(participant, character, health);
            RunResultParticipantSourceLocator.Register(source);
            return source;
        }

        private static RunResultSnapshot Header(RunId run)
        {
            return new RunResultSnapshot
            {
                SessionIdValue      = 77UL,
                RunIdValue          = run.Value,
                Mode                = RunMode.Standard,
                Outcome             = RunOutcome.Victory,
                BossKilled          = true,
                ScoreTimeLimitSeconds = 2640f,
            };
        }

        private static RunResultSnapshot EligibleRecord(uint runId, uint revision)
        {
            RunResultSnapshot result = Header(new RunId(runId));
            result.ResultRevision   = revision;
            result.FactsState       = RunResultFactsState.Authoritative;
            result.Participants     = Array.Empty<RunParticipantResultSnapshot>();
            result.ScoreRuleVersion = 2;
            result.Score            = 0;
            result.EvaluationGrade  = RunEvaluationGrade.D;
            return result;
        }

        private static RunResultSnapshot ApplyScore(RunManager runManager,
            RunResultSnapshot snapshot, RunOutcome outcome)
        {
            object[] arguments = { snapshot, outcome };
            runManager.GetType().GetMethod("ApplyScore", PRIVATE_INSTANCE)
                .Invoke(runManager, arguments);
            return (RunResultSnapshot) arguments[0];
        }

        private static RunParticipantResultSnapshot Find(in RunResultSnapshot result,
            ParticipantId participant)
        {
            for (int i = 0; i < result.Participants.Length; ++i)
            {
                if (result.Participants[i].ParticipantIdValue == participant.Value)
                {
                    return result.Participants[i];
                }
            }

            Assert.Fail($"참가자 결과가 없다: {participant}");
            return default;
        }

        private static void Set(object target, string fieldName, object value)
        {
            target.GetType().GetField(fieldName, PRIVATE_INSTANCE).SetValue(target, value);
        }

        private sealed class ParticipantSourceStub : IRunResultParticipantSource
        {
            private readonly ParticipantId _participant;
            private readonly CharacterKind _character;

            public float Health;

            public ParticipantSourceStub(ParticipantId participant, CharacterKind character,
                float health)
            {
                _participant = participant;
                _character   = character;
                Health       = health;
            }

            public bool TryCaptureResultState(RunId run, out RunParticipantStateFact state)
            {
                state = new RunParticipantStateFact(run, _participant, _character, Health);
                return true;
            }
        }

        private sealed class WalletStub : IPartyWallet
        {
            public int Balance { get; }
            public uint Revision => 1u;

            public event Action<int> BalanceChanged
            {
                add { }
                remove { }
            }

            public WalletStub(int balance)
            {
                Balance = balance;
            }

            public bool TryCredit(int amount) => false;
            public bool TryRefund(int amount) => false;
            public bool TryPrepareSpend(int amount, out PartyWalletSpendPlan plan)
            {
                plan = default;
                return false;
            }

            public bool TryCommitSpend(in PartyWalletSpendPlan plan) => false;
            public bool TrySpend(int amount) => false;
            public bool TryApplyLossRatio(float ratio, out int loss)
            {
                loss = 0;
                return false;
            }

            public bool TryApplyReviveLoss(ParticipantId participant, uint deathGeneration,
                float ratio, out int loss)
            {
                loss = 0;
                return false;
            }
        }

        private sealed class ScoreProviderStub : IRunResultProvider
        {
            private readonly int _version;
            private readonly int _score;
            private readonly RunEvaluationGrade _grade;
            private readonly string _message;

            public int ScoreRuleVersion => _version;

            public ScoreProviderStub(int version, int score, RunEvaluationGrade grade,
                string message)
            {
                _version = version;
                _score   = score;
                _grade   = grade;
                _message = message;
            }

            public int CalculateScore(in RunResultSnapshot facts) => _score;

            public RunEvaluationGrade CalculateGrade(int score) => _grade;

            public string GetEvaluationMessage(RunEvaluationGrade grade) => _message;
        }
    }
}
