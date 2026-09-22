// System
using System;

// Unity
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 프로젝트 내부
using Geumeum.Art;

namespace Geumeum.UI.Editor
{
    /// <summary>
    /// Steam 로비 화면을 SessionNetwork 프리팹 안에 짓는다
    ///
    /// 씬을 직접 고치지 않고 프리팹에 넣는다 - 씬 파일은 머지가 안 되므로
    /// 두 사람이 같은 씬을 건드리면 한쪽 작업이 통째로 사라진다
    /// 프리팹은 Boot가 이미 참조하고 있어 씬 목록도 그대로다
    ///
    /// 손으로 배치하지 않고 코드로 짓는 이유는 다시 만들 수 있어야 해서다
    /// 배치를 고치려면 이 파일을 고치고 지우기 - 만들기를 다시 실행한다
    /// </summary>
    public static class SteamLobbyUiBuilder
    {
        /****************************************
        *              Constants
        ****************************************/

        public const string ROOT_NAME = "SteamLobbyUI";

        private const string MENU_ROOT   = "그믐/UI/";
        private const string PREFAB_PATH = "Assets/02.Prefabs/Network/SessionNetwork.prefab";
        private const string FONT_PATH   =
            "Assets/13.UI/00.UI_Resource/Fonts/Hangeuljaemin4-Regular SDF.asset";

        // 개발용 오버레이(80)보다 위에 둔다. 로비에서는 이 화면이 가장 앞이어야 한다
        private const int SORTING_ORDER = 90;

        private static readonly Color G_Backdrop      = new Color(0.02f, 0.03f, 0.05f, 0.94f);
        private static readonly Color G_Surface       = new Color(0.075f, 0.095f, 0.13f, 0.98f);
        private static readonly Color G_SurfaceRaised = new Color(0.12f, 0.15f, 0.20f, 1f);
        private static readonly Color G_Primary       = new Color(0.86f, 0.59f, 0.18f, 1f);
        private static readonly Color G_Text          = new Color(0.96f, 0.95f, 0.91f, 1f);
        private static readonly Color G_MutedText     = new Color(0.70f, 0.75f, 0.80f, 1f);

        /****************************************
        *                Menu
        ****************************************/

