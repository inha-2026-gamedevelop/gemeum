// System
using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

// Unity
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

// Third party
using Mirror;
using NUnit.Framework;

// Project
using Geumeum.Common;
using Geumeum.Network;
using Geumeum.Systems;

namespace Geumeum.UI.Tests
{
    public sealed class FunctionalUiTests
    {
        private const BindingFlags PRIVATE_INSTANCE =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private bool _previousCameraInput;
        private CursorLockMode _previousCursorLock;
        private bool _previousCursorVisible;

        [SetUp]
        public void SetUp()
        {
            ResetInputGate();
            _previousCameraInput = CameraFrame.InputEnabled;
            _previousCursorLock = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;
            CameraFrame.InputEnabled = true;
        }

        [TearDown]
        public void TearDown()
        {
            ResetInputGate();
            CameraFrame.InputEnabled = _previousCameraInput;
            Cursor.lockState = _previousCursorLock;
            Cursor.visible = _previousCursorVisible;
        }

        [Test]
        public void NestedModalTokensBlockUntilLastReleaseAndMaintainAfterCameraEscape()
        {
            LocalGameplayInputToken first = LocalGameplayInputGate.Acquire();
            Assert.IsFalse(LocalGameplayInputGate.AllowsGameplayInput);
            Assert.IsFalse(CameraFrame.InputEnabled);

            CameraFrame.InputEnabled = true;
            LocalGameplayInputGate.Maintain(first);
            Assert.IsFalse(CameraFrame.InputEnabled);

            LocalGameplayInputToken second = LocalGameplayInputGate.Acquire();
            first.Dispose();
            Assert.IsFalse(LocalGameplayInputGate.AllowsGameplayInput);
            Assert.IsFalse(CameraFrame.InputEnabled);

            second.Dispose();
            Assert.IsTrue(LocalGameplayInputGate.AllowsGameplayInput);
            Assert.IsTrue(CameraFrame.InputEnabled);
        }

        [Test]
        public void ResultAdapterDistinguishesConfigurationFailureFromRealZeroScore()
        {
            RunResultSnapshot failedScore = CreateResult(0,
                RunEvaluationGrade.None, "점수 규칙을 확인해 주세요.");
            Assert.IsTrue(RunResultViewAdapter.TryCreate(failedScore,
                out RunResultViewData failedView));
            Assert.IsFalse(failedView.HasAuthoritativeScore);
            Assert.AreEqual(0, failedView.Score);
            Assert.AreEqual("점수 규칙을 확인해 주세요.",
                failedView.EvaluationMessage);

            RunResultSnapshot realZero = CreateResult(1,
                RunEvaluationGrade.D, string.Empty);
            Assert.IsTrue(RunResultViewAdapter.TryCreate(realZero,
                out RunResultViewData zeroView));
            Assert.IsTrue(zeroView.HasAuthoritativeScore);
            Assert.AreEqual(0, zeroView.Score);
        }

        [Test]
        public void LocalIdentityReplacementClosesModalForDirectSwapAndDestroyedPlayer()
        {
            GameObject panelObject = new GameObject("FunctionalPanelFixture");
            panelObject.SetActive(false);
            FunctionalManagementPanel panel =
                panelObject.AddComponent<FunctionalManagementPanel>();
            NetworkIdentity first = new GameObject("FirstLocalPlayer",
                typeof(NetworkIdentity)).GetComponent<NetworkIdentity>();
            NetworkIdentity second = new GameObject("SecondLocalPlayer",
                typeof(NetworkIdentity)).GetComponent<NetworkIdentity>();

            try
            {
                Set(panel, "_localIdentity", first);
                Set(panel, "_isVisible", true);
                Set(panel, "_inputToken", LocalGameplayInputGate.Acquire());

                Invoke(panel, "BindLocalPlayer", second);

                Assert.IsFalse((bool)Get(panel, "_isVisible"));
                Assert.IsNull(Get(panel, "_inputToken"));
                Assert.AreSame(second, Get(panel, "_localIdentity"));
                Assert.IsTrue(LocalGameplayInputGate.AllowsGameplayInput);

                Set(panel, "_isVisible", true);
                Set(panel, "_inputToken", LocalGameplayInputGate.Acquire());
                UnityEngine.Object.DestroyImmediate(second.gameObject);

                Invoke(panel, "BindLocalPlayer", (object)null);

                Assert.IsFalse((bool)Get(panel, "_isVisible"));
                Assert.IsNull(Get(panel, "_inputToken"));
                Assert.IsNull(Get(panel, "_localIdentity"));
                Assert.IsTrue(LocalGameplayInputGate.AllowsGameplayInput);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first.gameObject);
                if (!ReferenceEquals(second, null) && (second != null))
                {
                    UnityEngine.Object.DestroyImmediate(second.gameObject);
                }

                UnityEngine.Object.DestroyImmediate(panelObject);
            }
        }

