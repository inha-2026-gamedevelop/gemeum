// System

// Unity
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Network;

namespace Geumeum.UI
{
    /// <summary> 서버 로스터를 표시하고 선호, 준비 요청만 전달하는 로비 화면 </summary>
    public sealed class LobbyWeaponPanel : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float REFRESH_INTERVAL      = 0.1f;
        private const int   WEAPON_OPTION_COUNT   = 3;

        private const int   CANVAS_SORTING_ORDER  = 200;
        private const float CANVAS_MATCH_BLEND    = 0.5f;
        private const float BODY_FONT_SIZE        = 24f;

        private static readonly Vector2 REFERENCE_RESOLUTION  = new Vector2(1920f, 1080f);
        private static readonly Vector2 CENTER_ANCHOR         = new Vector2(0.5f, 0.5f);

        private static readonly Color PANEL_COLOR           = new Color(0.05f, 0.07f, 0.10f, 0.99f);
        private static readonly Color LABEL_COLOR           = new Color(0.94f, 0.92f, 0.86f);
        private static readonly Color BUTTON_COLOR          = new Color(0.13f, 0.17f, 0.22f);
        private static readonly Color SELECTED_BUTTON_COLOR = new Color(0.36f, 0.30f, 0.19f);
        private static readonly Color GOLD                 = new Color(0.85f, 0.73f, 0.48f);
        private static readonly Color MUTED                = new Color(0.65f, 0.70f, 0.77f);
        private static readonly Color READY_COLOR          = new Color(0.52f, 0.81f, 0.74f);
        private static readonly Color LINE                 = new Color(0.22f, 0.27f, 0.33f);

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private TMP_FontAsset _font;