        [MenuItem(MENU_ROOT + "Steam 로비 화면 만들기", false, 10)]
        private static void Build()
        {
            if (!TryLoad(out GameObject contents, out TMP_FontAsset font))
            {
                return;
            }

            try
            {
                if (contents.transform.Find(ROOT_NAME) != null)
                {
                    Report("이미 있다. 먼저 [Steam 로비 화면 지우기]를 실행할 것");
                    return;
                }

                Configure(contents, font);
                PrefabUtility.SaveAsPrefabAsset(contents, PREFAB_PATH);
                Debug.Log($"[SteamLobbyUiBuilder] {ROOT_NAME}을 {PREFAB_PATH}에 만들었다");
                Report("만들었다. Boot로 실행하면 로비 화면이 뜬다");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[SteamLobbyUiBuilder] 구성 실패: {exception}");
                Report("구성에 실패했다. 콘솔을 볼 것");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        [MenuItem(MENU_ROOT + "Steam 로비 화면 지우기", false, 11)]
        private static void Clear()
        {
            if (!TryLoad(out GameObject contents, out TMP_FontAsset _))
            {
                return;
            }

            try
            {
                Transform existing = contents.transform.Find(ROOT_NAME);
                if (existing == null)
                {
                    Report("지울 것이 없다");
                    return;
                }

                UnityEngine.Object.DestroyImmediate(existing.gameObject);
                PrefabUtility.SaveAsPrefabAsset(contents, PREFAB_PATH);
                Debug.Log($"[SteamLobbyUiBuilder] {ROOT_NAME}을 지웠다");
                Report("지웠다");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private static bool TryLoad(out GameObject contents, out TMP_FontAsset font)
        {
            contents = null;

            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_PATH);
            if (font == null)
            {
                Debug.LogError($"[SteamLobbyUiBuilder] 폰트를 찾지 못했다: {FONT_PATH}");
                Report("폰트를 찾지 못했다");
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH) == null)
            {
                Debug.LogError($"[SteamLobbyUiBuilder] 프리팹을 찾지 못했다: {PREFAB_PATH}");
                Report("SessionNetwork 프리팹을 찾지 못했다");
                return false;
            }

            contents = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
            return (contents != null);
        }

        /// <summary> 열린 prefab contents에 로비 화면 자식을 만든다. 저장은 호출자가 한다 </summary>
        public static void Configure(GameObject sessionNetworkContents, TMP_FontAsset font)
        {
            GameObject root = CreateObject(ROOT_NAME, sessionNetworkContents.transform);

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode      = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder    = SORTING_ORDER;

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight  = 0.5f;

            root.AddComponent<GraphicRaycaster>();

            // 로비에서 캐릭터를 감추는 일은 이 화면과 수명이 같아 같은 오브젝트에 둔다
            root.AddComponent<LobbyCharacterVisibility>();

            GameObject panel = BuildPanel(root.transform, font, out References references);

            SteamLobbyPanel controller = root.AddComponent<SteamLobbyPanel>();
            SetReference(controller, "_root",         panel);
            SetReference(controller, "_statusText",   references.Status);
            SetReference(controller, "_memberText",   references.Member);
            SetReference(controller, "_lobbyIdText",  references.LobbyId);
            SetReference(controller, "_hintText",     references.Hint);
            SetReference(controller, "_createButton", references.Create);
            SetReference(controller, "_copyIdButton", references.CopyId);
            SetReference(controller, "_leaveButton",  references.Leave);
            SetReference(controller, "_joinInput",    references.JoinInput);
            SetReference(controller, "_joinButton",   references.Join);
            SetReference(controller, "_pasteButton",  references.Paste);
            SetReference(controller, "_startButton",  references.Start);
        }

        private struct References
        {
            public TMP_Text       Status;
            public TMP_Text       Member;
            public TMP_Text       LobbyId;
            public TMP_Text       Hint;
            public Button         Create;
            public Button         CopyId;
            public Button         Leave;
            public TMP_InputField JoinInput;
            public Button         Join;
            public Button         Paste;
            public Button         Start;
        }

        private static GameObject BuildPanel(Transform parent, TMP_FontAsset font,
            out References references)
        {
            GameObject backdrop = CreateStretch("Backdrop", parent);
            Image backdropImage = backdrop.AddComponent<Image>();
            backdropImage.color = G_Backdrop;

            GameObject card = CreatePanel("Card", backdrop.transform,
                new Vector2(520f, 440f), Vector2.zero, G_Surface);

            CreateText("Title", card.transform, "그믐 - Steam 로비", font, 26f,
                TextAlignmentOptions.Center, new Vector2(480f, 40f),
                new Vector2(0f, 180f), G_Text);

            references = default;

            references.Status = CreateText("Status", card.transform, "단계: -", font, 16f,
                TextAlignmentOptions.Center, new Vector2(480f, 26f),
                new Vector2(0f, 142f), G_MutedText);

            references.Member = CreateText("Member", card.transform, "인원: -", font, 22f,
                TextAlignmentOptions.Center, new Vector2(480f, 34f),
                new Vector2(0f, 108f), G_Text);

            references.LobbyId = CreateText("LobbyId", card.transform, "로비: 없음", font, 15f,
                TextAlignmentOptions.Center, new Vector2(480f, 26f),
                new Vector2(0f, 76f), G_MutedText);

            references.Create = CreateButton("CreateButton", card.transform, "방 만들기",
                font, new Vector2(220f, 46f), new Vector2(-125f, 28f), G_Primary, out _);

            references.CopyId = CreateButton("CopyIdButton", card.transform, "로비 ID 복사",
                font, new Vector2(220f, 46f), new Vector2(125f, 28f), G_SurfaceRaised, out _);

            references.JoinInput = CreateInputField("JoinInput", card.transform,
                "로비 ID를 붙여넣기", font, new Vector2(340f, 44f),
                new Vector2(-65f, -28f), G_SurfaceRaised);

            references.Paste = CreateButton("PasteButton", card.transform, "붙여넣기",
                font, new Vector2(100f, 44f), new Vector2(180f, -28f), G_SurfaceRaised, out _);

            references.Join = CreateButton("JoinButton", card.transform, "참가",
                font, new Vector2(460f, 46f), new Vector2(0f, -82f), G_SurfaceRaised, out _);

            references.Start = CreateButton("StartButton", card.transform, "시작",
                font, new Vector2(340f, 54f), new Vector2(-65f, -142f), G_Primary, out _);

            references.Leave = CreateButton("LeaveButton", card.transform, "나가기",
                font, new Vector2(110f, 54f), new Vector2(175f, -142f), G_SurfaceRaised, out _);

            references.Hint = CreateText("Hint", card.transform,
                "방을 만들거나, 받은 로비 ID로 참가할 것", font, 14f,
                TextAlignmentOptions.Center, new Vector2(480f, 40f),
                new Vector2(0f, -192f), G_MutedText);

            return backdrop;
        }

        /****************************************
        *                Helper
        ****************************************/

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
            rect.anchorMin        = new Vector2(0.5f, 0.5f);
            rect.anchorMax        = new Vector2(0.5f, 0.5f);
            rect.sizeDelta        = size;
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
            colors.normalColor      = Color.white;
            colors.highlightedColor = new Color(1f, 0.96f, 0.84f, 1f);
            colors.pressedColor     = new Color(0.76f, 0.78f, 0.82f, 1f);
            colors.disabledColor    = new Color(0.35f, 0.37f, 0.40f, 0.65f);
            button.colors = colors;

            text = CreateText("Label", gameObject.transform, label, font, 18f,
                TextAlignmentOptions.Center, size, Vector2.zero, G_Text);
            return button;
        }

        private static TMP_InputField CreateInputField(string name, Transform parent,
            string placeholder, TMP_FontAsset font, Vector2 size, Vector2 position, Color color)
        {
            GameObject gameObject = CreatePanel(name, parent, size, position, color);
            TMP_InputField input = gameObject.AddComponent<TMP_InputField>();

            // 입력 글자가 칸 밖으로 나가지 않게 잘라 주는 영역
            GameObject viewport = CreateStretch("TextArea", gameObject.transform);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.offsetMin = new Vector2(10f, 6f);
            viewportRect.offsetMax = new Vector2(-10f, -6f);
            viewport.AddComponent<RectMask2D>();

            TMP_Text hint = CreateText("Placeholder", viewport.transform, placeholder,
                font, 16f, TextAlignmentOptions.Left, size, Vector2.zero, G_MutedText);
            StretchInside(hint.rectTransform);

            TMP_Text value = CreateText("Text", viewport.transform, string.Empty,
                font, 16f, TextAlignmentOptions.Left, size, Vector2.zero, G_Text);
            StretchInside(value.rectTransform);

            input.textViewport  = viewportRect;
            input.textComponent = value;
            input.placeholder   = hint;

            // 숫자 전용으로 잠그면 붙여넣기가 글자 단위 검증에 걸려 통째로 무시된다
            // 자리 수만 제한하고, 숫자만 남기는 일은 붙여넣기 쪽에서 한다
            input.contentType    = TMP_InputField.ContentType.Standard;
            input.lineType       = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 20;
            return input;
        }

        private static void StretchInside(RectTransform rect)
        {
            rect.anchorMin        = Vector2.zero;
            rect.anchorMax        = Vector2.one;
            rect.offsetMin        = Vector2.zero;
            rect.offsetMax        = Vector2.zero;
            rect.sizeDelta        = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
        }

        private static TMP_Text CreateText(string name, Transform parent, string value,
            TMP_FontAsset font, float fontSize, TextAlignmentOptions alignment,
            Vector2 size, Vector2 position, Color color)
        {
            GameObject gameObject = CreateObject(name, parent);
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.anchorMin        = new Vector2(0.5f, 0.5f);
            rect.anchorMax        = new Vector2(0.5f, 0.5f);
            rect.sizeDelta        = size;
            rect.anchoredPosition = position;

            TextMeshProUGUI text = gameObject.AddComponent<TextMeshProUGUI>();
            text.font             = font;
            text.fontSize         = fontSize;
            text.color            = color;
            text.alignment        = alignment;
            text.text             = value;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget    = false;
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

        /// <summary>
        /// 결과는 콘솔로만 알린다
        ///
        /// 모달 창을 띄우면 에디터가 클릭을 기다리며 멈춰, 이 메뉴를 자동으로 부르는
        /// 도구가 전부 응답 없이 끊긴다. 성공과 실패는 로그 종류로 구분한다
        /// </summary>
        private static void Report(string message)
        {
            Debug.Log($"[SteamLobbyUiBuilder] {message}");
        }
    }
}
