// System
using System.Text;

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
    /// <summary> 서버 로스터를 표시하고 선호, 준비 요청만 전달하는 로비 화면. </summary>
    public sealed class LobbyWeaponPanel : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float REFRESH_INTERVAL      = 0.1f;
        private const int   WEAPON_OPTION_COUNT   = 3;
        private const int   CENTER_WEAPON_INDEX   = 1;
        private const float WEAPON_BUTTON_SPACING = 215f;
        private const float WEAPON_BUTTON_Y       = 78f;

        private const int   CANVAS_SORTING_ORDER  = 200;
        private const float CANVAS_MATCH_BLEND    = 0.5f;
        private const float TITLE_FONT_SIZE       = 34f;
        private const float BODY_FONT_SIZE        = 24f;
        private const float NOTICE_FONT_SIZE      = 19f;

        private static readonly Vector2 REFERENCE_RESOLUTION  = new Vector2(1920f, 1080f);
        private static readonly Vector2 CENTER_ANCHOR         = new Vector2(0.5f, 0.5f);
        private static readonly Vector2 PANEL_SIZE            = new Vector2(720f, 510f);
        private static readonly Vector2 TITLE_POSITION        = new Vector2(0f, 200f);
        private static readonly Vector2 TITLE_SIZE            = new Vector2(650f, 55f);
        private static readonly Vector2 SUBTITLE_POSITION     = new Vector2(0f, 144f);
        private static readonly Vector2 SUBTITLE_SIZE         = new Vector2(650f, 45f);
        private static readonly Vector2 WEAPON_BUTTON_SIZE    = new Vector2(200f, 58f);
        private static readonly Vector2 MEMBERS_POSITION      = new Vector2(0f, -30f);
        private static readonly Vector2 MEMBERS_SIZE          = new Vector2(640f, 140f);
        private static readonly Vector2 NOTICE_POSITION       = new Vector2(0f, -135f);
        private static readonly Vector2 NOTICE_SIZE           = new Vector2(660f, 65f);
        private static readonly Vector2 READY_BUTTON_POSITION = new Vector2(0f, -210f);
        private static readonly Vector2 READY_BUTTON_SIZE     = new Vector2(300f, 58f);

        private static readonly Color PANEL_COLOR           = new Color(0.07f, 0.09f, 0.12f, 0.98f);
        private static readonly Color LABEL_COLOR           = new Color(0.94f, 0.92f, 0.86f);
        private static readonly Color BUTTON_COLOR          = new Color(0.20f, 0.23f, 0.28f);
        private static readonly Color SELECTED_BUTTON_COLOR = new Color(0.24f, 0.49f, 0.45f);

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
        private TMP_Text                _readyLabel;
        private Button                  _ready;
        private readonly Button[]       _weapons = new Button[WEAPON_OPTION_COUNT];
        private LocalGameplayInputToken _input;
        private float                   _nextRefresh;
        private bool                    _localReady;

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
            RefreshRoster();
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
            var text = new StringBuilder();
            text.AppendLine($"참가자 {_roster.SlotCount} / {Constants.Network.MAX_PLAYERS}");
            for (int i = 0; i < _roster.SlotCount; ++i)
            {
                LobbySlot slot = _roster.GetSlot(i);
                bool local = (slot.PlayerNetId == own);
                string choice = slot.HasPreference ? CharacterLabel(slot.PreferredCharacter) : "미선택";
                text.AppendLine($"{(local ? "나" : $"참가자 {i + 1}")}     {choice}     {(slot.IsReady ? "준비 완료" : "선택 중")}");
                if (!local)
                {
                    continue;
                }
                found = true;
                _localReady = slot.IsReady;
                for (int j = 0; j < _weapons.Length; ++j)
                {
                    _weapons[j].image.color = ((slot.HasPreference) && ((int)slot.PreferredCharacter == j))
                        ? SELECTED_BUTTON_COLOR : BUTTON_COLOR;
                }
            }
            _members.text = text.ToString();
            _ready.interactable = found;
            _readyLabel.text = _localReady ? "준비 취소" : "준비 완료";
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
            RectTransform panel = Element("Panel", _root.transform, PANEL_SIZE, Vector2.zero);
            panel.gameObject.AddComponent<Image>().color = PANEL_COLOR;
            Label(panel, "출발 준비", TITLE_POSITION, TITLE_SIZE, TITLE_FONT_SIZE);
            Label(panel, "선호 무기를 선택하세요", SUBTITLE_POSITION, SUBTITLE_SIZE, BODY_FONT_SIZE);
            string[] names = { "검, 차사", "활, 신궁", "방울, 무녀" };
            WeaponKind[] kinds = { WeaponKind.Sword, WeaponKind.Bow, WeaponKind.Bell };
            for (int i = 0; i < WEAPON_OPTION_COUNT; ++i)
            {
                WeaponKind weapon = kinds[i];
                var position = new Vector2((i - CENTER_WEAPON_INDEX) * WEAPON_BUTTON_SPACING, WEAPON_BUTTON_Y);
                _weapons[i] = MakeButton(panel, names[i], position, WEAPON_BUTTON_SIZE, out _);
                _weapons[i].onClick.AddListener(() => _lobby.RequestWeaponPreference(weapon));
            }
            _members = Label(panel, "", MEMBERS_POSITION, MEMBERS_SIZE, BODY_FONT_SIZE);
            Label(panel, "선호가 겹치면 서버가 추첨합니다. 검, 활, 방울 한 명씩 출발합니다.\n선택을 바꾸면 준비가 취소됩니다.", NOTICE_POSITION, NOTICE_SIZE, NOTICE_FONT_SIZE);
            _ready = MakeButton(panel, "준비 완료", READY_BUTTON_POSITION, READY_BUTTON_SIZE, out _readyLabel);
            _ready.onClick.AddListener(() => _lobby.RequestReady(!_localReady));
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
            return label;
        }

        private Button MakeButton(Transform parent, string text, Vector2 position, Vector2 size, out TMP_Text label)
        {
            RectTransform rect = Element(text, parent, size, position);
            rect.gameObject.AddComponent<Image>().color = BUTTON_COLOR;
            Button button = rect.gameObject.AddComponent<Button>();
            label = Label(rect, text, Vector2.zero, size, BODY_FONT_SIZE);
            return button;
        }
    }
}
