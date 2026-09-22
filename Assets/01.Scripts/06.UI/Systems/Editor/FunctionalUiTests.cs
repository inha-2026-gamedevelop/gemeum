// System
using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

// Unity
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

// Third party
using Mirror;
using NUnit.Framework;

// Project
using Geumeum.Common;
using Geumeum.Data;
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

        [Test]
        public void ProductionInventoryExposesEquipInsteadOfOverlappingUnequip()
        {
            var source = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/02.Prefabs/UI/RunManagementUI.prefab");
            var root = UnityEngine.Object.Instantiate(source);
            root.SetActive(false);
            try
            {
                var panel = root.GetComponentInChildren<FunctionalManagementPanel>(true);
                var slots = (InventorySlotSnapshot[])Get(panel, "_slots");
                slots[0] = new InventorySlotSnapshot { Kind = InventoryItemKind.Equipment,
                    Equipment = new WeaponCombatSnapshot { InstanceId = 17u, WeaponKind = WeaponKind.Sword } };
                Set(panel, "_selectedSlot", 0);
                Invoke(panel, "RefreshInventoryView");
                var equip = (Button)Get(panel, "_equipButton");
                var unequip = (Button)Get(panel, "_unequipButton");
                Assert.IsTrue(equip.gameObject.activeSelf);
                Assert.IsTrue(equip.interactable);
                Assert.IsFalse(unequip.gameObject.activeSelf, "해제 버튼이 장착 클릭을 가리면 안 된다");
                Set(panel, "_selectedSlot", -1);
                Invoke(panel, "RefreshInventoryView");
                Assert.IsFalse(equip.gameObject.activeSelf);
                Assert.IsTrue(unequip.gameObject.activeSelf);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void ProductionTabTextAndTitleAreConnected()
        {
            var source = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/02.Prefabs/UI/RunManagementUI.prefab");
            var root = UnityEngine.Object.Instantiate(source);
            root.SetActive(false);
            try
            {
                var panel = root.GetComponentInChildren<FunctionalManagementPanel>(true);
                var row = (RailRowVisual)Get(panel, "_talentRow");
                row.SetActiveRow(true);
                Assert.IsTrue(((TMP_Text)Get(row, "_label")).raycastTarget);
                Assert.NotNull(Get(panel, "_pageTitle"));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void PickupFailureNeverReportsInventorySuccess()
        {
            string full = InteractionPromptView.FormatResult(Geumeum.Systems.InteractionResult.Success,
                InventoryResult.Full, new WeaponCombatSnapshot { InstanceId = 1u });
            StringAssert.Contains("빈자리", full);
            StringAssert.DoesNotContain("넣었습니다", full);
            string combat = InteractionPromptView.FormatResult(Geumeum.Systems.InteractionResult.InCombat,
                InventoryResult.InvalidItem, default);
            StringAssert.Contains("전투", combat);
        }

        [Test]
        public void TalentTooltipShowsSoValueCostPrerequisiteAndEveryState()
        {
            TalentTreeSO tree = LoadProductionTalentTree();

            // 노드 ID를 테스트에 박지 않고 선행이 하나인 노드를 데이터에서 고른다
            TalentTreeSO.NodeEntry child = default;
            TalentTreeSO.NodeEntry parent = default;
            TalentTreeSO.NodeEntry rootNode = default;
            for (int i = 0; i < tree.NodeCount; ++i)
            {
                if (!tree.TryGetNodeAt(i, out TalentTreeSO.NodeEntry candidate))
                {
                    continue;
                }

                if ((rootNode.NodeId == 0) && (!candidate.HasExplicitPrerequisites) &&
                    (candidate.RowIndex == 0))
                {
                    rootNode = candidate;
                }

                if ((child.NodeId == 0) && candidate.HasExplicitPrerequisites &&
                    (candidate.Prerequisites.Length == 1) &&
                    tree.TryGetNode(candidate.Prerequisites[0], out parent))
                {
                    child = candidate;
                }
            }

            Assert.AreNotEqual(0, child.NodeId, "선행이 하나인 특성 노드를 찾지 못했다");
            Assert.AreNotEqual(0, rootNode.NodeId, "선행이 없는 특성 노드를 찾지 못했다");

            int total = parent.PointCost + child.PointCost;
            var none = new TalentAllocationSnapshot(1u, true, child.WeaponKind, 0,
                Array.Empty<int>());
            var hasParent = new TalentAllocationSnapshot(1u, true, child.WeaponKind,
                parent.PointCost, new[] { parent.NodeId });
            var hasBoth = new TalentAllocationSnapshot(1u, true, child.WeaponKind, total,
                new[] { parent.NodeId, child.NodeId });

            // 실제 SO 수치와 비용, 선행 이름이 그대로 보여야 한다
            string locked = FunctionalUiText.BuildTalentTooltip(tree, none, total, child);
            StringAssert.Contains(FunctionalUiText.GetTalentEffectName(child.EffectKind), locked);
            StringAssert.Contains(
                FunctionalUiText.DescribeTalentValue(child.EffectKind, child.Value), locked);
            StringAssert.Contains($"필요 포인트 {child.PointCost}", locked);
            StringAssert.Contains(FunctionalUiText.GetTalentNodeName(parent) + "(필요)", locked);
            StringAssert.Contains("선행 특성을 먼저", locked);

            string ready = FunctionalUiText.BuildTalentTooltip(tree, hasParent, total, child);
            StringAssert.Contains(FunctionalUiText.GetTalentNodeName(parent) + "(보유)", ready);
            StringAssert.Contains("상태: 선택 가능", ready);

            // 선행은 있지만 남은 포인트가 없는 경우
            string poor = FunctionalUiText.BuildTalentTooltip(tree, hasParent,
                parent.PointCost, child);
            StringAssert.Contains("남은 포인트 0", poor);
            StringAssert.Contains("포인트 부족", poor);

            string owned = FunctionalUiText.BuildTalentTooltip(tree, hasBoth, total, child);
            StringAssert.Contains("획득함", owned);

            string root = FunctionalUiText.BuildTalentTooltip(tree, none, total, rootNode);
            StringAssert.Contains("선행 특성 없음", root);
        }

        [Test]
        public void TalentTooltipAppearsOnHoverAndClearsOnTabChangeAndClose()
        {
            TalentPanelFixture fixture = CreateTalentPanel();
            try
            {
                FunctionalManagementPanel panel = fixture.Panel;
                var tooltip = (GameObject)Get(panel, "_talentTooltip");
                Assert.IsNotNull(tooltip, "특성 설명 상자가 만들어지지 않았다");
                Assert.IsFalse(tooltip.activeSelf, "가리키기 전에는 숨어 있어야 한다");
                Assert.IsFalse(tooltip.GetComponent<Image>().raycastTarget,
                    "설명이 레이캐스트를 먹으면 가리킨 노드를 고를 수 없다");
                Assert.IsFalse(((TMP_Text)Get(panel, "_talentTooltipText")).raycastTarget);

                int[] visible = (int[])Get(panel, "_visibleTalentNodeIds");
                int hovered = Array.FindIndex(visible, id => id > 0);
                Assert.GreaterOrEqual(hovered, 0, "그려진 특성 노드가 없다");
                Assert.IsTrue(fixture.Tree.TryGetNode(visible[hovered],
                    out TalentTreeSO.NodeEntry node));

                Invoke(panel, "HandleTalentHover", hovered, true);
                Assert.AreEqual(hovered, Get(panel, "_hoveredTalentIndex"));
                Assert.IsTrue(tooltip.activeSelf, "가리킨 노드의 설명이 떠야 한다");
                Assert.AreEqual(tooltip.transform.parent.childCount - 1,
                    tooltip.transform.GetSiblingIndex(),
                    "설명은 연결선과 노드보다 위에 그려져야 한다");

                string shown = ((TMP_Text)Get(panel, "_talentTooltipText")).text;
                StringAssert.Contains(FunctionalUiText.GetTalentNodeName(node), shown);
                StringAssert.Contains(
                    FunctionalUiText.GetTalentEffectName(node.EffectKind), shown);
                StringAssert.Contains(
                    FunctionalUiText.GetTalentEffectDetail(node.EffectKind, node.Value), shown);

                // 마우스는 새 노드에, 키보드 선택 해제는 옛 노드에 오는 순서를 흉내 낸다
                int other = Array.FindIndex(visible, hovered + 1, id => id > 0);
                if (other > 0)
                {
                    Invoke(panel, "HandleTalentHover", other, true);
                    Invoke(panel, "HandleTalentHover", hovered, false);
                    Assert.AreEqual(other, Get(panel, "_hoveredTalentIndex"),
                        "늦게 도착한 이전 노드의 해제가 새로 가리킨 노드를 지우면 안 된다");
                    Assert.IsTrue(tooltip.activeSelf);
                }

                Invoke(panel, "SetPage", ResolvePage("Inventory"));
                Assert.AreEqual(-1, Get(panel, "_hoveredTalentIndex"),
                    "탭을 옮기면 남은 설명을 지운다");
                Assert.IsFalse(tooltip.activeSelf);

                // 특성 탭이 아닌 동안에는 hover가 들어와도 뜨면 안 된다
                Invoke(panel, "HandleTalentHover", hovered, true);
                Assert.IsFalse(tooltip.activeSelf,
                    "다른 탭에서는 특성 설명이 뜨면 안 된다");

                Invoke(panel, "SetPage", ResolvePage("Talent"));
                Invoke(panel, "HandleTalentHover", hovered, true);
                Assert.IsTrue(tooltip.activeSelf);
                Invoke(panel, "SetVisible", false);
                Assert.AreEqual(-1, Get(panel, "_hoveredTalentIndex"),
                    "창을 닫으면 남은 설명을 지운다");
                Assert.IsFalse(tooltip.activeSelf);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void TalentTooltipSurvivesRepeatedRefreshWithoutDeactivatingHoveredNode()
        {
            TalentPanelFixture fixture = CreateTalentPanel();
            try
            {
                FunctionalManagementPanel panel = fixture.Panel;
                var tooltip = (GameObject)Get(panel, "_talentTooltip");
                var buttons = (FunctionalUiIndexedButton[])Get(panel, "_talentButtons");
                int[] visible = (int[])Get(panel, "_visibleTalentNodeIds");
                int hovered = Array.FindIndex(visible, id => id > 0);
                Assert.GreaterOrEqual(hovered, 0, "그려진 특성 노드가 없다");

                int nodeId = visible[hovered];
                FunctionalUiIndexedButton button = buttons[hovered];
                Assert.IsTrue(button.gameObject.activeSelf);

                // 실제 uGUI 전달 경로로 포인터를 올린다
                var probe = button.gameObject.AddComponent<DisableProbe>();
                ExecuteEvents.Execute(button.gameObject, new PointerEventData(null),
                    ExecuteEvents.pointerEnterHandler);
                Assert.AreEqual(hovered, Get(panel, "_hoveredTalentIndex"));
                Assert.IsTrue(tooltip.activeSelf);

                // 0.2초마다 도는 갱신을 3번 반복해도 설명이 살아 있어야 한다
                for (int i = 0; i < 3; ++i)
                {
                    Invoke(panel, "RefreshTalentView");
                    Assert.AreEqual(hovered, Get(panel, "_hoveredTalentIndex"),
                        $"{i + 1}번째 갱신에서 가리킨 노드를 놓쳤다");
                    Assert.IsTrue(tooltip.activeSelf,
                        $"{i + 1}번째 갱신에서 설명이 사라졌다");
                    Assert.IsTrue(button.gameObject.activeSelf);
                }

                Assert.AreEqual(0, probe.DisableCount,
                    "쓰는 노드를 껐다 켜면 포인터가 그대로여도 hover가 끊긴다");
                Assert.AreEqual(nodeId, visible[hovered], "갱신 뒤에도 같은 노드여야 한다");

                // 무기가 바뀌어 그 칸의 노드가 달라지면 이전 hover는 놓아야 한다
                Set(panel, "_allocation", new TalentAllocationSnapshot(1u, true,
                    WeaponKind.Bell, 0, Array.Empty<int>()));
                Invoke(panel, "RefreshTalentView");
                if (visible[hovered] != nodeId)
                {
                    Assert.AreEqual(-1, Get(panel, "_hoveredTalentIndex"),
                        "다른 노드로 바뀐 칸의 설명을 그대로 두면 안 된다");
                    Assert.IsFalse(tooltip.activeSelf);
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void TalentTooltipDetailExplainsRealTriggerConditionsAndConfigValues()
        {
            CombatConfigSO combat = UnityEditor.AssetDatabase.LoadAssetAtPath<CombatConfigSO>(
                "Assets/09.Data/Combat/CombatConfigSO.asset");
            Assert.IsNotNull(combat, "생산 전투 설정 자산을 찾지 못했다");

            GameDatabaseSO database = ScriptableObject.CreateInstance<GameDatabaseSO>();
            Set(database, "_combat", combat);
            Set(database, "_talent", LoadProductionTalentTree());
            GameDB.Initialize(database);
            try
            {
                // 반격 표식 수가 조건인 노드들
                string counterRecovery = FunctionalUiText.GetTalentEffectDetail(
                    TalentEffectKind.CounterRecoveryReduction, 0.1f);
                StringAssert.Contains("2개 이상", counterRecovery);
                StringAssert.Contains("적중", counterRecovery);
                StringAssert.Contains("빗나가면", counterRecovery);

                string thirdCombo = FunctionalUiText.GetTalentEffectDetail(
                    TalentEffectKind.CounterThirdCombo, 1f);
                StringAssert.Contains("표식 3개", thirdCombo);
                StringAssert.Contains("3단", thirdCombo);
                StringAssert.Contains($"{combat.CombatTokenDuration:0.##}초", thirdCombo);

                string secondCombo = FunctionalUiText.GetTalentEffectDetail(
                    TalentEffectKind.SkillSecondCombo, 1f);
                StringAssert.Contains("표식 없이", secondCombo);
                StringAssert.Contains("2단", secondCombo);

                string missRecovery = FunctionalUiText.GetTalentEffectDetail(
                    TalentEffectKind.MissSkillRecoveryReduction, 0.2f);
                StringAssert.Contains("빗나갔을 때만", missRecovery);

                // 방울 공명진 연계 조건
                string echoField = FunctionalUiText.GetTalentEffectDetail(
                    TalentEffectKind.EchoFieldDamagePercent, 20f);
                StringAssert.Contains("회수하는 적이 자기 공명진 안", echoField);
                StringAssert.Contains("시전자", echoField);

                // 창 동안 매번이 아니라 다음 회수 1회에만 붙는다
                string healDiscount = FunctionalUiText.GetTalentEffectDetail(
                    TalentEffectKind.FieldHealEchoDiscount, 2f);
                StringAssert.Contains("실제로 회복", healDiscount);
                StringAssert.Contains("초과 치유", healDiscount);
                StringAssert.Contains("한 번", healDiscount);
                StringAssert.Contains($"{combat.BellFieldHealDiscountWindow:0.##}초", healDiscount);

                // 처음 치유한 대상만이 아니라 진 안 아군 전부가 받는다
                string afterglow = FunctionalUiText.GetTalentEffectDetail(
                    TalentEffectKind.FieldHealAfterglowPercent, 30f);
                StringAssert.Contains("실제로 회복", afterglow);
                StringAssert.Contains("진 안에 있는 아군", afterglow);
                StringAssert.Contains("처음 치유한 대상만이 아니다", afterglow);
                StringAssert.Contains("한 진에 한 번", afterglow);

                // 잔향 시간이 아니라 공명진 수명이 늘어나는 노드다. 조건은 시전자 위치다
                Assert.AreEqual("공명진 지속 시간 연장", FunctionalUiText.GetTalentEffectName(
                    TalentEffectKind.FieldEchoExtendSeconds));
                string echoExtend = FunctionalUiText.GetTalentEffectDetail(
                    TalentEffectKind.FieldEchoExtendSeconds, 0.5f);
                StringAssert.Contains("시전자가 자기 공명진 안", echoExtend);
                StringAssert.Contains("지속 시간", echoExtend);
                StringAssert.Contains("한 진에 한 번", echoExtend);
                StringAssert.DoesNotContain("잔향 시간", echoExtend);

                // 반경을 넓히는 대신 지속이 줄어드는 노드. 벌점은 CombatConfigSO가 가진 값이다
                string widening = FunctionalUiText.GetTalentEffectDetail(
                    TalentEffectKind.FieldWideningRadiusBonus, 0.7f);
                StringAssert.Contains("0.7m", widening);
                StringAssert.Contains($"{combat.BellFieldWideningDurationPenalty:0.##}초", widening);
                StringAssert.Contains("짧아진다", widening);
                StringAssert.DoesNotContain("노드를 사야", widening);

                // 같은 화살이 이어서 관통한 다음 적에게 붙는다. 새 화살도, 같은 대상도 아니다
                string followThrough = FunctionalUiText.GetTalentEffectDetail(
                    TalentEffectKind.PrecisionFollowThrough, 0.4f);
                StringAssert.Contains("쐐기를 터뜨리면", followThrough);
                StringAssert.Contains("관통해 맞히는 다음 적", followThrough);
                StringAssert.Contains("40%", followThrough);
                StringAssert.DoesNotContain("곧바로", followThrough);

                string wedgeFirst = FunctionalUiText.GetTalentEffectDetail(
                    TalentEffectKind.WedgeFirstHitCooldown, 1f);
                StringAssert.Contains("처음 적중", wedgeFirst);
                StringAssert.Contains("두 번째", wedgeFirst);

                // 트리에 실제로 쓰이는 39개 효과에는 모두 설명이 있어야 한다
                TalentTreeSO tree = LoadProductionTalentTree();
                for (int i = 0; i < tree.NodeCount; ++i)
                {
                    Assert.IsTrue(tree.TryGetNodeAt(i, out TalentTreeSO.NodeEntry node));
                    string detail = FunctionalUiText.GetTalentEffectDetail(
                        node.EffectKind, node.Value);
                    Assert.AreNotEqual("효과 설명이 아직 없다", detail,
                        $"{node.NodeId}({node.EffectKind})에 상세 설명이 없다");
                    Assert.Greater(detail.Length,
                        FunctionalUiText.GetTalentEffectName(node.EffectKind).Length,
                        $"{node.NodeId}의 설명이 이름보다 짧다");
                }
            }
            finally
            {
                GameDB.ResetStatic();
                UnityEngine.Object.DestroyImmediate(database);
            }
        }

        [Test]
        public void TalentTooltipWrapsLongDescriptionInsideBoxEvenWhenLabelStartsWithoutWrapping()
        {
            TalentPanelFixture fixture = CreateTalentPanel();
            try
            {
                FunctionalManagementPanel panel = fixture.Panel;
                var tooltip = (GameObject)Get(panel, "_talentTooltip");
                var label = (TMP_Text)Get(panel, "_talentTooltipText");

                // 런타임 라벨이 줄바꿈 없음으로 시작하는 상태를 재현한다. 상자가 스스로 켜야 한다
                label.textWrappingMode = TextWrappingModes.NoWrap;

                // 설명 한 줄이 가장 긴 노드를 고른다. 짧은 노드는 접지 않아도 폭 안에 들어간다
                int[] visible = (int[])Get(panel, "_visibleTalentNodeIds");
                int hovered = -1;
                int longestSegment = 0;
                for (int i = 0; i < visible.Length; ++i)
                {
                    if ((visible[i] <= 0) ||
                        !fixture.Tree.TryGetNode(visible[i], out TalentTreeSO.NodeEntry candidate))
                    {
                        continue;
                    }

                    string detail = FunctionalUiText.GetTalentEffectDetail(
                        candidate.EffectKind, candidate.Value);
                    foreach (string segment in detail.Split('\n'))
                    {
                        if (segment.Length <= longestSegment) { continue; }
                        longestSegment = segment.Length;
                        hovered = i;
                    }
                }

                Assert.GreaterOrEqual(hovered, 0, "그려진 특성 노드가 없다");
                Invoke(panel, "HandleTalentHover", hovered, true);
                Assert.IsTrue(tooltip.activeSelf, "설명이 떠야 폭을 잴 수 있다");

                Assert.AreEqual(TextWrappingModes.Normal, label.textWrappingMode,
                    "설명 라벨이 줄바꿈 없이 남으면 긴 글이 상자 밖으로 가로로 흘러 나간다");
                Assert.AreNotEqual(TextOverflowModes.Truncate, label.overflowMode,
                    "글을 잘라내는 방식으로 해결하면 안 된다");
                Assert.AreNotEqual(TextOverflowModes.Ellipsis, label.overflowMode,
                    "글을 줄임표로 감추는 방식으로 해결하면 안 된다");

                // 설명이 끝까지 남아 있어야 한다
                Assert.IsTrue(fixture.Tree.TryGetNode(visible[hovered],
                    out TalentTreeSO.NodeEntry hoveredNode));
                StringAssert.Contains(FunctionalUiText.GetTalentEffectDetail(
                    hoveredNode.EffectKind, hoveredNode.Value), label.text);

                Canvas.ForceUpdateCanvases();
                label.ForceMeshUpdate();

                RectTransform tooltipRect = (RectTransform)tooltip.transform;
                Assert.Greater(tooltipRect.rect.width, 1f, "상자 폭을 잴 수 없어 검증이 무의미하다");

                // 줄바꿈 문자로 생기는 줄보다 실제 줄이 많아야 실제로 접힌 것이다.
                // isTextOverflowing은 이 결함에서 false로 나와 근거가 되지 못한다
                int explicitLines = 1;
                foreach (char character in label.text)
                {
                    if (character == '\n') { ++explicitLines; }
                }

                Assert.Greater(label.textInfo.lineCount, explicitLines,
                    "긴 설명 줄이 접히지 않았다");

                // 접힌 글이 상자 폭 안에 들어간다
                Assert.Greater(label.rectTransform.rect.width, 0f);
                Assert.LessOrEqual(label.textBounds.size.x, label.rectTransform.rect.width + 0.5f,
                    "설명이 글 영역 폭을 넘어 가로로 흘러 나간다");
                Assert.LessOrEqual(label.rectTransform.rect.width, tooltipRect.rect.width,
                    "글 영역이 상자보다 넓다");
            }
            finally
            {
                fixture.Dispose();
            }
        }

        private sealed class DisableProbe : MonoBehaviour
        {
            public int DisableCount;
            private void OnDisable() { ++DisableCount; }
        }

        /// <summary> 생산 프리팹과 생산 SO로 특성 탭이 열린 상태를 만든다 </summary>
        private sealed class TalentPanelFixture
        {
            public GameObject Root;
            public FunctionalManagementPanel Panel;
            public GameDatabaseSO Database;
            public TalentTreeSO Tree;

            public void Dispose()
            {
                if (Root != null) { UnityEngine.Object.DestroyImmediate(Root); }
                GameDB.ResetStatic();
                if (Database != null) { UnityEngine.Object.DestroyImmediate(Database); }
            }
        }

        private static TalentPanelFixture CreateTalentPanel()
        {
            CombatConfigSO combat = UnityEditor.AssetDatabase.LoadAssetAtPath<CombatConfigSO>(
                "Assets/09.Data/Combat/CombatConfigSO.asset");
            Assert.IsNotNull(combat, "생산 전투 설정 자산을 찾지 못했다");

            var fixture = new TalentPanelFixture { Tree = LoadProductionTalentTree() };
            fixture.Database = ScriptableObject.CreateInstance<GameDatabaseSO>();
            Set(fixture.Database, "_talent", fixture.Tree);
            Set(fixture.Database, "_combat", combat);
            GameDB.Initialize(fixture.Database);

            var source = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/02.Prefabs/UI/RunManagementUI.prefab");
            // ExecuteEvents는 비활성 오브젝트의 컴포넌트를 건너뛴다.
            // 실제 PointerEnter 경로를 쓰려면 계층이 살아 있어야 한다
            fixture.Root = UnityEngine.Object.Instantiate(source);
            fixture.Panel = fixture.Root.GetComponentInChildren<FunctionalManagementPanel>(true);
            Assert.IsNotNull(fixture.Panel, "관리 창 프리팹에 패널이 없다");

            // EditMode는 ExecuteAlways가 아닌 MonoBehaviour의 Awake를 부르지 않는다.
            // 설명 상자와 버튼 구독이 생산과 같은 초기화를 실제로 거치게 한다
            if (Get(fixture.Panel, "_talentTooltip") == null)
            {
                Invoke(fixture.Panel, "Awake");
                foreach (FunctionalUiIndexedButton button in
                    (FunctionalUiIndexedButton[])Get(fixture.Panel, "_talentButtons"))
                {
                    if (button != null) { Invoke(button, "Awake"); }
                }
            }

            Assert.IsNotNull(Get(fixture.Panel, "_talentTooltip"),
                "Awake를 거쳤는데도 특성 설명 상자가 만들어지지 않았다");

            Set(fixture.Panel, "_talentRevision", 1u);
            Set(fixture.Panel, "_allocation", new TalentAllocationSnapshot(1u, true,
                WeaponKind.Sword, 0, Array.Empty<int>()));
            Set(fixture.Panel, "_isVisible", true);
            Invoke(fixture.Panel, "SetPage", ResolvePage("Talent"));
            Invoke(fixture.Panel, "RefreshTalentView");
            return fixture;
        }

        private static object ResolvePage(string name)
        {
            Type pageKind = typeof(FunctionalManagementPanel)
                .GetNestedType("PageKind", BindingFlags.NonPublic);
            Assert.IsNotNull(pageKind, "PageKind를 찾지 못했다");
            return Enum.Parse(pageKind, name);
        }

        [Test]
        public void LockedTalentNodeStillReportsHoverWithoutBlockingClicks()
        {
            GameObject node = new GameObject("TalentNode", typeof(RectTransform),
                typeof(Image), typeof(Button), typeof(FunctionalUiIndexedButton));
            try
            {
                var indexed = node.GetComponent<FunctionalUiIndexedButton>();
                indexed.SetIndex(7);
                indexed.SetInteractable(false);
                Assert.IsFalse(indexed.Interactable);

                int seen = -1;
                bool entered = false;
                indexed.HoverChanged += (index, isEntered) =>
                {
                    seen = index;
                    entered = isEntered;
                };

                // 아직 고를 수 없는 노드에도 포인터 이벤트는 그대로 전달된다
                ((IPointerEnterHandler)indexed).OnPointerEnter(new PointerEventData(null));
                Assert.AreEqual(7, seen);
                Assert.IsTrue(entered, "잠긴 노드도 설명을 띄울 수 있어야 한다");
                Assert.IsTrue(node.GetComponent<Image>().raycastTarget,
                    "레이캐스트가 꺼지면 hover 자체가 오지 않는다");

                ((IPointerExitHandler)indexed).OnPointerExit(new PointerEventData(null));
                Assert.IsFalse(entered);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(node);
            }
        }

        private static TalentTreeSO LoadProductionTalentTree()
        {
            TalentTreeSO tree = UnityEditor.AssetDatabase.LoadAssetAtPath<TalentTreeSO>(
                "Assets/09.Data/Talent/TalentTreeSO.asset");
            Assert.IsNotNull(tree, "생산 특성 트리 자산을 찾지 못했다");
            return tree;
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
