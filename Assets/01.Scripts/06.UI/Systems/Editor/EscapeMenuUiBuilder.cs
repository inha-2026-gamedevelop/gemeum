// System
using System;

// Unity
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Geumeum.UI.Editor
{
    /// <summary>
    /// RunManagementUI 프리팹의 좌측 레일을 걷어내고 ESC 메뉴와 설정 골격을 심는다.
    /// 레일은 지우지 않고 _bak_ 이름으로 바꿔 꺼 둔다 — 되돌릴 때 스킨 좌표를 다시 맞추지 않아도 된다
    /// </summary>
    public static class EscapeMenuUiBuilder
    {
        private const string MENU_ROOT   = "그믐/UI/";
        private const string PREFAB_PATH = "Assets/02.Prefabs/UI/RunManagementUI.prefab";
        private const string FONT_PATH   =
            "Assets/13.UI/00.UI_Resource/Fonts/Hangeuljaemin4-Regular SDF.asset";

        private const string MENU_NAME     = "EscapeMenu";
        private const string SETTINGS_NAME = "SettingsWindow";
        private const string BACKUP_PREFIX = "_bak_";

        private static readonly string[] G_RailObjects =
        {
            "ManagementPanel/Window/LeftRailBg",
            "ManagementPanel/Window/LeftRailFade",
            "ManagementPanel/Window/InventoryTab",
            "ManagementPanel/Window/TalentTab",
            "ManagementPanel/Window/TransferTab",
        };

        private static readonly Color G_Backdrop      = new Color(0.025f, 0.035f, 0.055f, 0.55f);
        private static readonly Color G_MenuBackdrop  = new Color(0.025f, 0.035f, 0.055f, 0.25f);
        private static readonly Color G_Surface       = new Color(0.075f, 0.095f, 0.13f, 0.97f);
        private static readonly Color G_SurfaceRaised = new Color(0.12f, 0.15f, 0.20f, 1f);
        private static readonly Color G_Primary       = new Color(0.86f, 0.59f, 0.18f, 1f);
        private static readonly Color G_Text          = new Color(0.96f, 0.95f, 0.91f, 1f);
        private static readonly Color G_MutedText     = new Color(0.70f, 0.75f, 0.80f, 1f);

        // 캔버스 기준 해상도 1280x720과 같은 좌표계를 쓴다
        private const float BAR_WIDTH     = 540f;
        private const float BAR_HEIGHT    = 68f;
        private const float BAR_TOP_GAP   = 16f;
        private const float BUTTON_WIDTH  = 150f;
        private const float BUTTON_HEIGHT = 44f;
        private const float BUTTON_GAP    = 170f;

        [MenuItem(MENU_ROOT + "ESC 메뉴로 바꾸기", false, 20)]
        public static void Build()
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_PATH);
            if (font == null)
            {
                Debug.LogError($"[EscapeMenuUiBuilder] font를 찾지 못했다: {FONT_PATH}");
                return;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
            if (contents == null)
            {
                Debug.LogError($"[EscapeMenuUiBuilder] 프리팹을 열지 못했다: {PREFAB_PATH}");
                return;
            }

            try
            {
                // 배치를 고쳐 다시 돌릴 일이 잦다. 이전에 심은 것만 걷어내고 새로 세운다
                RemoveExisting(contents.transform, MENU_NAME);
                RemoveExisting(contents.transform, SETTINGS_NAME);

                BackupRail(contents.transform);
                SettingsPanel settings = BuildSettings(contents.transform, font);
                BuildMenu(contents.transform, font, settings);

                PrefabUtility.SaveAsPrefabAsset(contents, PREFAB_PATH);
                Debug.Log("[EscapeMenuUiBuilder] 완료: 레일 백업 · ESC 메뉴 · 설정 골격");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[EscapeMenuUiBuilder] 구성 실패: {exception}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static string ToBackupPath(string path)
        {
            int lastSlash = path.LastIndexOf('/');
            return (lastSlash < 0)
                ? BACKUP_PREFIX + path
                : path.Substring(0, lastSlash + 1) + BACKUP_PREFIX +
                    path.Substring(lastSlash + 1);
        }

        private static void RemoveExisting(Transform root, string name)
        {
            Transform existing = root.Find(name);
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }
        }

        /// <summary> 레일 오브젝트를 지우지 않고 이름만 바꿔 꺼 둔다 </summary>
        private static void BackupRail(Transform root)
        {
            for (int i = 0; i < G_RailObjects.Length; ++i)
            {
                // 다시 돌릴 때는 이미 _bak_가 붙어 있다. 그 이름으로도 찾아본다
                Transform rail = root.Find(G_RailObjects[i])
                    ?? root.Find(ToBackupPath(G_RailObjects[i]));
                if (rail == null)
                {
                    Debug.LogWarning(
                        $"[EscapeMenuUiBuilder] 레일을 찾지 못했다: {G_RailObjects[i]}");
                    continue;
                }

                if (!rail.name.StartsWith(BACKUP_PREFIX, StringComparison.Ordinal))
                {
                    rail.name = BACKUP_PREFIX + rail.name;
                }

                rail.gameObject.SetActive(false);
            }
        }

        private static void BuildMenu(Transform parent, TMP_FontAsset font,
            SettingsPanel settings)
        {
            GameObject menu = CreateStretch(MENU_NAME, parent);
            CanvasGroup canvasGroup = menu.AddComponent<CanvasGroup>();

            // 바가 얇아 화면을 세게 어둡게 하면 메뉴보다 딤이 더 눈에 띈다. 클릭만 막을 만큼만 깐다
            GameObject backdrop = CreateStretch("Backdrop", menu.transform);
            Image backdropImage = backdrop.AddComponent<Image>();
            backdropImage.color = G_MenuBackdrop;

            RectTransform slide = CreateTopBar("SlidePanel", menu.transform);

            // 세 칸을 가로로 나란히 둔다. 가운데가 특성, 좌우가 배낭과 설정
            Button inventory = CreateButton("InventoryButton", slide, "배낭", font,
                new Vector2(-BUTTON_GAP, 0f), G_SurfaceRaised);
            Button talent = CreateButton("TalentButton", slide, "특성", font,
                Vector2.zero, G_SurfaceRaised);
            Button settingsButton = CreateButton("SettingsButton", slide, "설정", font,
                new Vector2(BUTTON_GAP, 0f), G_SurfaceRaised);

            EscapeMenuPanel panel = menu.AddComponent<EscapeMenuPanel>();
            SetReference(panel, "_canvasGroup", canvasGroup);
            SetReference(panel, "_slidePanel", slide);
            SetReference(panel, "_inventoryButton", inventory);
            SetReference(panel, "_talentButton", talent);
            SetReference(panel, "_settingsButton", settingsButton);
            SetReference(panel, "_settingsPanel", settings);
        }

        private static SettingsPanel BuildSettings(Transform parent, TMP_FontAsset font)
        {
            GameObject window = CreateStretch(SETTINGS_NAME, parent);
            CanvasGroup canvasGroup = window.AddComponent<CanvasGroup>();
            Image backdrop = window.AddComponent<Image>();
            backdrop.color = G_Backdrop;

            GameObject box = CreateObject("Window", window.transform);
            RectTransform boxRect = box.GetComponent<RectTransform>();
            boxRect.anchorMin        = new Vector2(0.5f, 0.5f);
            boxRect.anchorMax        = new Vector2(0.5f, 0.5f);
            boxRect.pivot            = new Vector2(0.5f, 0.5f);
            boxRect.sizeDelta        = new Vector2(560f, 360f);
            boxRect.anchoredPosition = Vector2.zero;
            Image boxImage = box.AddComponent<Image>();
            boxImage.color = G_Surface;

            CreateText("Title", boxRect, "설정", font, 30f, TextAlignmentOptions.Center,
                new Vector2(520f, 46f), new Vector2(0f, 128f), G_Primary);
            CreateText("Placeholder", boxRect, "준비 중입니다.", font, 18f,
                TextAlignmentOptions.Center, new Vector2(520f, 40f), Vector2.zero,
                G_MutedText);

            Button close = CreateButton("CloseButton", boxRect, "닫기", font,
                new Vector2(0f, -128f), G_SurfaceRaised);

            SettingsPanel panel = window.AddComponent<SettingsPanel>();
            SetReference(panel, "_canvasGroup", canvasGroup);
            SetReference(panel, "_closeButton", close);
            return panel;
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static GameObject CreateObject(string name, Transform parent)
        {
            GameObject created = new GameObject(name, typeof(RectTransform));
            created.transform.SetParent(parent, false);
            return created;
        }

        private static GameObject CreateStretch(string name, Transform parent)
        {
            GameObject created = CreateObject(name, parent);
            RectTransform rect = created.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect.gameObject;
        }

        /// <summary> 화면 중앙 상단에 얇게 붙는 가로 바. 슬라이드의 기준 위치가 된다 </summary>
        private static RectTransform CreateTopBar(string name, Transform parent)
        {
            GameObject created = CreateObject(name, parent);
            RectTransform rect = created.GetComponent<RectTransform>();
            rect.anchorMin        = new Vector2(0.5f, 1f);
            rect.anchorMax        = new Vector2(0.5f, 1f);
            rect.pivot            = new Vector2(0.5f, 1f);
            rect.sizeDelta        = new Vector2(BAR_WIDTH, BAR_HEIGHT);
            rect.anchoredPosition = new Vector2(0f, -BAR_TOP_GAP);

            Image image = created.AddComponent<Image>();
            image.color = G_Surface;
            return rect;
        }

        private static TMP_Text CreateText(string name, Transform parent, string content,
            TMP_FontAsset font, float size, TextAlignmentOptions alignment,
            Vector2 sizeDelta, Vector2 position, Color color)
        {
            GameObject created = CreateObject(name, parent);
            RectTransform rect = created.GetComponent<RectTransform>();
            rect.anchorMin        = new Vector2(0.5f, 0.5f);
            rect.anchorMax        = new Vector2(0.5f, 0.5f);
            rect.pivot            = new Vector2(0.5f, 0.5f);
            rect.sizeDelta        = sizeDelta;
            rect.anchoredPosition = position;

            TextMeshProUGUI text = created.AddComponent<TextMeshProUGUI>();
            text.text          = content;
            text.font          = font;
            text.fontSize      = size;
            text.alignment     = alignment;
            text.color         = color;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateButton(string name, Transform parent, string label,
            TMP_FontAsset font, Vector2 position, Color color)
        {
            GameObject created = CreateObject(name, parent);
            RectTransform rect = created.GetComponent<RectTransform>();
            rect.anchorMin        = new Vector2(0.5f, 0.5f);
            rect.anchorMax        = new Vector2(0.5f, 0.5f);
            rect.pivot            = new Vector2(0.5f, 0.5f);
            rect.sizeDelta        = new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT);
            rect.anchoredPosition = position;

            Image image = created.AddComponent<Image>();
            image.color = color;

            Button button = created.AddComponent<Button>();
            button.targetGraphic = image;

            CreateText("Label", rect, label, font, 22f, TextAlignmentOptions.Center,
                new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT), Vector2.zero, G_Text);
            return button;
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
    }
}
