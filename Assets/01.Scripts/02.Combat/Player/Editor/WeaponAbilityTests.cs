using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Mirror;
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Combat.Tests
{
    public sealed partial class WeaponAbilityTests
    {
        private readonly List<Object> _objects = new List<Object>();
        private readonly List<uint> _ids = new List<uint>();
        private bool _serverWasActive;
        private RunManager _previousRun;
        private RunSceneDirector _previousScene;
        private IRunTimeSource _previousTime;
        private Clock _clock;
        private RunManager _run;
        private RunSceneDirector _scene;
        private uint _id = 700000u;

        [SetUp]
        public void Setup()
        {
            _serverWasActive = NetworkServer.active;
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, true);
            ResetPlacementAim();
            _previousRun = RunManager.ServerInstance; _previousScene = RunSceneDirector.ServerInstance;
            _previousTime = RunTimeLocator.Current;
            _run = Create<RunManager>(); Set(_run, "_runIdValue", 7u); Set(_run, "_phase", RunPhase.Farming);
            _scene = Create<RunSceneDirector>(); Set(_scene, "_revisionValue", 2u);
            SetStatic(typeof(RunManager), "ServerInstance", _run);
            SetStatic(typeof(RunSceneDirector), "ServerInstance", _scene);
            _clock = new Clock { GameplayElapsedSeconds = 10d }; RunTimeLocator.Register(_clock);
            var database = ScriptableObject.CreateInstance<GameDatabaseSO>(); _objects.Add(database);
            var combat = ScriptableObject.CreateInstance<CombatConfigSO>(); _objects.Add(combat);
            var arrow = ScriptableObject.CreateInstance<ArrowConfigSO>(); _objects.Add(arrow);
            Set(arrow, "_worldMask", (LayerMask)0);
            Set(database, "_combat", combat); Set(database, "_arrow", arrow);
            GameDB.Initialize(database);
        }

        [TearDown]
        public void Cleanup()
        {
            ResetPlacementAim();
            MarkRegistry.ClearAll();
            ServerPlayerList().Clear();
            foreach (uint id in _ids) { NetworkServer.spawned.Remove(id); }
            _ids.Clear();
            for (int i = _objects.Count - 1; i >= 0; --i) { Object.DestroyImmediate(_objects[i]); }
            _objects.Clear();
            SetStatic(typeof(RunManager), "ServerInstance", _previousRun);
            SetStatic(typeof(RunSceneDirector), "ServerInstance", _previousScene);
            RunTimeLocator.Unregister(_clock); RunTimeLocator.Register(_previousTime);
            GameDB.ResetStatic();
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, _serverWasActive);
        }

        private T Create<T>() where T : Component
        {
            GameObject go = new GameObject(typeof(T).Name, typeof(NetworkIdentity)); _objects.Add(go);
            T component = go.AddComponent<T>();
            InitializeIdentity(go);
            return component;
        }

        private void InitializeIdentity(GameObject go)
        {
            NetworkIdentity identity = go.GetComponent<NetworkIdentity>();
            typeof(NetworkIdentity).GetMethod("InitializeNetworkBehaviours", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(identity, null);
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isServer)).SetValue(identity, true);
            if (identity.netId == 0)
            {
                typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.netId)).SetValue(identity, ++_id);
                NetworkServer.spawned[identity.netId] = identity; _ids.Add(identity.netId);
            }
        }

        private PlayerAttack Owner(WeaponKind kind)
        {
            PlayerStatus status = Create<PlayerStatus>();
            Set(status, "_participant", new ParticipantId(status.netId));
            Set(status, "_stamina", 100f); Set(status, "_spirit", 100f); Set(status, "_serverSpirit", 100f);
            Set(status, "_resourceStateInitialized", true);
            PlayerWeapon weapon = status.gameObject.AddComponent<PlayerWeapon>();
            Set(weapon, "_weapon", kind); Set(weapon, "_hasWeapon", true);
            status.gameObject.AddComponent<Loadout>();
            SupportBuffController buff = status.gameObject.AddComponent<SupportBuffController>();
            PlayerAttack attack = status.gameObject.AddComponent<PlayerAttack>();
            InitializeIdentity(status.gameObject);
            typeof(SupportBuffController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(buff, null);
            typeof(PlayerAttack).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(attack, null);
            Set(status, "_hitAttack", attack);
            buff.OnStartServer();
            attack.OnStartServer();
            // 진의 체류 판정은 서버 참가자 목록을 훑는다. PlayerStatus.OnStartServer 전체를
            // 돌리는 대신 이 fixture가 목록만 직접 채운다
            ServerPlayerList().Add(status);
            return attack;
        }

        private Target TargetAt(float z = 2f)
        {
            Target target = Create<Target>(); target.transform.position = new Vector3(0, 1.2f, z); return target;
        }

        private void Install(PlayerAttack owner, Target target)
        {
            typeof(PlayerAttack).GetMethod("InstallMeleeWedge", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(owner, new object[] { target, target.transform.position });
        }

        private void Hit(PlayerAttack owner, Target target, uint action, bool precision = false, bool bell = false, float x = 0f)
        {
            PlayerAttack.ServerNotifyDirectHit(owner.netId, action, precision, bell, target, target.transform.position,
                new Vector3(x, 1.2f, 0f), Vector3.forward, 5f, 20f);
        }

        [Test]
        public void BroadBodyHitDoesNotCountAsWedgeHitAndTwoDifferentActionsBurst()
        {
            var bow = Owner(WeaponKind.Bow); var target = TargetAt();
            Install(bow, target); Assert.IsTrue(bow.Wedge.IsActive(10));
            Hit(bow, target, 2, x: 0.31f); Assert.AreEqual(0, bow.Wedge.Count);
            Hit(bow, target, 3); Assert.AreEqual(1, bow.Wedge.Count);
            Hit(bow, target, 3); Assert.AreEqual(1, bow.Wedge.Count);
            Hit(bow, target, 4); Assert.AreEqual(0u, bow.Wedge.Generation);
            Assert.AreEqual(960f, target.Health);
        }

        [Test]
        public void PrecisionNeverInstallsAndCanBurstMeleeInstalledWedge()
        {
            var bow = Owner(WeaponKind.Bow); var target = TargetAt();
            Hit(bow, target, 5, true); Assert.AreEqual(0u, bow.Wedge.Generation);
            Install(bow, target);
            Hit(bow, target, 6, true); Assert.AreEqual(960f, target.Health);
            Hit(bow, target, 6, true); Assert.AreEqual(960f, target.Health);
            Assert.AreEqual(0u, bow.Wedge.Generation);
        }

        [Test]
        public void AnotherArchersArrowDoesNotConsumeOwnersWedge()
        {
            var first = Owner(WeaponKind.Bow); var second = Owner(WeaponKind.Bow); var target = TargetAt();
            Install(first, target); Install(second, target);
            Assert.AreEqual(0, first.Wedge.Count); Assert.AreEqual(0, second.Wedge.Count);
        }

        [Test]
        public void EchoCapsAtTwoTargetsAndAllyChargesOnceWithoutRefreshingExpiry()
        {
            var bell = Owner(WeaponKind.Bell); var ally = Owner(WeaponKind.Sword);
            var a = TargetAt(); var b = TargetAt(); var c = TargetAt();
            Hit(bell, a, 1, bell: true); Hit(bell, b, 1, bell: true); Hit(bell, c, 1, bell: true);
            Assert.AreEqual(a.GetComponent<NetworkIdentity>().netId, bell.EchoFirst.Target.EntityNetId);
            Assert.AreEqual(b.GetComponent<NetworkIdentity>().netId, bell.EchoSecond.Target.EntityNetId);
            double expires = bell.EchoFirst.ExpiresAt;
            _clock.GameplayElapsedSeconds += 0.3;
            Hit(ally, a, 1); Hit(ally, a, 1);
            Assert.AreEqual(2, bell.EchoFirst.Count);
            _clock.GameplayElapsedSeconds += 0.3; Hit(ally, a, 2);
            Assert.AreEqual(3, bell.EchoFirst.Count); Assert.AreEqual(expires, bell.EchoFirst.ExpiresAt);
        }

        [Test]
        public void TwoBellOwnersHaveIndependentChargeDeduplication()
        {
            var a = Owner(WeaponKind.Bell); var b = Owner(WeaponKind.Bell); var ally = Owner(WeaponKind.Sword); var target = TargetAt();
            Hit(a, target, 1, bell: true); Hit(b, target, 1, bell: true);
            _clock.GameplayElapsedSeconds += 0.3; Hit(ally, target, 1);
            Assert.AreEqual(2, a.EchoFirst.Count); Assert.AreEqual(2, b.EchoFirst.Count);
        }

        [Test]
        public void NoTargetsAndStaleRequestCostNothing()
        {
            var bell = Owner(WeaponKind.Bell);
            Assert.AreEqual(ActionRejection.InvalidInput, bell.ServerRequestEchoRecall(1, 7, 2));
            Assert.AreEqual(ActionRejection.State, bell.ServerRequestEchoRecall(2, 6, 2));
            Assert.AreEqual(100f, bell.GetComponent<PlayerStatus>().Spirit);
        }

        [Test]
        public void HitCancellationReleasesReservationWithoutRefundOrExpiryExtension()
        {
            var bell = Owner(WeaponKind.Bell); var target = TargetAt(); Hit(bell, target, 1, bell: true);
            double expiry = bell.EchoFirst.ExpiresAt;
            Assert.AreEqual(ActionRejection.None, bell.ServerRequestEchoRecall(1, 7, 2));
            Assert.AreNotEqual(0u, bell.EchoFirst.Reservation); Assert.IsTrue(bell.IsRecalling);
            bell.ServerInterruptForHit();
            Assert.AreEqual(0u, bell.EchoFirst.Reservation); Assert.AreEqual(expiry, bell.EchoFirst.ExpiresAt);
            Assert.AreEqual(90f, bell.GetComponent<PlayerStatus>().Spirit); Assert.IsFalse(bell.IsRecalling);
            Assert.Greater(bell.EchoCooldownRemaining, 0);
        }

        [Test]
        public void RecallSnapshotsChargeAndUsesOwnPowerWithoutRecursiveAllyCharge()
        {
            var bell = Owner(WeaponKind.Bell); var other = Owner(WeaponKind.Bell); var ally = Owner(WeaponKind.Sword); var target = TargetAt();
            Hit(bell, target, 1, bell: true); Hit(other, target, 1, bell: true);
            Assert.AreEqual(ActionRejection.None, bell.ServerRequestEchoRecall(1, 7, 2));
            _clock.GameplayElapsedSeconds += 0.3; Hit(ally, target, 2);
            Assert.AreEqual(1, bell.EchoFirst.Count); Assert.AreEqual(2, other.EchoFirst.Count);
            bell.ServerUpdateState(NetworkTime.time);
            Assert.AreEqual(988f, target.Health); // .6 * own power 20
            Assert.AreEqual(0u, bell.EchoFirst.Generation); Assert.AreEqual(2, other.EchoFirst.Count);
        }

        [Test]
        public void LeavingRecallRangePreservesMarkAndCostsSpirit()
        {
            var bell = Owner(WeaponKind.Bell); var target = TargetAt(); Hit(bell, target, 1, bell: true);
            Assert.AreEqual(ActionRejection.None, bell.ServerRequestEchoRecall(1, 7, 2));
            target.transform.position += Vector3.forward * 20;
            _clock.GameplayElapsedSeconds += 0.3; bell.ServerUpdateState(NetworkTime.time);
            Assert.AreEqual(1000f, target.Health); Assert.AreNotEqual(0u, bell.EchoFirst.Generation);
            Assert.AreEqual(0u, bell.EchoFirst.Reservation); Assert.AreEqual(90f, bell.GetComponent<PlayerStatus>().Spirit);
        }

        [Test]
        public void ClosedPartSceneChangeAndResetClearMarks()
        {
            var bow = Owner(WeaponKind.Bow); var target = TargetAt(); Install(bow, target);
            target.Open = false; bow.ServerUpdateState(NetworkTime.time); Assert.AreEqual(0u, bow.Wedge.Generation);
            target.Open = true; Install(bow, target); Set(_scene, "_revisionValue", 3u);
            bow.ServerUpdateState(NetworkTime.time); Assert.AreEqual(0u, bow.Wedge.Generation);
            Install(bow, target); bow.ResetForNewRun(); Assert.AreEqual(0u, bow.Wedge.Generation);
        }

        [Test]
        public void PausedClockKeepsEchoAndReservedRecall()
        {
            var bell = Owner(WeaponKind.Bell); var target = TargetAt(); Hit(bell, target, 1, bell: true);
            Assert.AreEqual(ActionRejection.None, bell.ServerRequestEchoRecall(1, 7, 2));
            var before = bell.EchoFirst; Set(_run, "_pauseReasons", RunPauseReason.BossCinematic);
            bell.ServerUpdateState(NetworkTime.time + 30);
            Assert.AreEqual(before.Reservation, bell.EchoFirst.Reservation);
            Assert.AreEqual(before.ExpiresAt, bell.EchoFirst.ExpiresAt); Assert.AreEqual(1000, target.Health);
        }

        [Test]
        public void RecallDoesNotConsumeOrRefreshExistingCounterMark()
        {
            var bell = Owner(WeaponKind.Bell); var target = TargetAt(); Hit(bell, target, 1, bell: true);
            ParticipantId owner = bell.GetComponent<PlayerStatus>().Participant;
            TargetRef reference = bell.EchoFirst.Target;
            Assert.IsTrue(MarkRegistry.TryApply(owner, reference, 10d, 8d, 2, out _));
            Assert.IsTrue(MarkRegistry.TryGetSlot(owner, 0, 10d, out MarkSlot before));
            Assert.AreEqual(ActionRejection.None, bell.ServerRequestEchoRecall(1, 7, 2));
            _clock.GameplayElapsedSeconds += 0.3; bell.ServerUpdateState(NetworkTime.time);
            Assert.IsTrue(MarkRegistry.TryGetSlot(owner, 0, 10.3d, out MarkSlot after));
            Assert.AreEqual(before, after);
        }

        [Test]
        public void WallAppearingDuringRecallReleasesReservationWithoutDamage()
        {
            var bell = Owner(WeaponKind.Bell); var target = TargetAt(); Hit(bell, target, 1, bell: true);
            Assert.AreEqual(ActionRejection.None, bell.ServerRequestEchoRecall(1, 7, 2));
            Set(GameDB.Arrow, "_worldMask", (LayerMask)1);
            GameObject wall = new GameObject("Recall occluder", typeof(BoxCollider)); _objects.Add(wall);
            wall.transform.position = new Vector3(0, 1.2f, 1);
            wall.GetComponent<BoxCollider>().size = new Vector3(3, 3, 0.1f);
            Physics.SyncTransforms();
            _clock.GameplayElapsedSeconds += 0.3; bell.ServerUpdateState(NetworkTime.time);
            Assert.AreEqual(1000, target.Health); Assert.AreEqual(0, bell.EchoFirst.Reservation);
            Assert.AreNotEqual(0, bell.EchoFirst.Generation); Assert.AreEqual(90, bell.GetComponent<PlayerStatus>().Spirit);
        }

        [Test]
        public void ResourceAndBusyRejectionsDoNotReserveOrConsume()
        {
            var bell = Owner(WeaponKind.Bell); var target = TargetAt(); Hit(bell, target, 1, bell: true);
            Set(bell, "_isSwinging", true);
            Assert.AreEqual(ActionRejection.Busy, bell.ServerRequestEchoRecall(1, 7, 2));
            Set(bell, "_isSwinging", false); Set(bell.GetComponent<PlayerStatus>(), "_serverSpirit", 9f);
            Assert.AreEqual(ActionRejection.Resource, bell.ServerRequestEchoRecall(2, 7, 2));
            Assert.AreEqual(9f, bell.GetComponent<PlayerStatus>().Spirit); Assert.AreEqual(0, bell.EchoFirst.Reservation);
        }

        [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)] [TestCase(-1f)]
        public void NonFiniteOrNegativeRayLengthIsRejected(float distance)
        { Assert.IsFalse(WeaponAbilityRules.IntersectsWedge(Vector3.zero, Vector3.forward, distance, Vector3.forward, 0.3f)); }

        private static List<PlayerStatus> ServerPlayerList()
        {
            return (List<PlayerStatus>)typeof(PlayerStatus)
                .GetField("G_ServerPlayers", BindingFlags.Static | BindingFlags.NonPublic)
                .GetValue(null);
        }

        /// <summary> 버프 만료는 Update에서 정리된다. 시계를 옮긴 뒤 한 번 돌려 준다 </summary>
        private static void PumpBuff(SupportBuffController buff)
        {
            typeof(SupportBuffController)
                .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(buff, null);
        }

        private static void Set(object obj, string field, object value)
        { obj.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(obj, value); }
        /// <summary>
        /// LocalPlacementAim.SuppressesCombatInput은 조준을 끝낸 "그 프레임"을 Time.frameCount로
        /// 기억한다. EditMode 테스트는 여러 개가 에디터의 같은 프레임 안에서 연달아 도는데,
        /// 앞선 테스트(설치 조준, 입력 게이트 해제 등)가 End()를 부르고 나면 그 프레임 번호가
        /// 그대로 남아 뒤 테스트의 전투 입력이 통째로 삼켜진다.
        /// 정적 초기화는 런타임 훅이라 EditMode에서 돌지 않으므로 여기서 직접 되돌린다
        /// </summary>
        private static void ResetPlacementAim()
        {
            typeof(LocalPlacementAim)
                .GetMethod("ResetStatic", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, null);
        }

        private static void SetStatic(System.Type type, string property, object value)
        { type.GetProperty(property).SetValue(null, value); }
        private sealed class Clock : IRunTimeSource
        {
            public double GameplayElapsedSeconds { get; set; }
            public double FieldElapsedSeconds => GameplayElapsedSeconds;
            public double BossElapsedSeconds => 0;
            public bool IsPaused => false;
            public RunPauseReason PauseReasons => RunPauseReason.None;
        }
        private sealed class Loadout : MonoBehaviour, ICombatLoadoutProvider
        {
            public uint Revision => 1;
            public bool TryBuildLoadout(ParticipantId id, out CombatStatsSnapshot value)
            { value = new CombatStatsSnapshot { Revision = 1, MeleeAttackPower = 20, RangedAttackPower = 20,
                AttackSpeedMultiplier = 1, SpiritCostMultiplier = 1, StaminaCostMultiplier = 1,
                FieldRadius = 3, FieldDuration = 6, FieldTailDuration = 1.5f,
                FieldDamageMultiplier = 1.1f, EchoFieldDamageMultiplier = 1 }; return true; }
        }
        private sealed class Target : MonoBehaviour, IDamageable, IDamageHealthSource, ITargetPartIdSource
        {
            public float Health { get; private set; } = 1000;
            public bool IsDead => Health <= 0;
            public bool Open = true;
            public bool IsTargetable => Open;
            public byte TargetPartId => 1;
            public void TakeDamage(in DamageInfo info) { if (Open) { Health -= info.Amount; } }
        }
    }
}
