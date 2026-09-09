// System
using System;
using System.Reflection;

// Unity
using UnityEngine;

// Third party
using NUnit.Framework;

// Project
using Geumeum.Common;
using Geumeum.Combat;
using Geumeum.Data;

namespace Geumeum.Art.Tests
{
    public sealed class ArtPresentationTests
    {
        private GameObject _viewObject;
        private BossArenaPhaseView _view;
        private IRunTimeSource _previousRunTime;
        private MutableRunTimeSource _runTime;

        [SetUp]
        public void SetUp()
        {
            BossPhaseSourceLocator.Unregister(BossPhaseSourceLocator.Current);
            _previousRunTime = RunTimeLocator.Current;
            _runTime = new MutableRunTimeSource();
            RunTimeLocator.Register(_runTime);
            _viewObject = new GameObject("BossArenaPhaseViewTests");
            _view = _viewObject.AddComponent<BossArenaPhaseView>();
        }

        [TearDown]
        public void TearDown()
        {
            BossPhaseSourceLocator.Unregister(BossPhaseSourceLocator.Current);
            UnityEngine.Object.DestroyImmediate(_viewObject);
            RunTimeLocator.Unregister(_runTime);
            if (_previousRunTime != null)
            {
                RunTimeLocator.Register(_previousRunTime);
            }
        }

        [Test]
        public void LatePhaseSourceRegistrationAppliesCurrentSnapshot()
        {
            SceneTransitionSnapshot transition = default;
            _view.Bind(in transition);
            var source = new FakeBossPhaseSource(CreateSnapshot(2, 3u, 10d));

            BossPhaseSourceLocator.Register(source);

            Assert.AreEqual(BossArenaPhase.Underworld, _view.CurrentPhase);
        }

        [Test]
        public void SceneExitDetachesPhaseObserver()
        {
            var source = new FakeBossPhaseSource(CreateSnapshot(1, 1u, 0d));
            BossPhaseSourceLocator.Register(source);
            SceneTransitionSnapshot transition = default;
            _view.Bind(in transition);
            _view.Unbind();

            source.Publish(CreateSnapshot(2, 2u, 5d));

            Assert.AreEqual(BossArenaPhase.Shrine, _view.CurrentPhase);
        }

        [Test]
        public void SameRevisionCannotRestartOrReplaceAppliedPhase()
        {
            var source = new FakeBossPhaseSource(CreateSnapshot(1, 8u, 0d));
            BossPhaseSourceLocator.Register(source);
            SceneTransitionSnapshot transition = default;
            _view.Bind(in transition);

            source.Publish(CreateSnapshot(2, 8u, 4d));

            Assert.AreEqual(BossArenaPhase.Shrine, _view.CurrentPhase);
        }

        [Test]
        public void RemotePhaseProgressStartsFromAuthoritativeGameplayTime()
        {
            BossPhaseSnapshot snapshot = CreateSnapshot(2, 2u, 10d);

            float progress = BossArenaPhaseRules.ResolveTransitionProgress(
                in snapshot, 13d, 6f);

            Assert.AreEqual(0.5f, progress, 0.0001f);
            Assert.AreEqual(1f, BossArenaPhaseRules.ResolveTransitionProgress(
                in snapshot, 20d, 6f));
        }

