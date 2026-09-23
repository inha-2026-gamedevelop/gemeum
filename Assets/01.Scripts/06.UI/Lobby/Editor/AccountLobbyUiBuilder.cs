using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Geumeum.UI.Editor
{
    /// <summary>계정 로비 화면 프리팹을 재생성하고 기존 SessionNetwork에 배선한다.</summary>
    public static class AccountLobbyUiBuilder
    {
        public const string ROOT_NAME = "AccountLobbyUI";
        public const string EVENT_SYSTEM_NAME = "FrontendEventSystem";
        public const string UI_PREFAB_PATH = "Assets/02.Prefabs/UI/AccountLobbyUI.prefab";
        public const string SESSION_PREFAB_PATH = "Assets/02.Prefabs/Network/SessionNetwork.prefab";
        public const string FONT_PATH =
            "Assets/13.UI/00.UI_Resource/Fonts/Hangeuljaemin4-Regular SDF.asset";

        private const int SORTING_ORDER = 150;

        private static readonly Color G_Backdrop = new Color(0.025f, 0.037f, 0.055f, 1f);
        private static readonly Color G_Panel    = new Color(0.065f, 0.088f, 0.12f, 0.98f);
        private static readonly Color G_Raised   = new Color(0.105f, 0.14f, 0.18f, 1f);
        private static readonly Color G_Gold     = new Color(0.85f, 0.73f, 0.48f, 1f);
        private static readonly Color G_Text     = new Color(0.94f, 0.92f, 0.86f, 1f);
        private static readonly Color G_Muted    = new Color(0.65f, 0.70f, 0.77f, 1f);
        private static readonly Color G_Line     = new Color(0.22f, 0.27f, 0.33f, 1f);

        private sealed class References
        {
            public Canvas Canvas;
            public CanvasGroup CanvasGroup;
            public GameObject FrontendInput;
            public GameObject Loading;
            public GameObject Rooms;
            public GameObject Party;
            public TMP_Text Status;
            public TMP_Text Identity;
            public TMP_Text RoomSummary;
            public TMP_Text PartyTitle;
            public TMP_Text PartyMembers;
            public TMP_Text PartyHint;
            public TMP_InputField RoomNameInput;
            public RectTransform RoomListContent;
            public Button RoomRowTemplate;
            public Button RoomsRefresh;
            public Button RoomsCreate;
            public Button RoomsBack;
            public Button RoomsParty;
            public Button PartyBrowse;
            public Button PartyLeave;
            public Button PartyReady;
            public Button PartyBots;
            public Button PartyStart;
            public Button PartyRetry;
            public Button[] Preferences;
        }

        [MenuItem("그믐/UI/계정 로비 화면 만들기", false, 12)]
        public static void CreateOrUpdate()
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_PATH);
            if (font == null)
            {
                throw new InvalidOperationException("계정 로비 한글 글꼴을 찾지 못했다: " + FONT_PATH);
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(SESSION_PREFAB_PATH) == null)
            {
                throw new InvalidOperationException("SessionNetwork 프리팹을 찾지 못했다");
            }

            GameObject source = CreateVisualTree(null, font);
            try
            {
                PrefabUtility.SaveAsPrefabAsset(source, UI_PREFAB_PATH);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(SESSION_PREFAB_PATH);
            try
            {
                Transform previous = contents.transform.Find(ROOT_NAME);
                if (previous != null)
                {
                    UnityEngine.Object.DestroyImmediate(previous.gameObject);
                }
                GameObject uiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(UI_PREFAB_PATH);
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(uiPrefab, contents.transform);
                if (instance == null)
                {
                    throw new InvalidOperationException("계정 로비 프리팹 배선 실패");
                }
                instance.name = ROOT_NAME;
                PrefabUtility.SaveAsPrefabAsset(contents, SESSION_PREFAB_PATH);
                Debug.Log("[AccountLobbyUiBuilder] SessionNetwork에 계정 로비 UI를 연결했다");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>테스트와 빌더가 같은 시각 트리를 사용한다. 저장은 호출자가 결정한다.</summary>
        public static GameObject CreateVisualTree(Transform parent, TMP_FontAsset font)
        {
            if (font == null)
            {
                throw new ArgumentNullException(nameof(font));
            }
            GameObject root = NewRect(ROOT_NAME, parent).gameObject;
            try
            {
                Canvas canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.overrideSorting = true;
                canvas.sortingOrder = SORTING_ORDER;
                CanvasScaler scaler = root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                root.AddComponent<GraphicRaycaster>();
                CanvasGroup group = root.AddComponent<CanvasGroup>();

                References refs = new References { Canvas = canvas, CanvasGroup = group };
                refs.FrontendInput = BuildEventSystem(root.transform);
                RectTransform background = Stretch("Backdrop", root.transform);
                AddImage(background, G_Backdrop);
                RectTransform top = Surface("TopBar", background, new Vector2(1790f, 100f),
                    new Vector2(0f, 440f), G_Panel);
                Label("Brand", top, "그믐  |  원정대", font, 36f,
                    new Vector2(-590f, 0f), new Vector2(550f, 64f), G_Gold,
                    TextAlignmentOptions.MidlineLeft);
                refs.Identity = Label("Identity", top, "원정대", font, 22f,
                    new Vector2(575f, 0f), new Vector2(500f, 50f), G_Text,
                    TextAlignmentOptions.MidlineRight);
                RectTransform rule = Surface("HeaderRule", background,
                    new Vector2(1790f, 2f), new Vector2(0f, 381f), G_Gold);
                rule.GetComponent<Image>().raycastTarget = false;
                refs.Status = Label("Status", background, string.Empty, font, 22f,
                    new Vector2(0f, -471f), new Vector2(1700f, 46f), G_Muted,
                    TextAlignmentOptions.Center);

                refs.Loading = BuildLoading(background, font);
                refs.Rooms = BuildRooms(background, font, refs);
                refs.Party = BuildParty(background, font, refs);

                AccountLobbyPanel controller = root.AddComponent<AccountLobbyPanel>();
                Bind(controller, refs);
                return root;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(root);
                throw;
            }
        }

        private static GameObject BuildLoading(Transform parent, TMP_FontAsset font)
        {
            RectTransform page = Page("LoadingPage", parent);
            RectTransform card = Surface("LoadingCard", page,
                new Vector2(940f, 330f), Vector2.zero, G_Panel);
            Label("LoadingTitle", card, "원정대를 준비하고 있습니다", font, 42f,
                new Vector2(0f, 70f), new Vector2(840f, 70f), G_Gold);
            Label("LoadingBody", card, "잠시만 기다려 주세요.", font, 25f,
                new Vector2(0f, -45f), new Vector2(800f, 80f), G_Text);
            return page.gameObject;
        }

        /// <summary>
        /// 오프라인 계정 화면 전용 입력. SessionManagers의 RunEventSystem은 Mirror가 스폰하기 전까지
        /// 비활성이므로 따로 두고, 다른 EventSystem이 켜져 있으면 AccountLobbyPanel이 끈다.
        /// </summary>
        private static GameObject BuildEventSystem(Transform parent)
        {
            GameObject host = new GameObject(EVENT_SYSTEM_NAME);
            host.transform.SetParent(parent, false);
            host.AddComponent<EventSystem>();
            // 이 프로젝트는 레거시 Input(activeInputHandler=0)을 쓴다
            host.AddComponent<StandaloneInputModule>();
            host.SetActive(false);
            return host;
        }

        private static GameObject BuildRooms(Transform parent, TMP_FontAsset font, References refs)
        {
            RectTransform page = Page("RoomsPage", parent);
            RectTransform card = Surface("RoomsCard", page,
                new Vector2(1660f, 760f), Vector2.zero, G_Panel);
            Label("RoomsTitle", card, "원정대 찾기", font, 40f,
                new Vector2(-540f, 317f), new Vector2(500f, 60f), G_Text,
                TextAlignmentOptions.MidlineLeft);
            refs.RoomSummary = Label("RoomSummary", card, "목록을 불러오세요.", font, 23f,
                new Vector2(-450f, 259f), new Vector2(680f, 42f), G_Muted,
                TextAlignmentOptions.MidlineLeft);
            refs.RoomsRefresh = MakeButton("RefreshButton", card, "새로고침", font,
                new Vector2(188f, 54f), new Vector2(182f, 313f), G_Raised);
            refs.RoomsBack = MakeButton("RoomsBackButton", card, "처음으로", font,
                new Vector2(150f, 54f), new Vector2(709f, 313f), G_Raised);

            RectTransform viewport = Surface("RoomListViewport", card,
                new Vector2(1000f, 545f), new Vector2(-285f, -56f), G_Backdrop);
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = NewRect("RoomListContent", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 9f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            refs.RoomListContent = content;
            refs.RoomRowTemplate = MakeButton("RoomRowTemplate", content,
                string.Empty, font, new Vector2(940f, 92f), Vector2.zero, G_Raised);
            LayoutElement rowLayout = refs.RoomRowTemplate.gameObject.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = 92f;
            TMP_Text oldLabel = refs.RoomRowTemplate.GetComponentInChildren<TMP_Text>();
            UnityEngine.Object.DestroyImmediate(oldLabel.gameObject);
            Label("RoomName", refs.RoomRowTemplate.transform, "원정대", font, 25f,
                new Vector2(-210f, 16f), new Vector2(520f, 38f), G_Text,
                TextAlignmentOptions.MidlineLeft);
            Label("RoomDetails", refs.RoomRowTemplate.transform, "0/3명", font, 19f,
                new Vector2(-210f, -23f), new Vector2(520f, 34f), G_Muted,
                TextAlignmentOptions.MidlineLeft);
            Label("RoomJoin", refs.RoomRowTemplate.transform, "참가", font, 22f,
                new Vector2(357f, 0f), new Vector2(160f, 48f), G_Gold);

            RectTransform create = Surface("CreateCard", card,
                new Vector2(510f, 545f), new Vector2(545f, -56f), G_Raised);
            Label("CreateTitle", create, "새 원정대", font, 30f,
                new Vector2(0f, 196f), new Vector2(450f, 56f), G_Gold);
            Label("CreateHint", create, "이름을 정하고 파티원을 기다리세요.", font, 21f,
                new Vector2(0f, 131f), new Vector2(450f, 68f), G_Muted);
            refs.RoomNameInput = MakeInput("RoomNameInput", create, "원정대 이름", font,
                new Vector2(430f, 64f), new Vector2(0f, 38f));
            refs.RoomsCreate = MakeButton("CreateRoomButton", create, "원정대 만들기", font,
                new Vector2(430f, 68f), new Vector2(0f, -69f), G_Gold, true);
            refs.RoomsParty = MakeButton("CurrentPartyButton", create, "현재 원정대 보기", font,
                new Vector2(430f, 64f), new Vector2(0f, -167f), G_Raised);
            return page.gameObject;
        }

        private static GameObject BuildParty(Transform parent, TMP_FontAsset font, References refs)
        {
            RectTransform page = Page("PartyPage", parent);
            RectTransform card = Surface("PartyCard", page,
                new Vector2(1660f, 760f), Vector2.zero, G_Panel);
            refs.PartyTitle = Label("PartyTitle", card, "원정대", font, 39f,
                new Vector2(-510f, 319f), new Vector2(620f, 58f), G_Gold,
                TextAlignmentOptions.MidlineLeft);
            refs.PartyBrowse = MakeButton("BrowseButton", card, "다른 원정대", font,
                new Vector2(205f, 52f), new Vector2(480f, 313f), G_Raised);
            refs.PartyLeave = MakeButton("LeaveButton", card, "나가기", font,
                new Vector2(150f, 52f), new Vector2(711f, 313f), G_Raised);

            RectTransform roster = Surface("RosterCard", card,
                new Vector2(1010f, 530f), new Vector2(-290f, -10f), G_Backdrop);
            Label("RosterHeading", roster, "파티원", font, 26f,
                new Vector2(-380f, 195f), new Vector2(260f, 48f), G_Gold,
                TextAlignmentOptions.MidlineLeft);
            refs.PartyMembers = Label("PartyMembers", roster, string.Empty, font, 27f,
                new Vector2(0f, 14f), new Vector2(930f, 305f), G_Text,
                TextAlignmentOptions.TopLeft);
            Surface("RosterRule", roster, new Vector2(930f, 2f), new Vector2(0f, -159f), G_Line);
            refs.PartyHint = Label("PartyHint", roster, string.Empty, font, 20f,
                new Vector2(0f, -202f), new Vector2(920f, 70f), G_Muted);

            RectTransform controls = Surface("PartyControls", card,
                new Vector2(520f, 530f), new Vector2(535f, -10f), G_Raised);
            Label("WeaponHeading", controls, "내 선호 무기", font, 25f,
                new Vector2(0f, 217f), new Vector2(460f, 42f), G_Gold);
            string[] names = { "검, 차사", "활, 신궁", "방울, 무녀" };
            refs.Preferences = new Button[3];
            for (int i = 0; i < names.Length; ++i)
            {
                refs.Preferences[i] = MakeButton("Preference" + i, controls, names[i], font,
                    new Vector2(440f, 52f), new Vector2(0f, 152f - i * 66f), G_Panel);
            }
            refs.PartyBots = MakeButton("BotsButton", controls, "빈자리 봇 추가", font,
                new Vector2(440f, 52f), new Vector2(0f, -72f), G_Panel);
            refs.PartyReady = MakeButton("ReadyButton", controls, "준비 완료", font,
                new Vector2(440f, 54f), new Vector2(0f, -137f), G_Gold, true);
            refs.PartyStart = MakeButton("StartButton", controls, "원정 출발", font,
                new Vector2(440f, 54f), new Vector2(0f, -202f), G_Gold, true);
            refs.PartyRetry = MakeButton("RetryButton", card, "입장 다시 시도", font,
                new Vector2(420f, 56f), new Vector2(530f, -319f), G_Raised);
            return page.gameObject;
        }

        private static RectTransform Page(string name, Transform parent)
        {
            RectTransform page = NewRect(name, parent);
            page.anchorMin = Vector2.zero;
            page.anchorMax = Vector2.one;
            page.offsetMin = Vector2.zero;
            page.offsetMax = Vector2.zero;
            return page;
        }

        private static RectTransform Stretch(string name, Transform parent)
        {
            return Page(name, parent);
        }

        private static RectTransform Surface(string name, Transform parent, Vector2 size,
            Vector2 position, Color color)
        {
            RectTransform rect = NewRect(name, parent);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            AddImage(rect, color);
            return rect;
        }

        private static void AddImage(RectTransform rect, Color color)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            if (parent != null)
            {
                rect.SetParent(parent, false);
            }
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            return rect;
        }

        private static TMP_Text Label(string name, Transform parent, string value,
            TMP_FontAsset font, float fontSize, Vector2 position, Vector2 size,
            Color color, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            RectTransform rect = NewRect(name, parent);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = value;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.richText = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            return label;
        }

        private static Button MakeButton(string name, Transform parent, string value,
            TMP_FontAsset font, Vector2 size, Vector2 position, Color color,
            bool darkText = false)
        {
            RectTransform rect = Surface(name, parent, size, position, color);
            Button button = rect.gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            colors.selectedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.75f);
            button.colors = colors;
            Label("Label", rect, value, font, 23f, Vector2.zero, size,
                darkText ? G_Backdrop : G_Text);
            return button;
        }

        private static TMP_InputField MakeInput(string name, Transform parent,
            string placeholder, TMP_FontAsset font, Vector2 size, Vector2 position)
        {
            RectTransform rect = Surface(name, parent, size, position, G_Panel);
            TMP_InputField input = rect.gameObject.AddComponent<TMP_InputField>();
            RectTransform viewport = Stretch("TextArea", rect);
            viewport.offsetMin = new Vector2(16f, 8f);
            viewport.offsetMax = new Vector2(-16f, -8f);
            viewport.gameObject.AddComponent<RectMask2D>();
            TMP_Text hint = Label("Placeholder", viewport, placeholder, font, 22f,
                Vector2.zero, size, G_Muted, TextAlignmentOptions.MidlineLeft);
            TMP_Text value = Label("Text", viewport, string.Empty, font, 22f,
                Vector2.zero, size, G_Text, TextAlignmentOptions.MidlineLeft);
            StretchText(hint.rectTransform);
            StretchText(value.rectTransform);
            input.textViewport = viewport;
            input.textComponent = value;
            input.placeholder = hint;
            input.lineType = TMP_InputField.LineType.SingleLine;
            return input;
        }

        private static void StretchText(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Bind(AccountLobbyPanel panel, References refs)
        {
            SerializedObject serialized = new SerializedObject(panel);
            Put(serialized, "_canvas", refs.Canvas);
            Put(serialized, "_canvasGroup", refs.CanvasGroup);
            Put(serialized, "_frontendEventSystem", refs.FrontendInput);
            Put(serialized, "_loadingPage", refs.Loading);
            Put(serialized, "_roomsPage", refs.Rooms);
            Put(serialized, "_partyPage", refs.Party);
            Put(serialized, "_statusText", refs.Status);
            Put(serialized, "_identityText", refs.Identity);
            Put(serialized, "_roomSummaryText", refs.RoomSummary);
            Put(serialized, "_partyTitleText", refs.PartyTitle);
            Put(serialized, "_partyMembersText", refs.PartyMembers);
            Put(serialized, "_partyHintText", refs.PartyHint);
            Put(serialized, "_roomNameInput", refs.RoomNameInput);
            Put(serialized, "_roomListContent", refs.RoomListContent);
            Put(serialized, "_roomRowTemplate", refs.RoomRowTemplate);
            Put(serialized, "_roomsRefreshButton", refs.RoomsRefresh);
            Put(serialized, "_roomsCreateButton", refs.RoomsCreate);
            Put(serialized, "_roomsBackButton", refs.RoomsBack);
            Put(serialized, "_roomsPartyButton", refs.RoomsParty);
            Put(serialized, "_partyBrowseButton", refs.PartyBrowse);
            Put(serialized, "_partyLeaveButton", refs.PartyLeave);
            Put(serialized, "_partyReadyButton", refs.PartyReady);
            Put(serialized, "_partyBotsButton", refs.PartyBots);
            Put(serialized, "_partyStartButton", refs.PartyStart);
            Put(serialized, "_partyRetryButton", refs.PartyRetry);
            SerializedProperty preferences = serialized.FindProperty("_preferenceButtons");
            preferences.arraySize = refs.Preferences.Length;
            for (int i = 0; i < refs.Preferences.Length; ++i)
            {
                preferences.GetArrayElementAtIndex(i).objectReferenceValue = refs.Preferences[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Put(SerializedObject serialized, string name, UnityEngine.Object value)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null)
            {
                throw new InvalidOperationException("UI 직렬화 필드가 없다: " + name);
            }
            property.objectReferenceValue = value;
        }
    }
}
