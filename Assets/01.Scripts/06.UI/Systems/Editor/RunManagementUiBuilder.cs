// System
using System;
using System.Collections.Generic;

// Unity
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Project
using Geumeum.Systems;

namespace Geumeum.UI.Editor
{
    /// <summary> 열린 prefab contents에 기능 UI 자식만 추가한다. 저장은 호출자가 맡는다 </summary>
    public static class RunManagementUiBuilder
    {
        public const string ROOT_NAME = "FunctionalManagementUI";
        public const int SORTING_ORDER = 80;

        private static readonly Color G_Backdrop = new Color(0.025f, 0.035f, 0.055f, 0.88f);
        private static readonly Color G_Surface = new Color(0.075f, 0.095f, 0.13f, 0.98f);
        private static readonly Color G_SurfaceRaised = new Color(0.12f, 0.15f, 0.20f, 1f);
        private static readonly Color G_Primary = new Color(0.86f, 0.59f, 0.18f, 1f);
        private static readonly Color G_Text = new Color(0.96f, 0.95f, 0.91f, 1f);
        private static readonly Color G_MutedText = new Color(0.70f, 0.75f, 0.80f, 1f);

        /// <summary>
        /// PrefabUtility.LoadPrefabContents로 연 session root와 생산 TMP font를 넘긴다.
        /// 기존 자식이 있으면 중복 구성을 막고 false를 반환한다.
        /// </summary>
        public static bool Configure(GameObject sessionPrefabContents, TMP_FontAsset font)
        {
            if ((sessionPrefabContents == null) || (font == null))
            {
                Debug.LogError("[RunManagementUiBuilder] session prefab contents와 font가 필요하다");
                return false;
            }

            if (sessionPrefabContents.transform.Find(ROOT_NAME) != null)
            {
                Debug.LogError("[RunManagementUiBuilder] 기능 UI 자식이 이미 있다");
                return false;
            }

            CurrencyManager currency =
                sessionPrefabContents.GetComponentInChildren<CurrencyManager>(true);
            ShopTransactionService shopTransactions =
                sessionPrefabContents.GetComponentInChildren<ShopTransactionService>(true);
            EquipmentTransferService transferService =
                sessionPrefabContents.GetComponentInChildren<EquipmentTransferService>(true);
            if ((currency == null) || (shopTransactions == null) ||
                (transferService == null))
            {
                Debug.LogError("[RunManagementUiBuilder] 세션 거래 서비스를 찾지 못했다");
                return false;
            }

            GameObject root = null;
            try
            {
                root = CreateObject(ROOT_NAME, sessionPrefabContents.transform);
                Canvas canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.overrideSorting = true;
                canvas.sortingOrder = SORTING_ORDER;
                CanvasScaler scaler = root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280f, 720f);
                scaler.matchWidthOrHeight = 0.5f;
                root.AddComponent<GraphicRaycaster>();

                BuildManagement(root.transform, font, currency,
                    shopTransactions, transferService);
                BuildResult(root.transform, font);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[RunManagementUiBuilder] 기능 UI 구성 실패: {exception}");
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }

