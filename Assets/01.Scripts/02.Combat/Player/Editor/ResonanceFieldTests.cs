// System
using System;
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;

// 테스트
using NUnit.Framework;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat.Tests
{
    /// <summary> 공명진의 수명 규칙과 원천별 버프 소유를 검증한다 </summary>
    public sealed class ResonanceFieldTests
    {
        private const BindingFlags PRIVATE_INSTANCE =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();
        private bool _serverWasActive;
        private IRunTimeSource _previousTime;
        private Clock _clock;

        [SetUp]
        public void Setup()
        {
            _serverWasActive = NetworkServer.active;
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, true);
            _previousTime = RunTimeLocator.Current;
            _clock = new Clock { GameplayElapsedSeconds = 10d };
            RunTimeLocator.Register(_clock);
        }

        [TearDown]
        public void Cleanup()
        {
            for (int i = _objects.Count - 1; i >= 0; --i)
            {
                UnityEngine.Object.DestroyImmediate(_objects[i]);
            }

            _objects.Clear();
            RunTimeLocator.Unregister(_clock);
            RunTimeLocator.Register(_previousTime);
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                .SetValue(null, _serverWasActive);
        }

        /****************************************
        *              순수 규칙
        ****************************************/

        [Test]
        public void TailUsesEquipmentPercentThenTalentSecondsAndStopsAtCap()
        {
            // 1.5 x (1 + 25%) + 0 = 1.875
            Assert.AreEqual(1.875f,
                ResonanceFieldRules.ResolveTailDuration(1.5f, 25f, 0f, 2.5f), 0.0001f);

            // 1.5 x 1.25 + 0.5 = 2.375
            Assert.AreEqual(2.375f,
                ResonanceFieldRules.ResolveTailDuration(1.5f, 25f, 0.5f, 2.5f), 0.0001f);

            // 상한을 넘으면 잘린다
            Assert.AreEqual(2.5f,
                ResonanceFieldRules.ResolveTailDuration(1.5f, 100f, 0.5f, 2.5f), 0.0001f);

            // 값이 이상하면 0이다. 무한대 유지시간은 만들지 않는다
            Assert.AreEqual(0f,
                ResonanceFieldRules.ResolveTailDuration(float.NaN, 25f, 0.5f, 2.5f), 0.0001f);
        }

        [Test]
        public void WideningTradesDurationForRadiusOnlyWhenBought()
        {
            Assert.AreEqual(3.3f, ResonanceFieldRules.ResolveRadius(3f, 0.3f, 0f), 0.0001f);
            Assert.AreEqual(4f, ResonanceFieldRules.ResolveRadius(3f, 0.3f, 0.7f), 0.0001f);

            Assert.AreEqual(6f, ResonanceFieldRules.ResolveDuration(6f, 0f, 1f), 0.0001f,
                "끝노드를 사지 않으면 지속은 그대로다");
            Assert.AreEqual(5f, ResonanceFieldRules.ResolveDuration(6f, 0.7f, 1f), 0.0001f);
        }

        [Test]
        public void ResidencySeparatesHorizontalRadiusFromHeight()
        {
            Vector3 center = new Vector3(0f, 0f, 0f);

            Assert.IsTrue(ResonanceFieldRules.IsWithinField(
                center, new Vector3(2.9f, 0f, 0f), 3f, 1.5f));
            Assert.IsFalse(ResonanceFieldRules.IsWithinField(
                center, new Vector3(3.1f, 0f, 0f), 3f, 1.5f));

            // 위층에 선 사람은 수평으로 겹쳐도 같은 진이 아니다
            Assert.IsFalse(ResonanceFieldRules.IsWithinField(
                center, new Vector3(0f, 2f, 0f), 3f, 1.5f));
            Assert.IsTrue(ResonanceFieldRules.IsWithinField(
                center, new Vector3(0f, 1.4f, 0f), 3f, 1.5f));
        }

        /****************************************
        *            원천별 버프 소유
        ****************************************/

        [Test]
        public void SourcesExpireIndependentlyAndNeverStack()
        {
            SupportBuffController buff = CreateBuff(out ParticipantId participant);
            ResonanceFieldSource weak = Source(participant, 1u);
            ResonanceFieldSource strong = Source(participant, 2u);

            Assert.IsTrue(buff.ServerApplyResonance(weak, 1.10f, 12.0));
            Assert.IsTrue(buff.ServerApplyResonance(strong, 1.10f, 11.0));

            // 같은 세기를 겹쳐도 적용값은 하나다
            Assert.AreEqual(1.10f, buff.ActiveDamageMultiplier, 0.0001f);
            Assert.AreEqual(12.0, buff.ActiveExpiryGameplayTime, 0.0001);

            // 짧은 쪽이 먼저 끝나도 긴 쪽은 그대로 남는다
            _clock.GameplayElapsedSeconds = 11.5;
            Pump(buff);
            Assert.AreEqual(1.10f, buff.ActiveDamageMultiplier, 0.0001f);
            Assert.AreEqual(12.0, buff.ActiveExpiryGameplayTime, 0.0001);

            _clock.GameplayElapsedSeconds = 12.01;
            Pump(buff);
            Assert.AreEqual(1f, buff.ActiveDamageMultiplier, 0.0001f);
            Assert.AreEqual(0d, buff.ActiveExpiryGameplayTime, 0.0001);
        }

        [Test]
        public void RefreshOnlyPushesExpiryForwardAndForcedRemovalTakesTailBack()
        {
            SupportBuffController buff = CreateBuff(out ParticipantId participant);
            ResonanceFieldSource source = Source(participant, 7u);

            Assert.IsTrue(buff.ServerApplyResonance(source, 1.10f, 13.0));
            // 나갔다 들어오는 것이 이득이 되지 않도록 만료를 앞으로 당기지 않는다
            Assert.IsTrue(buff.ServerApplyResonance(source, 1.10f, 11.0));
            Assert.AreEqual(13.0, buff.ActiveExpiryGameplayTime, 0.0001);

            Assert.IsTrue(buff.ServerApplyResonance(source, 1.10f, 14.0));
            Assert.AreEqual(14.0, buff.ActiveExpiryGameplayTime, 0.0001);

            // 시전자 쪽 사유로 폐기하면 남은 이탈 유지분까지 즉시 사라진다
            Assert.IsTrue(buff.ServerRemoveResonance(source));
            Assert.AreEqual(1f, buff.ActiveDamageMultiplier, 0.0001f);
            Assert.IsFalse(buff.ServerRemoveResonance(source));
        }

        [Test]
        public void PausedGameplayClockFreezesRemainingTime()
        {
            SupportBuffController buff = CreateBuff(out ParticipantId participant);
            Assert.IsTrue(buff.ServerApplyResonance(Source(participant, 3u), 1.10f, 11.5));

            // 게임 시계가 멈춘 동안에는 남은 시간도 줄지 않는다
            for (int i = 0; i < 5; ++i) { Pump(buff); }
            Assert.AreEqual(1.10f, buff.ActiveDamageMultiplier, 0.0001f);

            _clock.GameplayElapsedSeconds = 11.51;
            Pump(buff);
            Assert.AreEqual(1f, buff.ActiveDamageMultiplier, 0.0001f);
        }

        [Test]
        public void InvalidSourceOrExpiredWindowIsRejected()
        {
            SupportBuffController buff = CreateBuff(out ParticipantId participant);

            Assert.IsFalse(buff.ServerApplyResonance(default, 1.10f, 12.0),
                "원천 ID가 없는 버프는 받지 않는다");
            Assert.IsFalse(buff.ServerApplyResonance(Source(participant, 1u), 1f, 12.0),
                "계수가 1이면 버프가 아니다");
            Assert.IsFalse(buff.ServerApplyResonance(Source(participant, 1u), 1.10f, 9.0),
                "이미 지난 만료는 받지 않는다");
            Assert.AreEqual(1f, buff.ActiveDamageMultiplier, 0.0001f);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private SupportBuffController CreateBuff(out ParticipantId participant)
        {
            GameObject go = new GameObject("Resonance receiver", typeof(NetworkIdentity));
            _objects.Add(go);

            TestParticipant stub = go.AddComponent<TestParticipant>();
            participant = stub.Participant;

            SupportBuffController buff = go.AddComponent<SupportBuffController>();
            NetworkIdentity identity = go.GetComponent<NetworkIdentity>();
            typeof(NetworkIdentity)
                .GetMethod("InitializeNetworkBehaviours", PRIVATE_INSTANCE).Invoke(identity, null);
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isServer))
                .SetValue(identity, true);
            typeof(SupportBuffController).GetMethod("Awake", PRIVATE_INSTANCE).Invoke(buff, null);
            buff.OnStartServer();
            return buff;
        }

        private static ResonanceFieldSource Source(ParticipantId caster, uint fieldId)
        {
            return new ResonanceFieldSource { Caster = caster, FieldId = fieldId };
        }

        private static void Pump(SupportBuffController buff)
        {
            typeof(SupportBuffController).GetMethod("Update", PRIVATE_INSTANCE)
                .Invoke(buff, null);
        }

        private sealed class TestParticipant : MonoBehaviour, IGrowthParticipant
        {
            public ParticipantId Participant => new ParticipantId(42UL);
            public GrowthState Growth => default;
            public event Action<GrowthState> GrowthChanged;
            public void ServerApplyGrowth(GrowthState state) { GrowthChanged?.Invoke(state); }
        }

        private sealed class Clock : IRunTimeSource
        {
            public double GameplayElapsedSeconds { get; set; }
            public double FieldElapsedSeconds => GameplayElapsedSeconds;
            public double BossElapsedSeconds => 0;
            public bool IsPaused => false;
            public RunPauseReason PauseReasons => RunPauseReason.None;
        }
    }
}
