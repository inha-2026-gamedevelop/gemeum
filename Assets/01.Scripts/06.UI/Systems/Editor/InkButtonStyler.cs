// Unity
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Geumeum.UI.Editor
{
    /// <summary>
    /// 관리 창의 버튼과 배낭 칸을 먹 버튼 스타일로 맞춘다. 스타일의 정본은 이 파일 하나다
    ///
    /// 주요 행동은 주칠 판에 낙관, 보조 행동은 먹 판, 메뉴 탭은 한지 두루마리, 배낭 칸은 먹 판이다.
    /// 상태는 색을 곱하지 않고 상태마다 그린 그림으로 바꾼다 - 눌림은 먹이 스미고 비활성은 바랜다.
    /// 새 버튼을 만들면 아래 목록에 이름을 올리고 메뉴를 다시 돌린다
    /// </summary>
    public static class InkButtonStyler
    {
        /****************************************
        *                Types
        ****************************************/

        private sealed class InkKind
        {
            public string   Prefix;
            public string   PressedState;
            public bool     IsSliced;
            public Color    LabelColor;
            public Vector4  LabelMargin;
            public string[] Targets;
        }

        /****************************************
        *                Fields
        ****************************************/

        private const string MENU_PATH   = "그믐/UI/먹 버튼 스타일 적용";
        private const string PREFAB_PATH = "Assets/02.Prefabs/UI/RunManagementUI.prefab";
        private const string SPRITE_DIR  = "Assets/13.UI/00.UI_Resource/InGame/Ink/";

        // 판 그림은 높이 94px, 테두리 14px이다. 44px 버튼에서 위아래 선이 글자를 가로지르지 않게 테두리를 이 비율로 줄인다
        private const float SLICE_PIXELS_PER_UNIT = 2.2f;

        private const int SLOT_COUNT = 12;

        private static readonly string[] G_PotionSlots =
        {
            "ManagementPanel/Window/InventoryPage/PotionSlots/HealthPotion",
            "ManagementPanel/Window/InventoryPage/PotionSlots/SpiritPotion",
        };

        // 한지 아이보리 - 먹 판과 주칠 판 위 글자
        private static readonly Color G_HanjiIvory = new Color(0.906f, 0.867f, 0.784f, 1f);

        // 글자가 테두리 선에 닿지 않게 좌우를 조금 비운다 (왼쪽, 위, 오른쪽, 아래 px)
        private static readonly Vector4 G_OrnamentMargin = new Vector4(4f, 0f, 4f, 0f);

        // 분류 필터에 걸린 칸의 강조선. 흰 선을 칸 밖으로 두르면 칸을 붙인 배낭에서 이웃 칸과 겹쳐
        // 굵은 흰 격자가 된다. 칸 안쪽에 얇은 옅은 금빛 선 하나만 두른다
        private static readonly Color G_FocusGold = new Color(0.722f, 0.584f, 0.353f, 0.85f);

        private const float HIGHLIGHT_INSET     = 6f; // 칸 가장자리에서 안쪽으로 (칸 단위 px)
        private const float HIGHLIGHT_THICKNESS = 3f;

        private static readonly InkKind G_Primary = new InkKind
        {
            Prefix = "Ink_Primary",
            PressedState = "Pressed",
            IsSliced = true,
            LabelColor = G_HanjiIvory,
            LabelMargin = G_OrnamentMargin,
            Targets = new[]
            {
                "ManagementPanel/Window/InventoryPage/Equip",
                "ManagementPanel/Window/InventoryPage/UsePotion",
                "ManagementPanel/Window/InventoryPage/UseTicket",
                "ManagementPanel/Window/TransferPage/AcceptTransfer",
                "ManagementPanel/QuickTransferConfirm",
            },
        };

        private static readonly InkKind G_Secondary = new InkKind
        {
            Prefix = "Ink_Secondary",
            PressedState = "Pressed",
            IsSliced = true,
            LabelColor = G_HanjiIvory,
            LabelMargin = G_OrnamentMargin,
            Targets = new[]
            {
                "ManagementPanel/Window/InventoryPage/Unequip",
                "ManagementPanel/Window/InventoryPage/TargetEquipped",
                "ManagementPanel/Window/InventoryPage/OpenTransfer",
                "ManagementPanel/Window/TransferPage/Recipient0",
                "ManagementPanel/Window/TransferPage/Recipient1",
                "ManagementPanel/Window/TransferPage/RejectTransfer",
                "ManagementPanel/Window/TransferPage/CancelTransfer",
                "ManagementPanel/TalentPage/TalentReset",
                "ManagementPanel/QuickTransferCancel",
            },
        };

        // 탭은 누르는 순간 선택 그림을 보인다. 두루마리가 펼쳐진 듯 읽힌다
        private static readonly InkKind G_Tab = new InkKind
        {
            Prefix = "Ink_Tab",
            PressedState = "Selected",
            IsSliced = true,
            LabelColor = G_HanjiIvory,
            Targets = new[]
            {
                "EscapeMenu/SlidePanel/InventoryButton",
                "EscapeMenu/SlidePanel/TalentButton",
                "EscapeMenu/SlidePanel/GuideButton",
                "EscapeMenu/SlidePanel/SettingsButton",
                "ManagementPanel/Window/InventoryPage/InventoryFilter0",
                "ManagementPanel/Window/InventoryPage/InventoryFilter1",
                "ManagementPanel/Window/InventoryPage/InventoryFilter2",
                "ManagementPanel/Window/InventoryPage/InventoryFilter3",
            },
        };

        // 칸은 원본이 정사각에 가까워 자르지 않고 통째로 쓴다
        private static readonly InkKind G_Slot = new InkKind
        {
            Prefix = "Ink_Slot",
            PressedState = "Selected",
            IsSliced = false,
            LabelColor = G_HanjiIvory,
            Targets = BuildSlotTargets(),
        };

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem(MENU_PATH, false, 40)]
        public static void Apply()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
            try
            {
                int styled = StyleKind(root.transform, G_Primary) + StyleKind(root.transform, G_Secondary) +
                    StyleKind(root.transform, G_Tab) + StyleKind(root.transform, G_Slot);
                PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
                Debug.Log($"[InkButtonStyler] 버튼 {styled}개에 먹 버튼 스타일을 입혔다");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static string[] BuildSlotTargets()
        {
            var targets = new string[SLOT_COUNT];
            for (int i = 0; i < SLOT_COUNT; ++i)
            {
                targets[i] = "ManagementPanel/Window/InventoryPage/InventorySlot" + i;
            }

            // 배낭 왼쪽의 물약 퀵슬롯도 같은 칸이다
            var withPotions = new string[SLOT_COUNT + G_PotionSlots.Length];
            targets.CopyTo(withPotions, 0);
            G_PotionSlots.CopyTo(withPotions, SLOT_COUNT);
            return withPotions;
        }

        private static int StyleKind(Transform root, InkKind kind)
        {
            Sprite normal = LoadSprite(kind.Prefix, "Normal");
            Sprite hover = LoadSprite(kind.Prefix, "Hover");
            Sprite pressed = LoadSprite(kind.Prefix, kind.PressedState);
            Sprite disabled = LoadSprite(kind.Prefix, "Disabled");
            if ((normal == null) || (hover == null) || (pressed == null) || (disabled == null))
            {
                return 0;
            }

            int styled = 0;
            for (int i = 0; i < kind.Targets.Length; ++i)
            {
                Transform target = root.Find(kind.Targets[i]);
                Button button = (target == null) ? null : target.GetComponent<Button>();
                if (button == null)
                {
                    // 조용히 넘기면 스타일이 빠진 버튼을 아무도 모른다
                    Debug.LogError($"[InkButtonStyler] 버튼을 찾지 못했다: {kind.Targets[i]}");
                    continue;
                }

                Style(button, kind, normal, hover, pressed, disabled);
                ++styled;
            }

            return styled;
        }

        /// <summary> 강조선 네 줄(T · B · L · R)을 칸 안쪽으로 들이고 옅은 금빛으로 바꾼다 </summary>
        private static void StyleFilterHighlight(GameObject border)
        {
            if (border == null)
            {
                return;
            }

            var frame = (RectTransform)border.transform;
            frame.offsetMin = new Vector2(HIGHLIGHT_INSET, HIGHLIGHT_INSET);
            frame.offsetMax = new Vector2(-HIGHLIGHT_INSET, -HIGHLIGHT_INSET);

            foreach (Transform line in frame)
            {
                var rect = (RectTransform)line;
                bool horizontal = (line.name == "T") || (line.name == "B");
                rect.sizeDelta = horizontal
                    ? new Vector2(rect.sizeDelta.x, HIGHLIGHT_THICKNESS)
                    : new Vector2(HIGHLIGHT_THICKNESS, rect.sizeDelta.y);

                Image image = line.GetComponent<Image>();
                if (image != null)
                {
                    image.color = G_FocusGold;
                    image.raycastTarget = false;
                }
            }

            EditorUtility.SetDirty(border);
        }

        private static Sprite LoadSprite(string prefix, string state)
        {
            string path = $"{SPRITE_DIR}{prefix}_{state}.png";
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                Debug.LogError($"[InkButtonStyler] 그림을 찾지 못했다: {path}");
            }

            return sprite;
        }

        /// <summary>
        /// 상태마다 그린 그림으로 바꾼다. 선택(키보드 포커스)은 평소 그림을 쓴다 -
        /// 누른 뒤 포커스가 남아 호버처럼 보이면 아직 가리키는 것처럼 읽힌다
        /// </summary>
        private static void Style(Button button, InkKind kind, Sprite normal, Sprite hover,
            Sprite pressed, Sprite disabled)
        {
            Image image = button.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = normal;
                image.type = kind.IsSliced ? Image.Type.Sliced : Image.Type.Simple;
                image.pixelsPerUnitMultiplier = kind.IsSliced ? SLICE_PIXELS_PER_UNIT : 1f;
                image.fillCenter = true;
                image.preserveAspect = false;
                image.color = Color.white;
                button.targetGraphic = image;
            }

            button.transition = Selectable.Transition.SpriteSwap;
            SpriteState states = button.spriteState;
            states.highlightedSprite = hover;
            states.pressedSprite = pressed;
            states.selectedSprite = normal;
            states.disabledSprite = disabled;
            button.spriteState = states;

            // 칸과 필터는 고른 상태를 판 그림으로 보인다. 누름 그림과 같은 그림을 쓴다
            FunctionalUiIndexedButton indexed = button.GetComponent<FunctionalUiIndexedButton>();
            if (indexed != null)
            {
                var serialized = new SerializedObject(indexed);
                serialized.FindProperty("_selectedSprite").objectReferenceValue = pressed;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                StyleFilterHighlight(serialized.FindProperty("_highlightBorder").objectReferenceValue as GameObject);
            }

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.color = kind.LabelColor;
                label.margin = kind.LabelMargin;

                // 판이 바래도 글자가 밝으면 누를 수 있는 버튼처럼 읽힌다
                InkButtonLabel dimmer = button.GetComponent<InkButtonLabel>();
                if (dimmer == null)
                {
                    dimmer = button.gameObject.AddComponent<InkButtonLabel>();
                }

                dimmer.Bind(button, label);
                EditorUtility.SetDirty(dimmer);
            }

            EditorUtility.SetDirty(button);
        }
    }
}