        [Test]
        public void DayClockHoldsWhenGameplayElapsedDoesNotAdvance()
        {
            DayCycleSO config = ScriptableObject.CreateInstance<DayCycleSO>();
            try
            {
                float beforePause = DayCycleRules.ResolveGameHour(
                    DayCycleSceneMode.Field, 450d, config);
                float duringPause = DayCycleRules.ResolveGameHour(
                    DayCycleSceneMode.Field, 450d, config);

                Assert.AreEqual(11f, beforePause, 0.0001f);
                Assert.AreEqual(beforePause, duringPause);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void DayClockClampsFieldEndAndFixesBossAtMidnight()
        {
            DayCycleSO config = ScriptableObject.CreateInstance<DayCycleSO>();
            try
            {
                Assert.AreEqual(20f, DayCycleRules.ResolveGameHour(
                    DayCycleSceneMode.Field, 1800d, config));
                Assert.AreEqual(20f, DayCycleRules.ResolveGameHour(
                    DayCycleSceneMode.Field, 9999d, config));
                Assert.AreEqual(0f, DayCycleRules.ResolveGameHour(
                    DayCycleSceneMode.Boss, 9999d, config));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void ActionCueRejectsExpiredAndStaleExecutionButAcceptsWrappedSuccessor()
        {
            PlayerActionCue cue = CreateActionCue(7u, 10d, 12d);

            Assert.AreEqual(PlayerActionCueDecision.Ignore,
                PlayerActionCueRules.ResolveStart(in cue, 12d,
                    PlayerActionCueKind.None, 0u, out _));
            Assert.AreEqual(PlayerActionCueDecision.Ignore,
                PlayerActionCueRules.ResolveStart(in cue, 10.5d,
                    PlayerActionCueKind.Attack, 8u, out _));

            cue.ExecutionId = 1u;
            Assert.AreEqual(PlayerActionCueDecision.Start,
                PlayerActionCueRules.ResolveStart(in cue, 10.5d,
                    PlayerActionCueKind.Attack, uint.MaxValue, out float progress));
            Assert.AreEqual(0.25f, progress, 0.0001f);
        }

        [Test]
        public void ActionCueCancelAndImpactRequireExactCurrentExecution()
        {
            Assert.IsTrue(PlayerActionCueRules.CanCancel(
                PlayerActionCueKind.Attack, 19u,
                PlayerActionCueKind.Attack, 19u));
            Assert.IsFalse(PlayerActionCueRules.CanCancel(
                PlayerActionCueKind.Attack, 18u,
                PlayerActionCueKind.Attack, 19u));
            Assert.IsFalse(PlayerCombatPresentationRules.CanPlayEvent(
                PlayerActionCueKind.Heal, 19u,
                PlayerActionCueKind.Attack, 19u,
                20d, 20.1d, 0.15f));
            Assert.IsFalse(PlayerCombatPresentationRules.CanPlayEvent(
                PlayerActionCueKind.Attack, 18u,
                PlayerActionCueKind.Attack, 19u,
                20d, 20.1d, 0.15f));
            Assert.IsTrue(PlayerCombatPresentationRules.CanPlayEvent(
                PlayerActionCueKind.Attack, 19u,
                PlayerActionCueKind.Attack, 19u,
                20d, 20.1d, 0.15f));
        }

        [Test]
        public void ImpactAndMarkOneShotsRejectFutureAndExpiredGameplayTimes()
        {
            Assert.IsTrue(PlayerActionCueRules.IsLateOneShot(10d, 9.9d, 0.15f));
            Assert.IsFalse(PlayerActionCueRules.IsLateOneShot(10d, 10.1d, 0.15f));
            Assert.IsTrue(PlayerActionCueRules.IsLateOneShot(10d, 10.2d, 0.15f));
            Assert.IsTrue(PlayerActionCueRules.IsLateOneShot(
                double.NaN, 10d, 0.15f));
        }

        [Test]
        public void ImmediateParryResultUsesResolvedGameplayTimeInsteadOfFutureVisualImpact()
        {
            Assert.IsFalse(PlayerCombatPresentationRules.CanPlayEvent(
                PlayerActionCueKind.Parry, 4u,
                PlayerActionCueKind.Parry, 4u,
                10.05d, 10d, 0.15f));
            Assert.IsTrue(PlayerCombatPresentationRules.CanPlayEvent(
                PlayerActionCueKind.Parry, 4u,
                PlayerActionCueKind.Parry, 4u,
                10d, 10d, 0.15f));
        }

        [Test]
        public void AnimatorHitStopOverlapsAndHoldsDuringRunPause()
        {
            var state = new AnimatorHitStopState();
            state.Begin(1.25f, 0.08f);
            state.Begin(0f, 0.04f);

            Assert.IsFalse(state.Tick(1f, true, out _));
            Assert.AreEqual(0.08f, state.RemainingSeconds, 0.0001f);
            Assert.IsFalse(state.Tick(0.04f, false, out _));
            Assert.IsTrue(state.Tick(0.04f, false, out float restoreSpeed));
            Assert.AreEqual(1.25f, restoreSpeed, 0.0001f);
        }

        [Test]
        public void PlayerAnimatorStopsAndRestoresAtRunPauseBoundary()
        {
            var root = new GameObject("PauseAwarePlayerAnimation");
            var pivot = new GameObject("VisualPivot");
            pivot.transform.SetParent(root.transform, false);
            Animator animator = pivot.AddComponent<Animator>();
            PlayerAnimationController controller =
                root.AddComponent<PlayerAnimationController>();
            controller.RefreshAnimator();
            MethodInfo updatePause = typeof(PlayerAnimationController).GetMethod(
                "UpdateAnimatorPause", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(updatePause);

            try
            {
                animator.speed = 1.5f;
                _runTime.IsPaused = true;
                Assert.IsTrue(RunTimeLocator.Current.IsPaused);
                updatePause.Invoke(controller, null);
                Assert.AreEqual(0f, animator.speed);

                _runTime.IsPaused = false;
                updatePause.Invoke(controller, null);
                Assert.AreEqual(1.5f, animator.speed, 0.0001f);
            }
            finally
            {
                _runTime.IsPaused = false;
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ReusedVisualRejectsStopFromPreviousLease()
        {
            GameObject root = CreatePooledVisual(out PooledVisualLifetime lifetime,
                out _);
            try
            {
                lifetime.OnRentFromPool();
                uint firstLease = lifetime.LeaseGeneration;
                lifetime.OnReturnToPool();
                lifetime.OnRentFromPool();

                Assert.AreNotEqual(firstLease, lifetime.LeaseGeneration);
                Assert.IsFalse(lifetime.TryStop(firstLease));
                Assert.IsTrue(lifetime.IsRented);
            }
            finally
            {
                lifetime.OnReturnToPool();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RentedParticleSystemPausesAndResumesWithRunClock()
        {
            GameObject root = CreatePooledVisual(out PooledVisualLifetime lifetime,
                out ParticleSystem particle);
            MethodInfo update = typeof(PooledVisualLifetime).GetMethod(
                "Update", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(update);
            try
            {
                lifetime.OnRentFromPool();
                Assert.IsTrue(particle.isPlaying);

                _runTime.IsPaused = true;
                update.Invoke(lifetime, null);
                Assert.IsTrue(particle.isPaused);

                _runTime.IsPaused = false;
                update.Invoke(lifetime, null);
                Assert.IsTrue(particle.isPlaying);

                lifetime.OnReturnToPool();
                _runTime.IsPaused = true;
                lifetime.OnRentFromPool();
                Assert.IsTrue(particle.isPaused);
            }
            finally
            {
                _runTime.IsPaused = false;
                lifetime.OnReturnToPool();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void DedicatedServerSkipsPresentationPoolPrewarm()
        {
            Assert.IsTrue(PresentationPoolRules.TryResolvePrewarm(
                true, false, out bool dedicatedPrewarm));
            Assert.IsFalse(dedicatedPrewarm);
            Assert.IsTrue(PresentationPoolRules.TryResolvePrewarm(
                true, true, out bool hostPrewarm));
            Assert.IsTrue(hostPrewarm);
            Assert.IsTrue(PresentationPoolRules.TryResolvePrewarm(
                false, true, out bool clientPrewarm));
            Assert.IsTrue(clientPrewarm);
            Assert.IsFalse(PresentationPoolRules.TryResolvePrewarm(
                false, false, out bool unresolvedPrewarm));
            Assert.IsFalse(unresolvedPrewarm);
        }

        [Test]
        public void BossGateCuePlaysOnlyOnClientEntryEdge()
        {
            Assert.IsFalse(RunEntranceSoundRules.ShouldPlayGate(
                true, BossEntranceState.CountingDown,
                BossEntranceState.Entering, false));
            Assert.IsTrue(RunEntranceSoundRules.ShouldPlayGate(
                true, BossEntranceState.CountingDown,
                BossEntranceState.Entering, true));
            Assert.IsFalse(RunEntranceSoundRules.ShouldPlayGate(
                true, BossEntranceState.Entering,
                BossEntranceState.Entering, true));
            Assert.IsFalse(RunEntranceSoundRules.ShouldPlayGate(
                false, BossEntranceState.Idle,
                BossEntranceState.Ready, true));
        }

        [Test]
        public void PresentationDefinitionRejectsNonFiniteAndNegativeRawValues()
        {
            PresentationConfigSO config =
                ScriptableObject.CreateInstance<PresentationConfigSO>();
            try
            {
                SetPrivateField(config, "_lightHitStopSeconds", float.NaN);
                Assert.IsFalse(config.ValidateDefinition(out _));

                SetPrivateField(config, "_lightHitStopSeconds", 0.04f);
                SetPrivateField(config, "_damageFlashSeconds", -1f);
                Assert.IsFalse(config.ValidateDefinition(out _));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        private static BossPhaseSnapshot CreateSnapshot(int phase, uint revision,
            double changedGameplayTime)
        {
            return new BossPhaseSnapshot
            {
                PhaseIndex = phase,
                Stage = BossEncounterStage.Fighting,
                Revision = revision,
                PhaseChangedGameplayTime = changedGameplayTime,
            };
        }

        private static PlayerActionCue CreateActionCue(uint executionId,
            double start, double end)
        {
            return new PlayerActionCue
            {
                ExecutionId = executionId,
                Kind = PlayerActionCueKind.Attack,
                Weapon = WeaponKind.Sword,
                ComboStage = 1,
                StartGameplayTime = start,
                ImpactGameplayTime = (start + end) * 0.5d,
                EndGameplayTime = end,
            };
        }

        private static GameObject CreatePooledVisual(
            out PooledVisualLifetime lifetime, out ParticleSystem particle)
        {
            var root = new GameObject("PooledVisualLifetimeTests");
            root.AddComponent<LocalPoolRoot>();
            var visual = new GameObject("Particles");
            visual.transform.SetParent(root.transform, false);
            particle = visual.AddComponent<ParticleSystem>();
            lifetime = root.AddComponent<PooledVisualLifetime>();
            return root;
        }

        private static void SetPrivateField(object target, string fieldName,
            object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, fieldName);
            field.SetValue(target, value);
        }

        private sealed class FakeBossPhaseSource : IBossPhaseSource
        {
            public BossPhaseSnapshot PhaseSnapshot { get; private set; }
            public event Action<BossPhaseSnapshot> PhaseChanged;

            public FakeBossPhaseSource(BossPhaseSnapshot snapshot)
            {
                PhaseSnapshot = snapshot;
            }

            public void Publish(BossPhaseSnapshot snapshot)
            {
                PhaseSnapshot = snapshot;
                PhaseChanged?.Invoke(snapshot);
            }
        }

        private sealed class MutableRunTimeSource : IRunTimeSource
        {
            public double FieldElapsedSeconds => GameplayElapsedSeconds;
            public double BossElapsedSeconds => GameplayElapsedSeconds;
            public double GameplayElapsedSeconds { get; set; }
            public RunPauseReason PauseReasons => IsPaused
                ? RunPauseReason.SceneTransition
                : RunPauseReason.None;
            public bool IsPaused { get; set; }
        }
    }
}