        [Test]
        public void ResultManagerLossAndReplacementClearDisplayedModalEvenForSameRunId()
        {
            GameObject panelObject = new GameObject("ResultPanelFixture",
                typeof(CanvasGroup));
            panelObject.SetActive(false);
            RunResultPanel panel = panelObject.AddComponent<RunResultPanel>();
            Set(panel, "_canvasGroup", panelObject.GetComponent<CanvasGroup>());
            RunManager first = CreateInactiveRunManager("FirstRunManager", 1u);
            RunManager second = CreateInactiveRunManager("SecondRunManager", 1u);

            try
            {
                SetDisplayedResult(panel, first);
                Invoke(panel, "BindRun", (object)null);
                AssertResultPanelCleared(panel);

                SetDisplayedResult(panel, first);
                Invoke(panel, "BindRun", second);
                AssertResultPanelCleared(panel);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first.gameObject);
                UnityEngine.Object.DestroyImmediate(second.gameObject);
                UnityEngine.Object.DestroyImmediate(panelObject);
            }
        }

        [Test]
        public void OfferedRejectionMessageIsNotOverwrittenByCountdown()
        {
            GameObject panelObject = new GameObject("TransferPanelFixture");
            panelObject.SetActive(false);
            FunctionalManagementPanel panel =
                panelObject.AddComponent<FunctionalManagementPanel>();
            GameObject textObject = new GameObject("TransferStatus",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            TextMeshProUGUI status = textObject.GetComponent<TextMeshProUGUI>();
            Set(panel, "_transferStatusText", status);
            var result = new EquipmentTransferResult
            {
                Rejection = TransactionRejection.StaleInventoryRevision,
                Offer = new EquipmentTransferOfferSnapshot
                {
                    OfferId = 3u,
                    State = EquipmentTransferState.Offered,
                    ExpiresAtGameplayTime = 100d,
                },
            };

            try
            {
                Invoke(panel, "HandleTransferChanged", result);
                string rejection = status.text;
                Assert.IsNotEmpty(rejection);

                Invoke(panel, "RefreshTransferCountdown");

                Assert.AreEqual(rejection, status.text);
                Assert.IsTrue((bool)Get(panel, "_preserveTransferFeedback"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(textObject);
                UnityEngine.Object.DestroyImmediate(panelObject);
            }
        }

        [Test]
        public void RejectedAbandonRequestUnlocksRetry()
        {
            GameObject panelObject = new GameObject("RunStatusPanelFixture");
            panelObject.SetActive(false);
            RunStatusPanel panel = panelObject.AddComponent<RunStatusPanel>();
            try
            {
                Set(panel, "_requestSent", true);
                Invoke(panel, "HandleAbandonRequestCompleted", false);

                Assert.IsFalse((bool)Get(panel, "_requestSent"));
                Assert.IsTrue((bool)Get(panel, "_requestRejected"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(panelObject);
            }
        }

        [Test]
        public void SameRevisionInitialWeaponRefreshesCachedTalentAllocation()
        {
            GameObject player = new GameObject("TalentOwnerFixture",
                typeof(NetworkIdentity), typeof(TalentController));
            GameObject panelObject = new GameObject("TalentPanelFixture");
            panelObject.SetActive(false);
            FunctionalManagementPanel panel =
                panelObject.AddComponent<FunctionalManagementPanel>();
            TalentController talents = player.GetComponent<TalentController>();
            NetworkIdentity identity = player.GetComponent<NetworkIdentity>();

            try
            {
                Invoke(identity, "InitializeNetworkBehaviours");
                identity.GetType().GetProperty(nameof(NetworkIdentity.isOwned))
                    .SetValue(identity, true);
                Set(talents, "_revision", 1u);
                Set(talents, "_hasWeapon", true);
                Set(talents, "_weapon", WeaponKind.Bell);
                Set(panel, "_talents", talents);
                Set(panel, "_talentRevision", 1u);
                Set(panel, "_allocation", new TalentAllocationSnapshot(
                    1u, false, default, 0, Array.Empty<int>()));

                Invoke(panel, "RefreshTalentSnapshot");

                TalentAllocationSnapshot allocation =
                    (TalentAllocationSnapshot)Get(panel, "_allocation");
                Assert.AreEqual(1u, allocation.Revision);
                Assert.IsTrue(allocation.HasWeapon);
                Assert.AreEqual(WeaponKind.Bell, allocation.Weapon);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(panelObject);
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void AbandonTargetAcknowledgementNotifiesRequestingClient()
        {
            RunManager run = CreateInactiveRunManager("AbandonAckRunManager", 1u);
            bool? observed = null;
            run.AbandonParticipantWaitRequestCompleted += accepted => observed = accepted;
            try
            {
                Invoke(run,
                    "UserCode_TargetAbandonParticipantWaitRequestCompleted__NetworkConnection__Boolean",
                    (object)null, false);
                Assert.AreEqual(false, observed);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(run.gameObject);
            }
        }

        [Test]
        public void CorruptVerificationCheckpointLogsAndBlocksQueueReplay()
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "GeumeumVerification-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string commandPath = Path.Combine(directory, "client.commands.json");
            string responsePath = Path.Combine(directory, "client.commands.response.json");
            const string corruptCheckpoint = "{broken";
            File.WriteAllText(commandPath,
                "{\"executionId\":\"old-command\",\"commands\":[]}");
            File.WriteAllText(responsePath, corruptCheckpoint);

            GameObject driverObject = new GameObject("VerificationDriverFixture");
            driverObject.SetActive(false);
            VerificationCommandDriver driver =
                driverObject.AddComponent<VerificationCommandDriver>();
            try
            {
                LogAssert.Expect(LogType.Error,
                    new Regex("기존 응답 checkpoint를 읽지 못해 명령 재실행을 차단"));
                Invoke(driver, "Initialize", commandPath);
                Assert.IsTrue((bool)Get(driver, "_checkpointLoadFailed"));

                Invoke(driver, "TryProcessQueue");
                Assert.AreEqual(corruptCheckpoint, File.ReadAllText(responsePath));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(driverObject);
                Directory.Delete(directory, true);
            }
        }

        private static RunResultSnapshot CreateResult(int scoreRuleVersion,
            RunEvaluationGrade grade, string message)
        {
            return new RunResultSnapshot
            {
                SessionIdValue = 1UL,
                RunIdValue = 1u,
                ResultRevision = 1u,
                Mode = RunMode.Standard,
                Outcome = RunOutcome.Victory,
                FactsState = RunResultFactsState.Authoritative,
                Participants = Array.Empty<RunParticipantResultSnapshot>(),
                Score = 0,
                ScoreRuleVersion = scoreRuleVersion,
                EvaluationGrade = grade,
                EvaluationMessage = message,
            };
        }

        private static RunManager CreateInactiveRunManager(string name, uint runId)
        {
            GameObject gameObject = new GameObject(name);
            gameObject.SetActive(false);
            gameObject.AddComponent<NetworkIdentity>();
            RunManager run = gameObject.AddComponent<RunManager>();
            Set(run, "_runIdValue", runId);
            return run;
        }

        private static void SetDisplayedResult(RunResultPanel panel, RunManager run)
        {
            Set(panel, "_boundRun", run);
            Set(panel, "_displayedSessionId", 1UL);
            Set(panel, "_displayedRunId", 1u);
            Set(panel, "_displayedRevision", 1u);
            Set(panel, "_hasDisplayedResult", true);
            Set(panel, "_isVisible", true);
            Set(panel, "_inputToken", LocalGameplayInputGate.Acquire());
        }

        private static void AssertResultPanelCleared(RunResultPanel panel)
        {
            Assert.IsFalse((bool)Get(panel, "_hasDisplayedResult"));
            Assert.IsFalse((bool)Get(panel, "_isVisible"));
            Assert.IsNull(Get(panel, "_inputToken"));
            Assert.IsTrue(LocalGameplayInputGate.AllowsGameplayInput);
        }

        private static object Invoke(object target, string methodName,
            params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, PRIVATE_INSTANCE);
            Assert.IsNotNull(method, methodName);
            return method.Invoke(target, arguments);
        }

        private static object Get(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, PRIVATE_INSTANCE);
            Assert.IsNotNull(field, fieldName);
            return field.GetValue(target);
        }

        private static void Set(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, PRIVATE_INSTANCE);
            Assert.IsNotNull(field, fieldName);
            field.SetValue(target, value);
        }

        private static void ResetInputGate()
        {
            typeof(LocalGameplayInputGate).GetMethod("ResetStatic",
                BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        }
    }
}
