// System
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;

// Third party
using Mirror;
using NUnit.Framework;
using UnityEngine.TestTools;

// Project
using Geumeum.Common;

namespace Geumeum.Combat.Tests
{
    /// <summary> 연결 없는 서버 입력의 형식, 순서, 취소 경계를 검증한다 </summary>
    public sealed class PlayerServerInputTests
    {
        private const BindingFlags PRIVATE_INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _owner;
        private NetworkIdentity _identity;
        private bool _wasServerActive;

        [SetUp]
        public void SetUp()
        {
            _wasServerActive = NetworkServer.active;
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, true);
            _owner = new GameObject("ServerInputTest", typeof(NetworkIdentity), typeof(Rigidbody));
            _identity = _owner.GetComponent<NetworkIdentity>();
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isServer)).SetValue(_identity, true);
        }

        [TearDown]
        public void TearDown()
        {
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isServer)).SetValue(_identity, false);
            Object.DestroyImmediate(_owner);
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, _wasServerActive);
        }

        [Test]
        public void MovementRejectsInvalidAndDuplicateInputsWithoutPoisoningSequence()
        {
            PlayerController controller = Add<PlayerController>();
            Assert.IsFalse(controller.ServerReceiveMoveInput(new PlayerMoveInput
            {
                Sequence = 1u, MoveDirection = new Vector2(float.NaN, 0f),
            }));
            Assert.IsTrue(controller.ServerReceiveMoveInput(new PlayerMoveInput
            {
                Sequence = 1u, MoveDirection = new Vector2(3f, 4f),
            }));
            Assert.IsFalse(controller.ServerReceiveMoveInput(new PlayerMoveInput
            {
                Sequence = 1u, MoveDirection = Vector2.zero,
            }));
            var buffer = (Queue<PlayerMoveInput>)Get(controller, "_serverInputBuffer");
            Assert.AreEqual(1, buffer.Count);
            Assert.AreEqual(1f, buffer.Peek().MoveDirection.magnitude, 0.0001f);
        }

        [Test]
        public void MovementBufferStaysBoundedForServerInput()
        {
            PlayerController controller = Add<PlayerController>();
            int capacity = Constants.Network.PLAYER_SERVER_INPUT_BUFFER_MAX;
            for (uint sequence = 1u; sequence <= (uint)(capacity + 5); ++sequence)
            {
                Assert.IsTrue(controller.ServerReceiveMoveInput(new PlayerMoveInput
                {
                    Sequence = sequence, MoveDirection = Vector2.up,
                }));
            }

            var buffer = (Queue<PlayerMoveInput>)Get(controller, "_serverInputBuffer");
            Assert.AreEqual(capacity, buffer.Count);
            Assert.AreEqual(6u, buffer.Peek().Sequence);
        }

        [Test]
        public void ResultPhaseAbortsMovementEvenWhenSimulationTicksHaveStopped()
        {
            PlayerController controller = Add<PlayerController>();
            typeof(PlayerController).GetMethod("Awake", PRIVATE_INSTANCE).Invoke(controller, null);
            using (var brain = new PlayerBrain(controller))
            {
                Assert.IsTrue(controller.ServerReceiveMoveInput(new PlayerMoveInput
                {
                    Sequence = 1u, MoveDirection = Vector2.up,
                }));
                _owner.GetComponent<Rigidbody>().linearVelocity = Vector3.forward * 5f;
                typeof(PlayerBrain).GetMethod("OnPhaseChanged", PRIVATE_INSTANCE).Invoke(brain,
                    new object[] { new RunPhaseChangedEvent(RunPhase.Farming, RunPhase.Result) });
                Assert.AreEqual("Blocked", brain.ActiveNode);
                Assert.AreEqual(0, ((Queue<PlayerMoveInput>)Get(controller, "_serverInputBuffer")).Count);
                Assert.AreEqual(0f, _owner.GetComponent<Rigidbody>().linearVelocity.z);
            }
        }

        [Test]
        public void OwnerlessMovementDoesNotSendTargetSnapshot()
        {
            PlayerController controller = Add<PlayerController>();
            controller.ServerTeleport(Vector3.one);
            typeof(PlayerController).GetMethod("UpdateOwnerSnapshot", PRIVATE_INSTANCE)
                .Invoke(controller, new object[] { Vector2.up, 5f, 0.1f });
            Assert.AreEqual(Vector3.one, controller.transform.position);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void NonFiniteCombatInputIsRejectedBeforeSpendingResources()
        {
            PlayerAttack attack = Add<PlayerAttack>();
            PlayerRollController roll = Add<PlayerRollController>();
            Assert.AreEqual(ActionRejection.InvalidInput,
                attack.ServerRequestAttack(1u, new Vector2(float.PositiveInfinity, 0f), default));
            Assert.AreEqual(ActionRejection.InvalidInput,
                roll.ServerRequestRoll(1u, new Vector2(0f, float.NaN)));
            Assert.IsFalse(roll.IsRolling);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void OwnerlessParryReturnsRejectionWithoutSendingTargetRpc()
        {
            ParryController parry = Add<ParryController>();
            Assert.AreEqual(ParryResultCode.RejectedInvalidRequest, parry.ServerRequestParry(0u));
            Assert.AreEqual(ParryResultCode.RejectedDependency, parry.ServerRequestParry(1u));
            Assert.AreEqual(ParryResultCode.RejectedDuplicate, parry.ServerRequestParry(1u));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void OldRescueCancelCannotClearNewHold()
        {
            ReviveController revive = Add<ReviveController>();
            typeof(ReviveController).GetField("_serverHoldGeneration", PRIVATE_INSTANCE).SetValue(revive, 5u);
            typeof(ReviveController).GetField("_rescueTargetNetId", PRIVATE_INSTANCE).SetValue(revive, 42u);
            Assert.IsFalse(revive.ServerCancelRescueRequest(4u));
            Assert.AreEqual(42u, Get(revive, "_rescueTargetNetId"));
            Assert.IsTrue(revive.ServerCancelRescueRequest(5u));
            Assert.AreEqual(0u, Get(revive, "_rescueTargetNetId"));
        }

        [Test]
        public void RollFacingBeatsLockOnAndSnapsUntilTheRollEnds()
        {
            LockOnController lockOn = Add<LockOnController>();
            PlayerRollController roll = Add<PlayerRollController>();
            PlayerPresentation presentation = Add<PlayerPresentation>();

            // 서로를 참조하므로 전부 붙인 뒤 캐싱을 다시 시킨다
            Invoke(roll, "Awake");
            Invoke(presentation, "Awake");

            GameObject target = new GameObject("LockOnTarget");
            try
            {
                target.transform.position = new Vector3(0f, 0f, 10f);
                presentation.SetLockOnTarget(target.transform);
                Set(lockOn, "_targetTransform", target.transform);

                presentation.ServerSetMotion(Vector2.up, 3f);
                Assert.AreEqual(1f, presentation.FacingDirection.y, 1e-4f,
                    "구르기 전에는 락온 대상을 본다");
                Assert.IsFalse(presentation.UsesActionFacing);

                // 서버가 뒤구르기 방향을 확정한 상태
                presentation.ServerSetFacing(Vector2.down);
                Set(roll, "_isRolling", true);

                Assert.AreEqual(-1f, presentation.FacingDirection.y, 1e-4f,
                    "구르기 중에는 락온이 아니라 서버가 정한 구르기 방향을 본다");
                Assert.IsTrue(presentation.UsesActionFacing,
                    "구르기 회전을 보간하면 도는 동안 진행 방향과 모델이 어긋난다");

                // 방향 입력이 없는 락온 구르기는 진행 방향이 아니라 대상에서 물러나야 한다
                object[] arguments = { Vector2.zero };
                MethodInfo backStep = typeof(PlayerRollController)
                    .GetMethod("TryGetLockOnBackStep", PRIVATE_INSTANCE);
                Assert.IsTrue((bool)backStep.Invoke(roll, arguments));
                Assert.AreEqual(-1f, ((Vector2)arguments[0]).y, 1e-4f);

                Set(roll, "_isRolling", false);
                Assert.AreEqual(1f, presentation.FacingDirection.y, 1e-4f,
                    "구르기가 끝나면 다시 락온 대상을 본다");
                Assert.IsFalse(presentation.UsesActionFacing);
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void LocomotionReturnWaitsWhileRollingDeadOrHitReacting()
        {
            Assert.IsTrue(PlayerAnimationController.AllowsLocomotionReturn(false, false, false));
            Assert.IsFalse(PlayerAnimationController.AllowsLocomotionReturn(false, false, true),
                "구르기 중 Locomotion으로 되돌리면 같은 프레임의 Roll 트리거가 묻힌다");
            Assert.IsFalse(PlayerAnimationController.AllowsLocomotionReturn(true, false, false));
            Assert.IsFalse(PlayerAnimationController.AllowsLocomotionReturn(false, true, false));
        }

        private static void Invoke(object target, string method)
        {
            target.GetType().GetMethod(method, PRIVATE_INSTANCE).Invoke(target, null);
        }

        private static void Set(object target, string field, object value)
        {
            target.GetType().GetField(field, PRIVATE_INSTANCE).SetValue(target, value);
        }

        private T Add<T>() where T : NetworkBehaviour
        {
            T component = _owner.AddComponent<T>();
            typeof(NetworkIdentity).GetMethod("InitializeNetworkBehaviours", PRIVATE_INSTANCE)
                .Invoke(_identity, null);
            return component;
        }

        private static object Get(object target, string field)
        {
            return target.GetType().GetField(field, PRIVATE_INSTANCE).GetValue(target);
        }
    }
}