                return false;
            }
        }

        private static void BuildManagement(Transform parent, TMP_FontAsset font,
            CurrencyManager currency, ShopTransactionService shopTransactions,
            EquipmentTransferService transferService)
        {
            GameObject modal = CreateStretch("ManagementPanel", parent);
            Image backdrop = modal.AddComponent<Image>();
            backdrop.color = G_Backdrop;
            CanvasGroup canvasGroup = modal.AddComponent<CanvasGroup>();

            GameObject window = CreatePanel("Window", modal.transform,
                new Vector2(1120f, 640f), Vector2.zero, G_Surface);
            TMP_Text title = CreateText("Title", window.transform,
                "배낭", font, 30f, TextAlignmentOptions.Left,
                new Vector2(720f, 48f), new Vector2(-170f, 286f), G_Text);
            title.fontStyle = FontStyles.Bold;

            Button closeButton = CreateButton("Close", window.transform, "닫기",
                font, new Vector2(100f, 44f), new Vector2(495f, 286f), G_SurfaceRaised,
                out _);
            Button inventoryTab = CreateButton("InventoryTab", window.transform,
                "배낭", font, new Vector2(150f, 44f), new Vector2(-425f, 232f),
                G_Primary, out _);
            Button talentTab = CreateButton("TalentTab", window.transform,
                "특성", font, new Vector2(150f, 44f), new Vector2(-265f, 232f),
                G_SurfaceRaised, out _);
            Button shopTab = CreateButton("ShopTab", window.transform,
                "객주", font, new Vector2(150f, 44f), new Vector2(-105f, 232f),
                G_SurfaceRaised, out _);

            GameObject inventoryPage = CreatePage("InventoryPage", window.transform);
            GameObject talentPage = CreatePage("TalentPage", window.transform);
            GameObject shopPage = CreatePage("ShopPage", window.transform);

            InventoryReferences inventory = BuildInventoryPage(inventoryPage.transform, font);
            TalentReferences talent = BuildTalentPage(talentPage.transform, font);
            ShopReferences shop = BuildShopPage(shopPage.transform, font);

            FunctionalManagementPanel panel = modal.AddComponent<FunctionalManagementPanel>();
            SetReference(panel, "_canvasGroup", canvasGroup);
            SetReference(panel, "_closeButton", closeButton);
            SetReference(panel, "_inventoryTabButton", inventoryTab);
            SetReference(panel, "_talentTabButton", talentTab);
            SetReference(panel, "_shopTabButton", shopTab);
            SetReference(panel, "_inventoryPage", inventoryPage);
            SetReference(panel, "_talentPage", talentPage);
            SetReference(panel, "_shopPage", shopPage);
            SetReference(panel, "_equippedWeaponText", inventory.EquippedWeaponText);
            SetArray(panel, "_inventoryButtons", inventory.InventoryButtons);
            SetReference(panel, "_inventoryDetailText", inventory.DetailText);
            SetReference(panel, "_inventoryStatusText", inventory.StatusText);
            SetReference(panel, "_equipButton", inventory.EquipButton);
            SetReference(panel, "_unequipButton", inventory.UnequipButton);
            SetReference(panel, "_usePotionButton", inventory.UsePotionButton);
            SetReference(panel, "_useTicketButton", inventory.UseTicketButton);
            SetReference(panel, "_targetEquippedButton", inventory.TargetEquippedButton);
            SetReference(panel, "_talentPointText", talent.PointText);
            SetReference(panel, "_talentStatusText", talent.StatusText);
            SetArray(panel, "_talentColumnTexts", talent.ColumnTexts);
            SetArray(panel, "_talentButtons", talent.TalentButtons);
            SetReference(panel, "_talentResetButton", talent.ResetButton);
            SetReference(panel, "_walletText", shop.WalletText);
            SetReference(panel, "_shopHintText", shop.HintText);
            SetReference(panel, "_shopStatusText", shop.StatusText);
            SetArray(panel, "_shopButtons", shop.ShopButtons);
            SetReference(panel, "_transferStatusText", inventory.TransferStatusText);
            SetArray(panel, "_recipientButtons", inventory.RecipientButtons);
            SetReference(panel, "_acceptTransferButton", inventory.AcceptButton);
            SetReference(panel, "_rejectTransferButton", inventory.RejectButton);
            SetReference(panel, "_cancelTransferButton", inventory.CancelButton);
            SetReference(panel, "_currency", currency);
            SetReference(panel, "_shopTransactions", shopTransactions);
            SetReference(panel, "_transferService", transferService);

            talentPage.SetActive(false);
            shopPage.SetActive(false);
        }

        private static InventoryReferences BuildInventoryPage(Transform parent,
            TMP_FontAsset font)
        {
            var references = new InventoryReferences
            {
                EquippedWeaponText = CreateText("EquippedWeapon", parent,
                    "현재 장비 확인 중", font, 18f, TextAlignmentOptions.TopLeft,
                    new Vector2(520f, 110f), new Vector2(-270f, 145f), G_Text),
                InventoryButtons = new FunctionalUiIndexedButton[12],
                RecipientButtons = new FunctionalUiIndexedButton[2],
            };

            for (int i = 0; i < references.InventoryButtons.Length; ++i)
            {
                int column = i % 4;
                int row = i / 4;
                references.InventoryButtons[i] = CreateIndexedButton(
                    $"InventorySlot{i}", parent, "빈 칸", font, i,
                    new Vector2(132f, 66f),
                    new Vector2(-465f + (column * 140f), 45f - (row * 75f)),
                    G_SurfaceRaised);
            }

            references.DetailText = CreateText("InventoryDetail", parent,
                "물품을 선택해 주세요.", font, 18f, TextAlignmentOptions.TopLeft,
                new Vector2(380f, 105f), new Vector2(325f, 145f), G_Text);
            references.StatusText = CreateText("InventoryStatus", parent,
                string.Empty, font, 16f, TextAlignmentOptions.TopLeft,
                new Vector2(380f, 48f), new Vector2(325f, 70f), G_MutedText);
            references.EquipButton = CreateButton("Equip", parent, "장착",
                font, new Vector2(105f, 42f), new Vector2(200f, 15f), G_Primary,
                out _);
            references.UnequipButton = CreateButton("Unequip", parent, "해제",
                font, new Vector2(105f, 42f), new Vector2(315f, 15f),
                G_SurfaceRaised, out _);
            references.UsePotionButton = CreateButton("UsePotion", parent, "물약 사용",
                font, new Vector2(125f, 42f), new Vector2(445f, 15f), G_Primary,
                out _);
            references.TargetEquippedButton = CreateButton("TargetEquipped", parent,
                "장착 장비 대상", font, new Vector2(150f, 42f),
                new Vector2(245f, -35f), G_SurfaceRaised, out _);
            references.UseTicketButton = CreateButton("UseTicket", parent, "티켓 사용",
                font, new Vector2(150f, 42f), new Vector2(420f, -35f), G_Primary,
                out _);

            CreateText("TransferTitle", parent, "장비 양도", font, 20f,
                TextAlignmentOptions.Left, new Vector2(360f, 32f),
                new Vector2(325f, -92f), G_Text);
            for (int i = 0; i < references.RecipientButtons.Length; ++i)
            {
                references.RecipientButtons[i] = CreateIndexedButton(
                    $"Recipient{i}", parent, "받을 참가자 없음", font, i,
                    new Vector2(180f, 42f), new Vector2(230f + (i * 190f), -132f),
                    G_SurfaceRaised);
            }

            references.TransferStatusText = CreateText("TransferStatus", parent,
                "배낭 장비를 고른 뒤 참가자를 선택하세요.", font, 15f,
                TextAlignmentOptions.TopLeft, new Vector2(360f, 48f),
                new Vector2(325f, -177f), G_MutedText);
            references.AcceptButton = CreateButton("AcceptTransfer", parent, "수락",
                font, new Vector2(105f, 40f), new Vector2(195f, -228f),
                G_Primary, out _);
            references.RejectButton = CreateButton("RejectTransfer", parent, "거절",
                font, new Vector2(105f, 40f), new Vector2(315f, -228f),
                G_SurfaceRaised, out _);
            references.CancelButton = CreateButton("CancelTransfer", parent, "제안 취소",
                font, new Vector2(115f, 40f), new Vector2(445f, -228f),
                G_SurfaceRaised, out _);
            return references;
        }

        private static TalentReferences BuildTalentPage(Transform parent,
            TMP_FontAsset font)
        {
            var references = new TalentReferences
            {
                PointText = CreateText("TalentPoints", parent, "특성 확인 중",
                    font, 20f, TextAlignmentOptions.Left, new Vector2(340f, 38f),
                    new Vector2(-365f, 185f), G_Text),
                StatusText = CreateText("TalentStatus", parent, string.Empty,
                    font, 16f, TextAlignmentOptions.Left, new Vector2(600f, 38f),
                    new Vector2(-100f, -273f), G_MutedText),
                ColumnTexts = new TMP_Text[2],
                TalentButtons = new FunctionalUiIndexedButton[10],
            };

            for (int column = 0; column < 2; ++column)
            {
                float x = -245f + (column * 490f);
                references.ColumnTexts[column] = CreateText($"TalentColumn{column}",
                    parent, "특성", font, 22f, TextAlignmentOptions.Center,
                    new Vector2(360f, 38f), new Vector2(x, 135f), G_Text);
                for (int row = 0; row < 5; ++row)
                {
                    int index = (column * 5) + row;
                    references.TalentButtons[index] = CreateIndexedButton(
                        $"TalentNode{index}", parent, "특성 확인 중", font, index,
                        new Vector2(360f, 60f),
                        new Vector2(x, 82f - (row * 72f)), G_SurfaceRaised);
                }
            }

            references.ResetButton = CreateButton("TalentReset", parent, "특성 초기화",
                font, new Vector2(150f, 42f), new Vector2(400f, -273f),
                G_SurfaceRaised, out _);
            return references;
        }

        private static ShopReferences BuildShopPage(Transform parent,
            TMP_FontAsset font)
        {
            var references = new ShopReferences
            {
                WalletText = CreateText("Wallet", parent, "공용 노잣돈 확인 중",
                    font, 24f, TextAlignmentOptions.Center, new Vector2(520f, 42f),
                    new Vector2(0f, 170f), G_Text),
                HintText = CreateText("ShopHint", parent,
                    "객주 가까이에서 상호작용해 주세요.", font, 18f,
                    TextAlignmentOptions.Center, new Vector2(760f, 48f),
                    new Vector2(0f, 120f), G_MutedText),
                StatusText = CreateText("ShopStatus", parent, string.Empty,
                    font, 18f, TextAlignmentOptions.Center, new Vector2(760f, 48f),
                    new Vector2(0f, -190f), G_Text),
                ShopButtons = new FunctionalUiIndexedButton[4],
            };

            for (int i = 0; i < references.ShopButtons.Length; ++i)
            {
                int column = i % 2;
                int row = i / 2;
                references.ShopButtons[i] = CreateIndexedButton($"ShopItem{i}",
                    parent, "상품 확인 중", font, i, new Vector2(320f, 100f),
                    new Vector2(-170f + (column * 340f), 42f - (row * 116f)),
                    G_Primary);
            }

            return references;
        }

        private static void BuildResult(Transform parent, TMP_FontAsset font)
        {
            GameObject modal = CreateStretch("ResultPanel", parent);
            Image backdrop = modal.AddComponent<Image>();
            backdrop.color = G_Backdrop;
            CanvasGroup canvasGroup = modal.AddComponent<CanvasGroup>();
            GameObject window = CreatePanel("ResultWindow", modal.transform,
                new Vector2(940f, 600f), Vector2.zero, G_Surface);

            TMP_Text outcome = CreateText("Outcome", window.transform, "런 결과",
                font, 34f, TextAlignmentOptions.Center, new Vector2(780f, 52f),
                new Vector2(0f, 255f), G_Text);
            outcome.fontStyle = FontStyles.Bold;
            TMP_Text score = CreateText("Score", window.transform, string.Empty,
                font, 25f, TextAlignmentOptions.Center, new Vector2(780f, 42f),
                new Vector2(0f, 207f), G_Primary);
            TMP_Text summary = CreateText("Summary", window.transform, string.Empty,
                font, 18f, TextAlignmentOptions.TopLeft, new Vector2(350f, 130f),
                new Vector2(-245f, 106f), G_Text);
            TMP_Text evaluation = CreateText("Evaluation", window.transform,
                string.Empty, font, 17f, TextAlignmentOptions.TopLeft,
                new Vector2(390f, 130f), new Vector2(220f, 106f), G_MutedText);

            TMP_Text[] participants = new TMP_Text[3];
            for (int i = 0; i < participants.Length; ++i)
            {
                participants[i] = CreateText($"Participant{i}", window.transform,
                    string.Empty, font, 16f, TextAlignmentOptions.TopLeft,
                    new Vector2(280f, 250f), new Vector2(-300f + (i * 300f), -100f),
                    G_Text);
            }

            Button close = CreateButton("ResultClose", window.transform, "확인",
                font, new Vector2(160f, 46f), new Vector2(0f, -265f), G_Primary,
                out _);
            RunResultPanel panel = modal.AddComponent<RunResultPanel>();
            SetReference(panel, "_canvasGroup", canvasGroup);
            SetReference(panel, "_outcomeText", outcome);
            SetReference(panel, "_scoreText", score);
            SetReference(panel, "_summaryText", summary);
            SetReference(panel, "_evaluationText", evaluation);
            SetArray(panel, "_participantTexts", participants);
            SetReference(panel, "_closeButton", close);
        }

        private static GameObject CreatePage(string name, Transform parent)
        {
            GameObject page = CreateObject(name, parent);
            RectTransform rect = page.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1080f, 450f);
            rect.anchoredPosition = new Vector2(0f, -10f);
            return page;
        }

        private static GameObject CreateStretch(string name, Transform parent)
        {
            GameObject gameObject = CreateObject(name, parent);
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return gameObject;
        }

        private static GameObject CreatePanel(string name, Transform parent,
            Vector2 size, Vector2 position, Color color)
        {
            GameObject gameObject = CreateObject(name, parent);
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Image image = gameObject.AddComponent<Image>();
            image.color = color;
            return gameObject;
        }

        private static Button CreateButton(string name, Transform parent, string label,
            TMP_FontAsset font, Vector2 size, Vector2 position, Color color,
            out TMP_Text text)
        {
            GameObject gameObject = CreatePanel(name, parent, size, position, color);
            Button button = gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.96f, 0.84f, 1f);
            colors.pressedColor = new Color(0.76f, 0.78f, 0.82f, 1f);
            colors.disabledColor = new Color(0.35f, 0.37f, 0.40f, 0.65f);
            button.colors = colors;
            text = CreateText("Label", gameObject.transform, label, font, 17f,
                TextAlignmentOptions.Center, size, Vector2.zero, G_Text);
            return button;
        }

        private static FunctionalUiIndexedButton CreateIndexedButton(string name,
            Transform parent, string label, TMP_FontAsset font, int index,
            Vector2 size, Vector2 position, Color color)
        {
            Button button = CreateButton(name, parent, label, font, size, position,
                color, out TMP_Text text);
            FunctionalUiIndexedButton indexed =
                button.gameObject.AddComponent<FunctionalUiIndexedButton>();
            SetReference(indexed, "_button", button);
            SetReference(indexed, "_label", text);
            SetInteger(indexed, "_index", index);
            return indexed;
        }

        private static TMP_Text CreateText(string name, Transform parent, string value,
            TMP_FontAsset font, float fontSize, TextAlignmentOptions alignment,
            Vector2 size, Vector2 position, Color color)
        {
            GameObject gameObject = CreateObject(name, parent);
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            TextMeshProUGUI text = gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.text = value;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            return text;
        }

        private static GameObject CreateObject(string name, Transform parent)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static void SetReference(UnityEngine.Object target, string propertyName,
            UnityEngine.Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException($"직렬화 field가 없다: {propertyName}");
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInteger(UnityEngine.Object target, string propertyName,
            int value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException($"직렬화 field가 없다: {propertyName}");
            }

            property.intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray<T>(UnityEngine.Object target, string propertyName,
            T[] values) where T : UnityEngine.Object
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException($"직렬화 배열 field가 없다: {propertyName}");
            }

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; ++i)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private sealed class InventoryReferences
        {
            public TMP_Text EquippedWeaponText;
            public FunctionalUiIndexedButton[] InventoryButtons;
            public TMP_Text DetailText;
            public TMP_Text StatusText;
            public Button EquipButton;
            public Button UnequipButton;
            public Button UsePotionButton;
            public Button UseTicketButton;
            public Button TargetEquippedButton;
            public TMP_Text TransferStatusText;
            public FunctionalUiIndexedButton[] RecipientButtons;
            public Button AcceptButton;
            public Button RejectButton;
            public Button CancelButton;
        }

        private sealed class TalentReferences
        {
            public TMP_Text PointText;
            public TMP_Text StatusText;
            public TMP_Text[] ColumnTexts;
            public FunctionalUiIndexedButton[] TalentButtons;
            public Button ResetButton;
        }

        private sealed class ShopReferences
        {
            public TMP_Text WalletText;
            public TMP_Text HintText;
            public TMP_Text StatusText;
            public FunctionalUiIndexedButton[] ShopButtons;
        }
    }
}