        private LobbyController         _lobby;
        private LobbyRoster             _roster;
        private RunManager              _run;
        private RunSceneDirector        _director;
        private GameObject              _root;
        private TMP_Text                _members;
        private TMP_Text                _partyStatus;
        private readonly TMP_Text[]     _names = new TMP_Text[WEAPON_OPTION_COUNT];
        private readonly TMP_Text[]     _roles = new TMP_Text[WEAPON_OPTION_COUNT];
        private readonly TMP_Text[]     _states = new TMP_Text[WEAPON_OPTION_COUNT];
        private readonly Image[]        _slotBars = new Image[WEAPON_OPTION_COUNT];
        private readonly Image[]        _slotBackgrounds = new Image[WEAPON_OPTION_COUNT];
        private readonly NetworkIdentity[] _nameIdentities = new NetworkIdentity[WEAPON_OPTION_COUNT];
        private readonly IDisplayNamed[] _displayNames = new IDisplayNamed[WEAPON_OPTION_COUNT];
        private TMP_Text                _readyLabel;
        private Button                  _ready;
        private Button                  _botFill;
        private TMP_Text                _botFillLabel;
        private readonly Button[]       _weapons = new Button[WEAPON_OPTION_COUNT];
        private LocalGameplayInputToken _input;
        private float                   _nextRefresh;
        private bool                    _localReady;
        private bool                    _enteredRun;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _lobby = GetComponent<LobbyController>();
            _roster = GetComponent<LobbyRoster>();
            _run = GetComponent<RunManager>();
            _director = GetComponent<RunSceneDirector>();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefresh)
            {
                return;
            }
            _nextRefresh = Time.unscaledTime + REFRESH_INTERVAL;
            if (AccountGameServer.IsAccountProcess && !AccountGameServer.Enabled && _run != null)
            {
                if (_run.Phase == RunPhase.Farming)
                {
                    _enteredRun = true;
                }

                if (_enteredRun && _run.Phase == RunPhase.Lobby) { Application.Quit(); return; }
            }
            bool visible = (_lobby != null) && _lobby.isClient && (NetworkClient.localPlayer != null)
                && (_run != null) && (_run.Phase == RunPhase.Lobby)
                && (_director != null) && (_director.CurrentScene == RunSceneId.Lobby)
                && (_director.State == RunTransitionState.Idle);
            if ((visible) && (_root == null))
            {
                Build();
            }
            if (_root != null)
            {
                _root.SetActive(visible);
            }
            if (!visible)
            {
                ReleaseInput();
                return;
            }
            if (_input == null)
            {
                _input = LocalGameplayInputGate.Acquire();
            }
            if (!AccountGameServer.IsAccountProcess)
            {
                RefreshRoster();
            }
        }

        private void LateUpdate() { LocalGameplayInputGate.Maintain(_input); }

        private void OnDisable()
        {
            if (_root != null)
            {
                _root.SetActive(false);
            }
            ReleaseInput();
        }

        private void OnDestroy() { ReleaseInput(); }

        /****************************************
        *                Methods
        ****************************************/

        private void ReleaseInput()
        {
            _input?.Dispose();
            _input = null;
        }

        private void RefreshRoster()
        {
            uint own = NetworkClient.localPlayer.netId;
            bool found = false;
            bool owner = false;
            int readyCount = 0;
            _localReady = false;
            for (int j = 0; j < _weapons.Length; ++j)
            {
                _weapons[j].image.color = BUTTON_COLOR;
            }

            for (int i = 0; i < _names.Length; ++i)
            {
                if (i >= _roster.SlotCount)
                {
                    _names[i].text = _lobby.FillWithBots ? "봇 참가 예정" : "빈자리";
                    _roles[i].text = _lobby.FillWithBots ? "출발 시 남은 역할을 맡습니다" : "함께할 파티원을 기다리는 중";
                    _states[i].text = _lobby.FillWithBots ? "자동 충원" : "모집 중";
                    _states[i].color = _lobby.FillWithBots ? GOLD : MUTED;
                    _slotBars[i].color = _lobby.FillWithBots ? GOLD : LINE;
                    _slotBackgrounds[i].color = new Color(0.075f, 0.10f, 0.14f);
                    _nameIdentities[i] = null;
                    _displayNames[i] = null;
                    continue;
                }
                LobbySlot slot = _roster.GetSlot(i);
                bool local = (slot.PlayerNetId == own);
                string choice = slot.HasPreference ? CharacterLabel(slot.PreferredCharacter) : "미선택";
                string nickname = $"참가자 {i + 1}";
                NetworkClient.spawned.TryGetValue(slot.PlayerNetId, out var player);
                if (_nameIdentities[i] != player)
                {
                    _nameIdentities[i] = player;
                    _displayNames[i] = player != null && player.TryGetComponent<IDisplayNamed>(out var named) ? named : null;
                }
                if (_displayNames[i] != null && !string.IsNullOrWhiteSpace(_displayNames[i].DisplayName))
                {
                    nickname = _displayNames[i].DisplayName;
                }

                _names[i].text = nickname + (local ? "  (나)" : "");
                _roles[i].text = (slot.IsRoomOwner ? "방장  ·  " : "파티원  ·  ") + choice;
                _states[i].text = slot.IsReady ? "준비 완료" : "준비 중";
                _states[i].color = slot.IsReady ? READY_COLOR : MUTED;
                _slotBars[i].color = slot.IsRoomOwner ? GOLD : slot.IsReady ? READY_COLOR : LINE;
                _slotBackgrounds[i].color = local ? new Color(0.14f, 0.18f, 0.23f) : new Color(0.10f, 0.14f, 0.19f);
                if (slot.IsReady)
                {
                    ++readyCount;
                }

                if (!local)
                {
                    continue;
                }
                found = true;
                owner = slot.IsRoomOwner;
                _localReady = slot.IsReady;
                for (int j = 0; j < _weapons.Length; ++j)
                {
                    _weapons[j].image.color = ((slot.HasPreference) && ((int)slot.PreferredCharacter == j))
                        ? SELECTED_BUTTON_COLOR : BUTTON_COLOR;
                }
            }
            _members.text = $"1 파티    {_roster.SlotCount} / {Constants.Network.MAX_PLAYERS}";
            _partyStatus.text = $"준비 완료  {readyCount} / {_roster.SlotCount}\n" +
                (_lobby.FillWithBots ? "빈자리 봇 충원 사용 중" : "파티원 3명이 모이면 출발합니다");
            _ready.interactable = found;
            _readyLabel.text = _localReady ? "준비 취소" : "준비 완료";
            _botFill.gameObject.SetActive(true);
            _botFill.interactable = found && owner;
            _botFillLabel.text = owner ? (_lobby.FillWithBots ? "봇 충원 취소" : "빈자리 봇 추가") : "봇 설정 · 방장 전용";
            foreach (Button button in _weapons)
            {
                button.interactable = found;
            }
        }

        private static string CharacterLabel(CharacterKind character)
        {
            switch (character)
            {
                case CharacterKind.Chasa:
                    return "검, 차사";
                case CharacterKind.Singung:
                    return "활, 신궁";
                case CharacterKind.Munyeo:
                    return "방울, 무녀";
                default:
                    return "미선택";
            }
        }

        private void Build()
        {
            // EventSystem은 같은 프리팹의 RunEventSystem이 맡는다. 여기서 또 만들지 않고, 없으면 버튼이 눌리지 않으니 구성 오류로 알린다
            if (EventSystem.current == null)
            {
                Debug.LogError("[LobbyWeaponPanel] 활성 EventSystem이 없어 로비 버튼 입력을 받을 수 없다", this);
            }
            _root = new GameObject("LobbyWeaponSelection", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _root.transform.SetParent(transform, false);
            Canvas canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CANVAS_SORTING_ORDER;
            CanvasScaler scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = REFERENCE_RESOLUTION;
            scaler.matchWidthOrHeight = CANVAS_MATCH_BLEND;
            if (AccountGameServer.IsAccountProcess)
            {
                var loading = Surface("ExpeditionLoading", _root.transform, new Vector2(2400f, 1500f), Vector2.zero, PANEL_COLOR, false);
                Label(loading, "원정에 입장하는 중", new Vector2(0f, 35f), new Vector2(1000f, 70f), 40f).color = GOLD;
                Label(loading, "런처에서 준비한 파티를 연결하고 있습니다.", new Vector2(0f, -45f), new Vector2(1000f, 60f), 25f);
                FixedAspectViewport.Register(canvas);
                return;
            }
            Surface("Backdrop", _root.transform, new Vector2(2400f, 1500f), Vector2.zero, new Color(0.02f, 0.03f, 0.05f, 0.70f), false);
            RectTransform panel = Surface("RaidLobby", _root.transform, new Vector2(1260f, 820f), Vector2.zero, PANEL_COLOR);
            LeftLabel(panel, "그믐  /  원정대", new Vector2(-280f, 365f), new Vector2(610f, 28f), 19f, GOLD);
            LeftLabel(panel, "출발 준비", new Vector2(-280f, 316f), new Vector2(610f, 60f), 40f, LABEL_COLOR);
            Label(panel, "3인 협동 원정", new Vector2(414f, 330f), new Vector2(320f, 40f), 22f).color = MUTED;
            Surface("HeaderRule", panel, new Vector2(1170f, 2f), new Vector2(0f, 269f), GOLD, false);
            _members = LeftLabel(panel, "1 파티", new Vector2(-234f, 230f), new Vector2(702f, 40f), 24f, GOLD);
            for (int i = 0; i < _names.Length; ++i)
            {
                RectTransform row = Surface("PartySlot" + i, panel, new Vector2(702f, 96f), new Vector2(-234f, 148f - i * 112f), BUTTON_COLOR);
                _slotBackgrounds[i] = row.GetComponent<Image>();
                _slotBars[i] = Surface("StatusBar", row, new Vector2(3f, 94f), new Vector2(-348f, 0f), GOLD, false).GetComponent<Image>();
                Label(row, (i + 1).ToString("00"), new Vector2(-306f, 0f), new Vector2(60f, 60f), 26f).color = GOLD;
                _names[i] = LeftLabel(row, "빈자리", new Vector2(-64f, 18f), new Vector2(400f, 38f), 28f, LABEL_COLOR);
                _names[i].overflowMode = TextOverflowModes.Ellipsis;
                _names[i].textWrappingMode = TextWrappingModes.NoWrap;
                _roles[i] = LeftLabel(row, "", new Vector2(-64f, -21f), new Vector2(400f, 30f), 20f, MUTED);
                _states[i] = Label(row, "모집 중", new Vector2(250f, 0f), new Vector2(150f, 44f), 22f);
            }
            RectTransform info = Surface("ExpeditionInfo", panel, new Vector2(420f, 436f), new Vector2(375f, 44f), new Color(0.075f, 0.105f, 0.15f));
            Label(info, "원정 안내", new Vector2(0f, 174f), new Vector2(350f, 40f), 24f).color = GOLD;
            Surface("InfoRule", info, new Vector2(350f, 1f), new Vector2(0f, 136f), LINE, false);
            Label(info, "검 · 활 · 방울", new Vector2(0f, 94f), new Vector2(350f, 44f), 30f);
            Label(info, "서로 다른 세 역할로 함께 출발합니다.", new Vector2(0f, 48f), new Vector2(370f, 36f), 19f).color = MUTED;
            _partyStatus = Label(info, "", new Vector2(0f, -32f), new Vector2(370f, 80f), 22f);
            _botFill = MakeButton(info, "빈자리 봇 추가", new Vector2(0f, -138f), new Vector2(348f, 58f), out _botFillLabel);
            _botFill.onClick.AddListener(() => _lobby.RequestBotFill(!_lobby.FillWithBots));
            LeftLabel(panel, "내 선호 무기", new Vector2(-234f, -172f), new Vector2(702f, 36f), 21f, GOLD);
            string[] names = { "검, 차사", "활, 신궁", "방울, 무녀" };
            WeaponKind[] kinds = { WeaponKind.Sword, WeaponKind.Bow, WeaponKind.Bell };
            for (int i = 0; i < WEAPON_OPTION_COUNT; ++i)
            {
                WeaponKind weapon = kinds[i];
                var position = new Vector2(-467f + i * 233f, -226f);
                _weapons[i] = MakeButton(panel, names[i], position, new Vector2(218f, 58f), out _);
                _weapons[i].onClick.AddListener(() => _lobby.RequestWeaponPreference(weapon));
            }
            Surface("FooterRule", panel, new Vector2(1170f, 1f), new Vector2(0f, -281f), LINE, false);
            LeftLabel(panel, "선호가 겹치면 역할을 추첨합니다. 선택을 바꾸면 준비가 취소됩니다.\n모든 파티원이 준비하면 자동으로 출발합니다.", new Vector2(-192f, -337f), new Vector2(786f, 76f), 20f, MUTED);
            _ready = MakeButton(panel, "준비 완료", new Vector2(420f, -337f), new Vector2(330f, 68f), out _readyLabel);
            _ready.image.color = GOLD;
            _readyLabel.color = new Color(0.08f, 0.07f, 0.05f);
            _ready.onClick.AddListener(() => _lobby.RequestReady(!_localReady));
            FixedAspectViewport.Register(canvas);
        }

        private static RectTransform Element(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = CENTER_ANCHOR;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        private static RectTransform Surface(string name, Transform parent, Vector2 size, Vector2 position, Color color, bool border = true)
        {
            RectTransform rect = Element(name, parent, size, position);
            rect.gameObject.AddComponent<Image>().color = color;
            if (border)
            {
                var outline = rect.gameObject.AddComponent<Outline>();
                outline.effectColor = LINE;
                outline.effectDistance = new Vector2(1f, -1f);
            }
            return rect;
        }

        private TMP_Text LeftLabel(Transform parent, string value, Vector2 position, Vector2 size, float fontSize, Color color)
        {
            TMP_Text label = Label(parent, value, position, size, fontSize);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.color = color;
            return label;
        }

        private TMP_Text Label(Transform parent, string value, Vector2 position, Vector2 size, float fontSize)
        {
            var label = Element("Label", parent, size, position).gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null)
            {
                label.font = _font;
            }
            label.text = value;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = LABEL_COLOR;
            label.raycastTarget = false;
            label.richText = false;
            return label;
        }

        private Button MakeButton(Transform parent, string text, Vector2 position, Vector2 size, out TMP_Text label)
        {
            RectTransform rect = Surface(text, parent, size, position, BUTTON_COLOR);
            Button button = rect.gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
            colors.selectedColor = new Color(1.25f, 1.25f, 1.25f);
            colors.disabledColor = new Color(0.48f, 0.48f, 0.48f);
            button.colors = colors;
            label = Label(rect, text, Vector2.zero, size, BODY_FONT_SIZE);
            return button;
        }
    }
}
