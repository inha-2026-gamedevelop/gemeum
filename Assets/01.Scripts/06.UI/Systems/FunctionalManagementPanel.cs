// System
using System;
using System.Collections.Generic;

// Unity
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Third party
using Mirror;

// Project
using Geumeum.Combat;
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;
using Geumeum.Systems;

namespace Geumeum.UI
{
    /// <summary> 인벤토리, 특성, 객주와 장비 양도를 실제 요청 API에 연결한다 </summary>
    [DisallowMultipleComponent]
    public sealed class FunctionalManagementPanel : MonoBehaviour
    {
        private const float SNAPSHOT_POLL_INTERVAL = 0.2f;
        private const float NETWORK_SCAN_INTERVAL = 0.5f;
        // 가지 하나는 4열 x 5행 칸을 가진다. 지금 트리는 무기마다 뿌리 1 + 세 갈래 4개를 쓴다
        // 칸 번호는 가지 * 20 + 열 * 5 + 행이다. 가지는 파티 순번(1P, 2P, 3P)이다
        private const int TALENT_COLUMNS_PER_ARM = 4;
        private const int TALENT_ROWS_PER_COLUMN = 5;
        private const int TALENT_SLOTS_PER_ARM   = TALENT_COLUMNS_PER_ARM * TALENT_ROWS_PER_COLUMN;
        private const int TALENT_ARM_COUNT       = Constants.Network.MAX_PLAYERS;
        private const int TALENT_BUTTON_COUNT    = TALENT_ARM_COUNT * TALENT_SLOTS_PER_ARM;

        // 특성 노드 설명 상자. 특성 페이지 좌표계(1280x720) px
        private const float TALENT_TOOLTIP_WIDTH         = 280f;
        private const float TALENT_TOOLTIP_PADDING       = 12f;
        private const float TALENT_TOOLTIP_GAP           = 8f;
        private const float TALENT_TOOLTIP_FONT_SIZE     = 15f;
        private const float TALENT_TOOLTIP_MIN_FONT_SIZE = 9f;

        // 제작 UI 팔레트와 같은 값을 쓴다
        private static readonly Color G_TooltipBackground = new Color(0.075f, 0.095f, 0.13f, 0.98f);
        private static readonly Color G_TooltipText = new Color(0.96f, 0.95f, 0.91f, 1f);

        // 별자리. 고른 노드만 빛나고 고르지 않은 노드는 모두 회색이다
        // 고를 수 있는 노드와 잠긴 노드는 색이 아니라 모양(별/점)으로 가른다
        private static readonly Color G_TalentAllocated = new Color(0.86f, 0.97f, 1f, 1f);
        private static readonly Color G_TalentOpen      = new Color(0.62f, 0.62f, 0.62f, 0.9f);
        private static readonly Color G_TalentLocked    = new Color(0.5f, 0.5f, 0.5f, 0.85f);
        private static readonly Color G_TalentOther     = new Color(0.42f, 0.42f, 0.42f, 0.6f);
        private static readonly Color G_TalentLineLit   = new Color(0.75f, 0.9f, 1f, 0.9f);
        private static readonly Color G_TalentLine      = new Color(0.5f, 0.5f, 0.5f, 0.55f);
        private static readonly Color G_TalentLineOther = new Color(0.4f, 0.4f, 0.4f, 0.4f);
        private static readonly Color G_TalentArmLabel  = new Color(0.86f, 0.9f, 1f, 1f);
        private static readonly Color G_TalentArmOther  = new Color(0.6f, 0.65f, 0.78f, 0.7f);

        private static readonly ShopItemKind[] G_ShopItems =
        {
            ShopItemKind.HealthPotion,
            ShopItemKind.SpiritPotion,
            ShopItemKind.RerollTicket,
            ShopItemKind.UpgradeTicket,
        };

        [Header("창")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Button _closeButton;
        [Tooltip("옆으로 미는 대상. 우측 콘텐츠를 한 덩어리로 들고 있는 Window를 넣는다")]
        [SerializeField] private RectTransform _slideRoot;
        [Tooltip("닫힌 상태에서 오른쪽으로 밀어 두는 거리(px, 1280 기준). 우측 판 왼쪽 끝이 180이라 460이면 화면 밖으로 빠진다")]
        [SerializeField] private float _slideDistance = 480f;
        [Tooltip("열리고 닫히는 데 걸리는 시간(초)")]
        [SerializeField] private float _slideDuration = 0.22f;
        [SerializeField] private Button _shopTabButton;
        [SerializeField] private GameObject _inventoryPage;
        [SerializeField] private GameObject _talentPage;
        [SerializeField] private GameObject _shopPage;
        [SerializeField] private GameObject _transferPage;

        [Header("인벤토리")]
        [SerializeField] private TMP_Text _equippedWeaponText;
        [SerializeField] private FunctionalUiIndexedButton[] _inventoryButtons;
        [SerializeField] private TMP_Text _inventoryDetailText;
        [SerializeField] private TMP_Text _inventoryStatusText;
        [SerializeField] private Button _equipButton;
        [SerializeField] private Button _unequipButton;
        [SerializeField] private Button _usePotionButton;
        [SerializeField] private Button _useTicketButton;
        [SerializeField] private Button _targetEquippedButton;

        // 레일이 사라진 뒤 보내는 쪽이 양도 페이지로 들어갈 유일한 입구다.
        // 받는 쪽은 제안이 도착하면 자동으로 열린다(HandleTransferChanged)
        [SerializeField] private Button _openTransferButton;

        [Header("배낭 필터")]
        [Tooltip("무기·물약·권 순서대로. 같은 것을 다시 누르면 풀린다")]
        [SerializeField] private FunctionalUiIndexedButton[] _inventoryFilterButtons =
            new FunctionalUiIndexedButton[0];

        [Header("곁에 선 파티원에게 바로 양도")]
        [Tooltip("배낭 페이지 안에서 장비를 끌어다 놓는 상자")]
        [SerializeField] private RectTransform _quickTransferBox;

        [Tooltip("상자에 올려 둔 장비 이름")]
        [SerializeField] private TMP_Text _quickTransferBoxLabel;

        [Tooltip("누구에게 주는지 적는 줄. 장비를 올리면 상자 문구가 장비 이름으로 바뀌므로 따로 둔다")]
        [SerializeField] private TMP_Text _quickTransferTitleText;

        [SerializeField] private Button _quickTransferConfirmButton;
        [SerializeField] private Button _quickTransferCancelButton;

        [Tooltip("끄는 동안 커서를 따라다니는 표시. 창이 밀려도 커서에서 어긋나지 않게 Window 밖에 둔다")]
        [SerializeField] private RectTransform _quickTransferDragGhost;

        [SerializeField] private TMP_Text _quickTransferDragGhostLabel;

        [Header("특성")]
        [SerializeField] private TMP_Text _talentPointText;
        [SerializeField] private TMP_Text _talentStatusText;
        [SerializeField] private TMP_Text[] _talentColumnTexts;
        [SerializeField] private FunctionalUiIndexedButton[] _talentButtons;
        [SerializeField] private Button _talentResetButton;

        [Header("특성 별자리")]
        [SerializeField] private TalentConstellationLayout _talentConstellation =
            new TalentConstellationLayout();

        [Tooltip("가운데 그믐. 별자리 중심으로 옮긴다")]
        [SerializeField] private RectTransform _talentHub;

        [Tooltip("고른 노드와 고를 수 있는 노드")]
        [SerializeField] private Sprite _talentStarSprite;

        [Tooltip("아직 잠긴 노드와 남의 가지 노드")]
        [SerializeField] private Sprite _talentDotSprite;

        [Tooltip("특성 탭에서는 끈다. 별하늘을 가리는 우측 판 같은 것")]
        [SerializeField] private GameObject[] _hideOnTalentPage = new GameObject[0];

        [Header("객주")]
        [SerializeField] private TMP_Text _walletText;
        [SerializeField] private TMP_Text _shopHintText;
        [SerializeField] private TMP_Text _shopStatusText;
        [SerializeField] private FunctionalUiIndexedButton[] _shopButtons;

        [Header("양도")]
        [SerializeField] private TMP_Text _transferStatusText;
        [SerializeField] private FunctionalUiIndexedButton[] _recipientButtons;
        [SerializeField] private Button _acceptTransferButton;
        [SerializeField] private Button _rejectTransferButton;
        [SerializeField] private Button _cancelTransferButton;

        [Header("세션 서비스")]
        [SerializeField] private CurrencyManager _currency;
        [SerializeField] private ShopTransactionService _shopTransactions;
        [SerializeField] private EquipmentTransferService _transferService;

        // 씬에 꺼진 사본이 함께 있을 수 있어 살아 있는 것만 모아 둔다. Find로 씬을 훑지 않으려는 것이다
        private static readonly List<FunctionalManagementPanel> G_Live =
            new List<FunctionalManagementPanel>();

        private readonly InventorySlotSnapshot[] _slots =
            new InventorySlotSnapshot[InventorySnapshot.SLOT_COUNT];
        // 내가 고를 수 있는 노드만 담는다. 남의 가지는 그리기만 하므로 _drawnTalentNodeIds에만 있다
        private readonly int[] _visibleTalentNodeIds = new int[TALENT_BUTTON_COUNT];
        private readonly int[] _drawnTalentNodeIds = new int[TALENT_BUTTON_COUNT];

        // 파티원 무기. 남의 가지를 그 사람 무기의 트리 모양으로 그린다
        private readonly Dictionary<uint, PlayerWeapon> _partyWeapons =
            new Dictionary<uint, PlayerWeapon>();
        private readonly List<uint> _talentArmNetIds = new List<uint>();
        private readonly Dictionary<uint, ShopInteractionPoint> _shopPoints =
            new Dictionary<uint, ShopInteractionPoint>();
        private readonly Dictionary<uint, RecipientEntry> _recipients =
            new Dictionary<uint, RecipientEntry>();
        private readonly Dictionary<uint, NetworkIdentity> _scannedIdentities =
            new Dictionary<uint, NetworkIdentity>();
        private readonly List<uint> _staleKeys = new List<uint>();
        private readonly List<RecipientEntry> _visibleRecipients = new List<RecipientEntry>();

        private NetworkIdentity _localIdentity;
        private InventoryController _inventory;
        private BotInventoryView _botInventoryView;
        [SerializeField] private TMP_Text _pageTitle;
        private TalentController _talents;
        private LocalGameplayInputToken _inputToken;
        private TalentAllocationSnapshot _allocation;
        private EquipmentTransferOfferSnapshot _activeOffer;
        private ShopInteractionPoint _activeShopPoint;

        private uint _inventoryRevision;

        // 배낭 12칸을 한 번이라도 받았는지. revision 0은 "아직 못 받음"이 아니라 빈 배낭의 정상 시작값이라
        // revision으로 대신 판단하면 빈 배낭에서는 첫 구매 · 장착 버튼이 영영 열리지 않는다
        private bool _hasInventorySnapshot;

        // 곁에 선 파티원에게 바로 주는 모드. 0이면 평소 배낭이다
        private uint   _quickRecipientNetId = 0u;
        private string _quickRecipientName  = string.Empty;
        // 지금 고른 분류. None이면 필터가 꾸지 않았다는 뜻
        private InventoryFilter _inventoryFilter = InventoryFilter.None;

        private int    _quickBoxSlot        = -1;
        private int    _quickDragSlot       = -1;
        private uint _talentRevision;
        private uint _observedRunId;
        private uint _observedSceneRevision;
        private int _selectedSlot = -1;
        private int _selectedTicketSlot = -1;
        private int _selectedEquipmentSlot = -1;
        private bool _targetEquipped;
        private bool _preserveTransferFeedback;
        private bool _isVisible;
        private float _nextSnapshotPollTime;
        private float _nextNetworkScanTime;

        // 슬라이드 연출. 0이면 완전히 닫힌 자리, 1이면 제자리다
        private float _revealProgress;
        private Vector2 _slideOpenPosition;
        private bool _hasSlideOrigin;

        public static FunctionalManagementPanel Instance { get; private set; }

        /// <summary> 관리 창이 열리고 닫힐 때. HUD처럼 같이 숨겨야 하는 화면이 듣는다 </summary>
        public static event Action<bool> VisibilityChanged;

        public bool IsVisible => _isVisible;

        internal CurrencyManager Currency => _currency;
        internal bool IsActiveInHierarchy => gameObject.activeInHierarchy;
        internal bool IsCanvasEnabled
        {
            get
            {
                Canvas canvas = GetComponentInParent<Canvas>();
                return (canvas != null) && canvas.isActiveAndEnabled;
            }
        }
        internal bool IsTalentPageActive => (_talentPage != null) &&
            _talentPage.activeInHierarchy;
        internal bool CachedTalentHasWeapon => _allocation.HasWeapon;
        internal string InventoryStatusMessage => ReadText(_inventoryStatusText);
        internal string TalentStatusMessage => ReadText(_talentStatusText);
        internal string ShopStatusMessage => ReadText(_shopStatusText);
        internal string TransferStatusMessage => ReadText(_transferStatusText);
        internal EquipmentTransferOfferSnapshot ActiveTransferOffer => _activeOffer;

        internal int[] CopyVisibleTalentNodeIds()
        {
            return (int[])_visibleTalentNodeIds.Clone();
        }

        internal uint[] CopyVisibleRecipientNetIds()
        {
            uint[] netIds = new uint[_visibleRecipients.Count];
            for (int i = 0; i < _visibleRecipients.Count; ++i)
            {
                netIds[i] = _visibleRecipients[i].NetId;
            }

            return netIds;
        }

        private readonly struct RecipientEntry
        {
            public readonly uint NetId;
            public readonly NetworkIdentity Identity;
            public readonly string DisplayName;

            public RecipientEntry(uint netId, NetworkIdentity identity, string displayName)
            {
                NetId = netId;
                Identity = identity;
                DisplayName = displayName;
            }
        }

        private enum PageKind
        {
            Inventory,
            Talent,
            Shop,
            Transfer,
        }

        /// <summary> 배낭에서 어느 분류를 돋보이게 할지. 칸을 감추지 않고 테두리만 밝힌다 </summary>
        private enum InventoryFilter
        {
            None      = 0,
            Weapon    = 1,
            Potion    = 2,
            Ticket    = 3,
        }

        // 소유권(Instance)은 여기서 잡지 않는다. 꺼진 사본도 Awake는 돌기 때문에
        // 꺼져 있는 쪽이 Instance를 차지한다 - 등록은 OnEnable/ReleaseRegistration이 맡는다
        private void Awake()
        {
            EnsureTalentViewCapacity();

            if ((_inventoryPage != null) && (_inventoryDetailText != null))
            {
                _botInventoryView = BotInventoryView.Create(_inventoryPage, _inventoryDetailText.font);
            }

            BindButtons();
            CacheSlideOrigin();
            SetVisible(false, true);
        }

        private readonly List<Image> _talentEdges = new List<Image>();

        // 노드 버튼 안에 둔 별 그림. 버튼 자체는 투명한 누름 영역이다
        private Image[] _talentNodeImages = new Image[0];

        // 가지 주인 이름. 첫 열 이름표를 복제해 만든다
        private TMP_Text[] _talentArmTexts = new TMP_Text[0];

        // 특성 노드 상세 설명. 프리팹을 고치지 않도록 런타임에 만든다
        private GameObject _talentTooltip;
        private RectTransform _talentTooltipRect;
        private TMP_Text _talentTooltipText;
        private int _hoveredTalentIndex = -1;

        // 지금 열려 있는 탭. 계층 활성 상태가 아니라 이 값으로 판단해야 키보드 select 잔류나
        // 바깥에서 들어온 이벤트로 특성 설명이 다른 탭 위에 뜨지 않는다
        private PageKind _currentPage = PageKind.Inventory;

        /// <summary>
        /// 프리팹에 있는 노드 버튼 하나를 본으로 가지 셋 몫의 칸을 채우고 별 모양으로 바꾼다
        /// 프리팹에는 버튼을 몇 개만 두어도 된다 - 칸 수는 코드가 맞춘다
        /// </summary>
        private void EnsureTalentViewCapacity()
        {
            if (_talentButtons == null || _talentButtons.Length == 0 || _talentButtons[0] == null) { return; }

            int previous = _talentButtons.Length;
            Array.Resize(ref _talentButtons, TALENT_BUTTON_COUNT);
            for (int i = previous; i < TALENT_BUTTON_COUNT; ++i)
            {
                var button = Instantiate(_talentButtons[0], _talentButtons[0].transform.parent);
                button.name = "TalentNode" + i;
                button.SetIndex(i);
                _talentButtons[i] = button;
            }

            _talentNodeImages = new Image[TALENT_BUTTON_COUNT];
            for (int i = 0; i < TALENT_BUTTON_COUNT; ++i)
            {
                _talentNodeImages[i] = StyleTalentNode(_talentButtons[i]);
            }

            if (_talentColumnTexts != null && _talentColumnTexts.Length > 0)
            {
                previous = _talentColumnTexts.Length;
                Array.Resize(ref _talentColumnTexts, TALENT_COLUMNS_PER_ARM);
                for (int i = previous; i < TALENT_COLUMNS_PER_ARM; ++i)
                { _talentColumnTexts[i] = Instantiate(_talentColumnTexts[0], _talentColumnTexts[0].transform.parent); }

                _talentArmTexts = new TMP_Text[TALENT_ARM_COUNT];
                for (int i = 0; i < TALENT_ARM_COUNT; ++i)
                {
                    TMP_Text label = Instantiate(_talentColumnTexts[0], _talentColumnTexts[0].transform.parent);
                    label.name = "TalentArm" + i;
                    _talentArmTexts[i] = label;
                }
            }

            if (_talentHub != null)
            {
                _talentHub.anchoredPosition = _talentConstellation.Center;
            }

            EnsureTalentTooltip();
        }

        /// <summary>
        /// 버튼 판을 투명한 누름 영역으로 두고 그 안에 별 그림을 하나 넣는다
        /// 별이 점만 해져도 가리키기 쉬워야 하므로 누름 영역은 크기를 고정한다
        /// </summary>
        private Image StyleTalentNode(FunctionalUiIndexedButton button)
        {
            Transform existing = button.transform.Find("Star");
            if (existing != null)
            {
                return existing.GetComponent<Image>();
            }

            // 비활성 틴트가 노드 색을 덮어쓰지 않게 한다. 상태 색은 RefreshTalentView가 정한다
            Button uiButton = button.GetComponent<Button>();
            if (uiButton != null)
            {
                uiButton.transition = Selectable.Transition.None;
            }

            Image hitArea = button.GetComponent<Image>();
            if (hitArea != null)
            {
                hitArea.sprite = null;
                hitArea.color = Color.clear;
                hitArea.raycastTarget = true;
            }

            if (button.Label != null)
            {
                button.Label.gameObject.SetActive(false);
            }

            var rect = (RectTransform)button.transform;
            rect.sizeDelta = Vector2.one * _talentConstellation.HitSize;

            GameObject star = new GameObject("Star", typeof(RectTransform), typeof(Image));
            star.transform.SetParent(button.transform, false);
            Image image = star.GetComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            return image;
        }

        /// <summary> 한 무기 트리의 노드 자리. 열 수와 줄기 길이는 데이터에서 읽는다 </summary>
        private Vector2 TalentPosition(TalentTreeSO tree, int arm, WeaponKind weapon, int column, int row)
        {
            int trunkRows = tree.TryGetColumn(weapon, 0, out TalentTreeSO.ColumnEntry trunk)
                ? trunk.NodeCount : 1;
            return _talentConstellation.NodePosition(arm, column, row,
                tree.CountColumns(weapon), trunkRows);
        }

        /// <summary>
        /// 노드 설명 상자를 노드와 같은 부모에 한 번만 만든다
        ///
        /// 배경과 글자 모두 raycastTarget을 끈다. 설명이 노드 위에 떠도 클릭을 가리면
        /// 마우스를 올린 노드를 고를 수 없게 된다
        /// </summary>
        private void EnsureTalentTooltip()
        {
            if ((_talentTooltip != null) || (_talentButtons == null) ||
                (_talentButtons.Length == 0) || (_talentButtons[0] == null))
            {
                return;
            }

            RectTransform template = (RectTransform)_talentButtons[0].transform;
            GameObject root = new GameObject("TalentTooltip", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(template.parent, false);
            root.transform.SetAsLastSibling();

            _talentTooltipRect = (RectTransform)root.transform;
            _talentTooltipRect.anchorMin = template.anchorMin;
            _talentTooltipRect.anchorMax = template.anchorMax;
            _talentTooltipRect.pivot = new Vector2(0.5f, 0.5f);

            Image background = root.GetComponent<Image>();
            background.color = G_TooltipBackground;
            background.raycastTarget = false;

            GameObject textObject = new GameObject("TalentTooltipText", typeof(RectTransform));
            textObject.transform.SetParent(root.transform, false);

            float padding = TALENT_TOOLTIP_PADDING;
            RectTransform textRect = (RectTransform)textObject.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(padding, padding);
            textRect.offsetMax = new Vector2(-padding, -padding);

            TextMeshProUGUI label = textObject.AddComponent<TextMeshProUGUI>();
            label.font = ResolveTalentFont();
            label.fontSize = TALENT_TOOLTIP_FONT_SIZE;
            label.color = G_TooltipText;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.raycastTarget = false;

            // 줄바꿈을 명시한다. 런타임으로 만든 라벨이 줄바꿈 없음으로 시작하면 긴 설명이
            // 상자 밖으로 가로로 흘러 나가고, GetPreferredValues도 한 줄 높이만 돌려주어
            // 상자가 글을 따라 커지지 않는다. 프로젝트 TMP 기본값에 기대지 않는다
            label.textWrappingMode = TextWrappingModes.Normal;

            // 넘치면 접어서 보여 줄 뿐 잘라내지 않는다. 설명은 끝까지 남아야 한다
            label.overflowMode = TextOverflowModes.Overflow;
            _talentTooltipText = label;

            _talentTooltip = root;
            root.SetActive(false);
        }

        /// <summary> 특성 페이지가 이미 쓰고 있는 폰트를 그대로 쓴다 </summary>
        private TMP_FontAsset ResolveTalentFont()
        {
            if ((_talentButtons[0].Label != null) && (_talentButtons[0].Label.font != null))
            {
                return _talentButtons[0].Label.font;
            }

            return (_talentPointText == null) ? null : _talentPointText.font;
        }

        /// <summary>
        /// 마우스나 키보드 선택이 특성 노드에 머물기 시작하거나 벗어났을 때
        /// 매 프레임 다시 그리지 않고 이 시점과 특성 갱신 시점에만 내용을 만든다
        /// </summary>
        private void HandleTalentHover(int index, bool entered)
        {
            if (entered)
            {
                _hoveredTalentIndex = index;
            }
            else if (_hoveredTalentIndex == index)
            {
                _hoveredTalentIndex = -1;
            }
            else
            {
                // 이미 다른 노드로 옮겨간 뒤 도착한 나감 통지다. 새 노드를 지우면 안 된다
                return;
            }

            RefreshTalentTooltip();
        }

        /// <summary> 지금 가리키는 노드의 설명을 다시 만든다. 가리키는 노드가 없으면 숨긴다 </summary>
        private void RefreshTalentTooltip()
        {
            if (_talentTooltip == null)
            {
                return;
            }

            if ((!_isVisible) || (_currentPage != PageKind.Talent) ||
                (_hoveredTalentIndex < 0) ||
                (_hoveredTalentIndex >= _visibleTalentNodeIds.Length) ||
                (_visibleTalentNodeIds[_hoveredTalentIndex] <= 0) ||
                (!_allocation.HasWeapon) || (!GameDB.IsInitialized) || (GameDB.Talent == null))
            {
                HideTalentTooltip();
                return;
            }

            TalentTreeSO tree = GameDB.Talent;
            if (!tree.TryGetNode(_visibleTalentNodeIds[_hoveredTalentIndex],
                    out TalentTreeSO.NodeEntry node))
            {
                HideTalentTooltip();
                return;
            }

            // RefreshTalentView와 같은 총 포인트를 넘겨야 상태 판정이 버튼과 어긋나지 않는다
            int available = (_talents == null) ? 0 : _talents.AvailablePoints;
            _talentTooltipText.text = FunctionalUiText.GetTalentNodeName(node) + "\n" +
                FunctionalUiText.BuildTalentTooltip(tree, _allocation,
                    available + _allocation.SpentPoints, node);

            // 연결선 GO가 나중에 생기면 설명이 그 아래로 내려간다. 띄울 때마다 맨 위로 올린다
            _talentTooltipRect.SetAsLastSibling();
            _talentTooltip.SetActive(true);
            PlaceTalentTooltip((RectTransform)_talentButtons[_hoveredTalentIndex].transform);
        }

        private void HideTalentTooltip()
        {
            if ((_talentTooltip != null) && _talentTooltip.activeSelf)
            {
                _talentTooltip.SetActive(false);
            }
        }

        /// <summary>
        /// 노드 오른쪽에 두되 창을 넘어가면 왼쪽으로 접고, 그래도 넘치면 창 안으로 당긴다
        /// </summary>
        private void PlaceTalentTooltip(RectTransform anchor)
        {
            RectTransform parent = _talentTooltipRect.parent as RectTransform;
            if ((parent == null) || (anchor == null))
            {
                return;
            }

            // anchoredPosition은 앵커 기준이라 부모 Rect와 비교하려면 앵커 위치를 더해야 한다
            Rect bounds = parent.rect;

            float padding = TALENT_TOOLTIP_PADDING;
            float width = Mathf.Min(TALENT_TOOLTIP_WIDTH,
                Mathf.Max(1f, bounds.width));

            // 앞선 노드에서 켜 둔 자동 축소가 남아 있으면 이번 높이를 잘못 잰다. 늘 같은
            // 글자 크기로 재고 나서 넘칠 때만 줄인다
            float fontSize = TALENT_TOOLTIP_FONT_SIZE;
            _talentTooltipText.enableAutoSizing = false;
            _talentTooltipText.fontSize = fontSize;

            // 접은 높이로 재야 상자가 글 길이를 따라간다
            _talentTooltipText.textWrappingMode = TextWrappingModes.Normal;

            float height = _talentTooltipText.GetPreferredValues(
                _talentTooltipText.text, width - (padding * 2f), 0f).y + (padding * 2f);

            // 글이 길어 부모보다 커지면 Clamp의 min이 max를 넘어 경계 밖으로 나간다.
            // 상자를 창 안으로 줄이고 그만큼 글자를 작게 맞춘다
            float maxHeight = Mathf.Max(1f, bounds.height);
            if (height > maxHeight)
            {
                height = maxHeight;
                _talentTooltipText.fontSizeMin = Mathf.Min(TALENT_TOOLTIP_MIN_FONT_SIZE, fontSize);
                _talentTooltipText.fontSizeMax = fontSize;
                _talentTooltipText.enableAutoSizing = true;
            }

            _talentTooltipRect.sizeDelta = new Vector2(width, height);
            Vector2 anchorOrigin = new Vector2(
                (anchor.anchorMin.x - parent.pivot.x) * bounds.width,
                (anchor.anchorMin.y - parent.pivot.y) * bounds.height);
            Vector2 center = anchorOrigin + anchor.anchoredPosition;

            float offset = (anchor.sizeDelta.x * 0.5f) + (width * 0.5f) + TALENT_TOOLTIP_GAP;
            float x = center.x + offset;
            if ((x + (width * 0.5f)) > bounds.xMax)
            {
                x = center.x - offset;
            }

            x = Mathf.Clamp(x, bounds.xMin + (width * 0.5f), bounds.xMax - (width * 0.5f));
            float y = Mathf.Clamp(center.y, bounds.yMin + (height * 0.5f),
                bounds.yMax - (height * 0.5f));

            _talentTooltipRect.anchoredPosition = new Vector2(x, y) - anchorOrigin;
        }

        /// <summary>
        /// 그려진 노드마다 선행 노드와 선을 잇는다. 선행이 없는 뿌리는 가운데 그믐에 잇는다
        /// 내 가지에서 이미 고른 노드에서 나가는 선만 밝힌다
        /// </summary>
        private void RefreshTalentEdges(TalentTreeSO tree, int localArm)
        {
            int used = 0;
            for (int i = 0; i < _drawnTalentNodeIds.Length; ++i)
            {
                if (!tree.TryGetNode(_drawnTalentNodeIds[i], out TalentTreeSO.NodeEntry node))
                {
                    continue;
                }

                int arm = i / TALENT_SLOTS_PER_ARM;
                bool mine = arm == localArm;
                Vector2 end = TalentPosition(tree, arm, node.WeaponKind, node.ColumnIndex, node.RowIndex);

                bool hasParent = false;
                foreach (int parent in tree.EnumeratePrerequisites(node))
                {
                    if (!tree.TryGetNode(parent, out TalentTreeSO.NodeEntry prerequisite))
                    {
                        continue;
                    }

                    hasParent = true;
                    Vector2 start = TalentPosition(tree, arm, prerequisite.WeaponKind,
                        prerequisite.ColumnIndex, prerequisite.RowIndex);
                    PlaceTalentEdge(used++, start, end, !mine ? G_TalentLineOther :
                        _allocation.Contains(parent) ? G_TalentLineLit : G_TalentLine);
                }

                if (!hasParent)
                {
                    PlaceTalentEdge(used++, _talentConstellation.Center, end, !mine ? G_TalentLineOther :
                        _allocation.Contains(node.NodeId) ? G_TalentLineLit : G_TalentLine);
                }
            }

            for (int i = used; i < _talentEdges.Count; ++i) { _talentEdges[i].gameObject.SetActive(false); }
        }

        /// <summary>
        /// 선 하나를 두 점 사이에 눕힌다. 모자라면 새로 만든다
        /// 그믐 바로 아래에 끼운다 - 맨 앞에 두면 별하늘 층에 가려진다
        /// </summary>
        private void PlaceTalentEdge(int index, Vector2 start, Vector2 end, Color color)
        {
            if (index == _talentEdges.Count)
            {
                var go = new GameObject("TalentConnection", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(_talentButtons[0].transform.parent, false);
                if ((_talentHub != null) && (_talentHub.parent == go.transform.parent))
                {
                    go.transform.SetSiblingIndex(_talentHub.GetSiblingIndex());
                }
                else
                {
                    go.transform.SetAsFirstSibling();
                }
                var image = go.GetComponent<Image>();
                image.raycastTarget = false;
                _talentEdges.Add(image);
            }

            Image line = _talentEdges[index];
            if (!line.gameObject.activeSelf)
            {
                line.gameObject.SetActive(true);
            }

            RectTransform rect = line.rectTransform;
            var template = (RectTransform)_talentButtons[0].transform;
            rect.anchorMin = template.anchorMin;
            rect.anchorMax = template.anchorMax;
            rect.anchoredPosition = (start + end) * 0.5f;
            rect.sizeDelta = new Vector2(Vector2.Distance(start, end), _talentConstellation.LineWidth);
            rect.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg);
            line.color = color;
        }

        private void OnEnable()
        {
            // 같은 씬에 꺼져 있는 사본이 또 있을 수 있다. Awake에서 잡으면 꺼진 쪽이 Instance를 차지한다
            if (!G_Live.Contains(this))
            {
                G_Live.Add(this);
            }

            Instance = this;

            if (_currency != null)
            {
                _currency.BalanceChanged += HandleBalanceChanged;
            }

            if (_shopTransactions != null)
            {
                _shopTransactions.TransactionCompleted += HandleShopTransaction;
            }

            if (_transferService != null)
            {
                _transferService.TransferChanged += HandleTransferChanged;
            }

            RefreshLocalBinding();
        }

        private void OnDisable()
        {
            ReleaseRegistration();
            UnbindLocalPlayer();
            UnbindShopPoints();

            if (_currency != null)
            {
                _currency.BalanceChanged -= HandleBalanceChanged;
            }

            if (_shopTransactions != null)
            {
                _shopTransactions.TransactionCompleted -= HandleShopTransaction;
            }

            if (_transferService != null)
            {
                _transferService.TransferChanged -= HandleTransferChanged;
            }

            SetVisible(false, true);
        }

        private void OnDestroy()
        {
            ReleaseInputToken();
            ReleaseRegistration();
        }

        /// <summary> 쥐고 있던 쪽이 빠지면 아직 살아 있는 사본에게 넘긴다 </summary>
        private void ReleaseRegistration()
        {
            G_Live.Remove(this);
            if (Instance != this)
            {
                return;
            }

            Instance = (G_Live.Count > 0) ? G_Live[G_Live.Count - 1] : null;
        }

        private void Update()
        {
            // 닫히는 연출은 _isVisible이 이미 false인 상태로 진행되므로 아래 조기 반환보다 앞에 있어야 한다
            AdvanceRevealAnimation();

            RefreshLocalBinding();
            RefreshObservedContext();

            if (Time.unscaledTime >= _nextNetworkScanTime)
            {
                _nextNetworkScanTime = Time.unscaledTime + NETWORK_SCAN_INTERVAL;
                RefreshNetworkCache();
            }

            if (Input.GetKeyDown(KeyCode.I))
            {
                Toggle();
            }

            if (!_isVisible)
            {
                return;
            }

            // ESC는 EscapeMenuPanel이 단독으로 읽는다. 여기서도 읽으면 한 번 누를 때 두 창이 함께 반응한다
            LocalGameplayInputGate.Maintain(_inputToken);

            if (Time.unscaledTime >= _nextSnapshotPollTime)
            {
                _nextSnapshotPollTime = Time.unscaledTime + SNAPSHOT_POLL_INTERVAL;
                RefreshAllViews();
            }

            RefreshTransferCountdown();
        }

        public void Toggle()
        {
            if (_isVisible)
            {
                Close();
            }
            else
            {
                OpenInventory();
            }
        }

        public void OpenInventory()
        {
            Open(PageKind.Inventory);
        }

        public void OpenTalents()
        {
            Open(PageKind.Talent);
        }

        public void OpenTransfer()
        {
            Open(PageKind.Transfer);
        }

        /// <summary>
        /// 곁에 선 파티원을 받을 사람으로 못 박고 배낭을 연다
        ///
        /// 받는 사람을 고르는 단계가 없다는 점만 다르고, 화면과 슬라이드 연출은 평소 배낭 그대로다
        /// 서버에 보내는 것은 기존 EquipmentTransferService.RequestOffer 하나뿐이며,
        /// 확인을 눌러도 제안이 갈 뿐이고 상대가 수락해야 장비가 넘어간다
        /// </summary>
        public void OpenTransferTo(uint recipientNetId, string recipientName)
        {
            if (recipientNetId == 0u)
            {
                return;
            }

            _quickRecipientNetId = recipientNetId;
            _quickRecipientName  = string.IsNullOrEmpty(recipientName) ? "참가자" : recipientName;
            _quickBoxSlot        = -1;
            CancelQuickDrag();

            Open(PageKind.Inventory);
            SetText(_inventoryStatusText, "줄 장비를 위 상자로 끌어다 놓으세요.");
            RefreshInventoryView();
        }

        /// <summary> 양도 모드인가. 평소 배낭과 화면 구성이 달라진다 </summary>
        private bool IsQuickTransferMode => (_quickRecipientNetId != 0u);

        /// <summary> 아직 답을 받지 못한 제안이 떠 있는가. 서버는 한 번에 하나만 받는다 </summary>
        private bool HasPendingOffer => (_activeOffer.State == EquipmentTransferState.Offered);

        private void ExitQuickTransferMode()
        {
            // 평소 배낭을 닫는 것까지 건드리지 않는다
            if (IsQuickTransferMode)
            {
                WithdrawQuickOffer();
            }

            _quickRecipientNetId = 0u;
            _quickRecipientName  = string.Empty;
            _quickBoxSlot        = -1;
            CancelQuickDrag();
        }

        /// <summary>
        /// 내가 보낸 제안을 거두어들인다
        ///
        /// 이 창은 양도 한 건이 곧 창 하나다. 닫았는데 제안이 30초 동안 남아 있으면
        /// 보낸 사람은 그런 줄 모르고, 그 동안 다른 사람에게는 줄 수도 없다
        ///
        /// 상대가 이미 수락했으면 State가 Offered가 아니므로 아무것도 하지 않는다
        /// </summary>
        private void WithdrawQuickOffer()
        {
            if ((_transferService == null) || (!CanLocalCancelOffer()))
            {
                return;
            }

            _transferService.RequestCancel(_activeOffer.OfferId);
        }

        public void OpenShop(uint shopNetId)
        {
            if (!_shopPoints.TryGetValue(shopNetId, out ShopInteractionPoint shopPoint) ||
                (shopPoint == null))
            {
                SetText(_shopStatusText, "객주 정보를 확인하고 있습니다.");
                return;
            }

            _activeShopPoint = shopPoint;
            Open(PageKind.Shop);
        }

        public void Close()
        {
            ExitQuickTransferMode();

            // 모드를 끈 상태를 화면에도 반영한다. 안 하면 다음에 열 때 옛 상자가 한 프레임 비친다
            RefreshQuickTransferView();
            SetVisible(false);
        }

        /// <summary>
        /// 분류 버튼을 눌렀다. 같은 것을 다시 누르면 풀린다
        ///
        /// 칸을 숨기지 않는다. 12칸 자리가 그대로 있어야 손에 익은 위치가 흔들리지 않는다
        /// </summary>
        public void SelectInventoryFilter(int filterIndex)
        {
            InventoryFilter wanted = ToInventoryFilter(filterIndex);
            _inventoryFilter = (_inventoryFilter == wanted) ? InventoryFilter.None : wanted;
            RefreshInventoryView();
        }

        private static InventoryFilter ToInventoryFilter(int filterIndex)
        {
            switch (filterIndex)
            {
                case 0:  return InventoryFilter.Weapon;
                case 1:  return InventoryFilter.Potion;
                case 2:  return InventoryFilter.Ticket;
                default: return InventoryFilter.None;
            }
        }

        /// <summary> 이 칸이 지금 고른 분류에 들어가는가 </summary>
        private bool MatchesInventoryFilter(in InventorySlotSnapshot slot)
        {
            switch (_inventoryFilter)
            {
                case InventoryFilter.Weapon:
                    return (slot.Kind == InventoryItemKind.Equipment);
                case InventoryFilter.Potion:
                    return (slot.Kind == InventoryItemKind.Consumable) && IsPotion(slot.ItemId);
                case InventoryFilter.Ticket:
                    return (slot.Kind == InventoryItemKind.Consumable) && IsTicket(slot.ItemId);
                default:
                    return false;
            }
        }

        public void SelectInventorySlot(int slotIndex)
        {
            if ((slotIndex < 0) || (slotIndex >= _slots.Length))
            {
                return;
            }

            // 같은 칸을 다시 누르면 실행한다. 첫 클릭까지 실행하면 물약이 오클릭으로 사라지고
            // 양도할 장비를 고르는 순간 장착돼 버린다
            bool isRepeat = (_selectedSlot == slotIndex) && !_slots[slotIndex].IsEmpty;

            _selectedSlot = slotIndex;
            InventorySlotSnapshot slot = _slots[slotIndex];
            if (slot.Kind == InventoryItemKind.Equipment)
            {
                _selectedEquipmentSlot = slotIndex;
                _targetEquipped = false;
            }
            else if ((slot.Kind == InventoryItemKind.Consumable) &&
                     IsTicket(slot.ItemId))
            {
                _selectedTicketSlot = slotIndex;
            }

            RefreshInventoryView();

            if (isRepeat)
            {
                RequestSelectedPrimaryAction();
            }
        }

        public void RequestSelectedPrimaryAction()
        {
            if ((_inventory == null) || (_selectedSlot < 0) ||
                (_selectedSlot >= _slots.Length))
            {
                SetText(_inventoryStatusText, "먼저 배낭 칸을 선택해 주세요.");
                return;
            }

            InventorySlotSnapshot slot = _slots[_selectedSlot];
            if (slot.Kind == InventoryItemKind.Equipment)
            {
                _inventory.RequestEquip(_selectedSlot, slot.Equipment.InstanceId,
                    _inventoryRevision);
                SetText(_inventoryStatusText, "장비 변경을 요청했습니다.");
                return;
            }

            if ((slot.Kind == InventoryItemKind.Consumable) && IsPotion(slot.ItemId))
            {
                RequestUseSelectedPotion();
                return;
            }

            SetText(_inventoryStatusText, "이 물품은 대상 장비와 함께 사용해 주세요.");
        }

        public void RequestUnequip()
        {
            if (_inventory == null)
            {
                SetText(_inventoryStatusText, "배낭을 아직 불러오지 못했습니다.");
                return;
            }

            _inventory.RequestUnequip(_inventoryRevision);
            SetText(_inventoryStatusText, "장비 해제를 요청했습니다.");
        }

        public void RequestUseSelectedPotion()
        {
            if ((_shopTransactions == null) || !TryGetSelectedConsumable(
                    out InventorySlotSnapshot slot) || !IsPotion(slot.ItemId))
            {
                SetText(_inventoryStatusText, "사용할 물약을 선택해 주세요.");
                return;
            }

            uint requestId = _shopTransactions.RequestUsePotion(_selectedSlot,
                slot.ItemId, _inventoryRevision);
            SetText(_inventoryStatusText, requestId == 0u
                ? "지금은 물약을 사용할 수 없습니다."
                : "물약 사용을 요청했습니다.");
        }

        public void SelectEquippedTicketTarget()
        {
            if ((_inventory == null) || (!_inventory.EquippedWeapon.HasEquipment))
            {
                SetText(_inventoryStatusText, "현재 장착 장비는 티켓 대상이 아닙니다.");
                return;
            }

            _targetEquipped = true;
            RefreshInventoryView();
        }

        public void RequestUseSelectedTicket()
        {
            if ((_shopTransactions == null) || (_selectedTicketSlot < 0) ||
                (_selectedTicketSlot >= _slots.Length))
            {
                SetText(_inventoryStatusText, "사용할 티켓을 선택해 주세요.");
                return;
            }

            InventorySlotSnapshot ticket = _slots[_selectedTicketSlot];
            if ((ticket.Kind != InventoryItemKind.Consumable) || !IsTicket(ticket.ItemId))
            {
                SetText(_inventoryStatusText, "선택한 칸에 사용할 티켓이 없습니다.");
                return;
            }

            uint requestId;
            if (_targetEquipped)
            {
                WeaponCombatSnapshot equipped = _inventory.EquippedWeapon;
                requestId = (ticket.ItemId == ShopItemKind.RerollTicket)
                    ? _shopTransactions.RequestRerollEquippedTicket(_selectedTicketSlot,
                        equipped.InstanceId, _inventoryRevision)
                    : _shopTransactions.RequestUpgradeEquippedTicket(_selectedTicketSlot,
                        equipped.InstanceId, _inventoryRevision);
            }
            else if ((_selectedEquipmentSlot >= 0) &&
                     (_selectedEquipmentSlot < _slots.Length) &&
                     (_slots[_selectedEquipmentSlot].Kind == InventoryItemKind.Equipment))
            {
                WeaponCombatSnapshot equipment =
                    _slots[_selectedEquipmentSlot].Equipment;
                requestId = (ticket.ItemId == ShopItemKind.RerollTicket)
                    ? _shopTransactions.RequestRerollTicket(_selectedTicketSlot,
                        _selectedEquipmentSlot, equipment.InstanceId, _inventoryRevision)
                    : _shopTransactions.RequestUpgradeTicket(_selectedTicketSlot,
                        _selectedEquipmentSlot, equipment.InstanceId, _inventoryRevision);
            }
            else
            {
                SetText(_inventoryStatusText, "티켓을 적용할 장비를 선택해 주세요.");
                return;
            }

            SetText(_inventoryStatusText, requestId == 0u
                ? "지금은 티켓을 사용할 수 없습니다."
                : "티켓 사용을 요청했습니다.");
        }

        public void RequestAllocateTalent(int visibleIndex)
        {
            if ((_talents == null) || (visibleIndex < 0) ||
                (visibleIndex >= _visibleTalentNodeIds.Length) ||
                (_visibleTalentNodeIds[visibleIndex] <= 0))
            {
                SetText(_talentStatusText, "선택할 특성을 확인해 주세요.");
                return;
            }

            _talents.RequestAllocate(_visibleTalentNodeIds[visibleIndex]);
            SetText(_talentStatusText, "특성 선택을 요청했습니다.");
        }

        public void RequestResetTalents()
        {
            if (_talents == null)
            {
                SetText(_talentStatusText, "특성 정보를 아직 불러오지 못했습니다.");
                return;
            }

            _talents.RequestReset();
            SetText(_talentStatusText, "특성 초기화를 요청했습니다.");
        }

        public void RequestPurchase(int itemIndex)
        {
            if ((_shopTransactions == null) || (_currency == null) ||
                (_inventory == null) || (_activeShopPoint == null) ||
                (!_activeShopPoint.IsOpen) || (itemIndex < 0) ||
                (itemIndex >= G_ShopItems.Length))
            {
                SetText(_shopStatusText, "객주 가까이에서 거래를 열어 주세요.");
                return;
            }

            uint requestId = _shopTransactions.RequestPurchase(_activeShopPoint.netId,
                G_ShopItems[itemIndex], _currency.Revision, _inventoryRevision);
            SetText(_shopStatusText, requestId == 0u
                ? "지금은 구매 요청을 보낼 수 없습니다."
                : "구매를 요청했습니다.");
        }

        public void RequestOfferTransfer(int recipientIndex)
        {
            if ((_transferService == null) || (_selectedEquipmentSlot < 0) ||
                (_selectedEquipmentSlot >= _slots.Length) ||
                (recipientIndex < 0) || (recipientIndex >= _visibleRecipients.Count))
            {
                SetText(_transferStatusText, "장비와 받을 참가자를 선택해 주세요.");
                return;
            }

            InventorySlotSnapshot slot = _slots[_selectedEquipmentSlot];
            if (slot.Kind != InventoryItemKind.Equipment)
            {
                SetText(_transferStatusText, "배낭에 있는 장비만 양도할 수 있습니다.");
                return;
            }

            RecipientEntry recipient = _visibleRecipients[recipientIndex];
            uint requestId = _transferService.RequestOffer(recipient.NetId,
                _selectedEquipmentSlot, slot.Equipment.InstanceId, _inventoryRevision);
            SetText(_transferStatusText, requestId == 0u
                ? "지금은 양도 제안을 보낼 수 없습니다."
                : $"{recipient.DisplayName}님에게 양도를 제안했습니다.");
        }

        /****************************************
        *           Quick Transfer Drag
        ****************************************/

        private void HandleInventoryDragBegan(int slotIndex, PointerEventData eventData)
        {
            CancelQuickDrag();

            if ((!IsQuickTransferMode) || (slotIndex < 0) || (slotIndex >= _slots.Length))
            {
                return;
            }

            // 서버는 배낭에 있는 장비만 받는다. 물약이나 권을 끌 수 있게 두면 헛손질만 늘어난다
            if (_slots[slotIndex].Kind != InventoryItemKind.Equipment)
            {
                SetText(_inventoryStatusText, "장비만 양도할 수 있습니다.");
                return;
            }

            _quickDragSlot = slotIndex;
            SetText(_quickTransferDragGhostLabel,
                FunctionalUiText.DescribeWeapon(_slots[slotIndex].Equipment));
            SetActive(_quickTransferDragGhost, true);
            HandleInventoryDragged(slotIndex, eventData);
        }

        private void HandleInventoryDragged(int slotIndex, PointerEventData eventData)
        {
            if ((_quickDragSlot < 0) || (_quickTransferDragGhost == null) || (eventData == null))
            {
                return;
            }

            RectTransform parent = _quickTransferDragGhost.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,
                    eventData.position, eventData.pressEventCamera, out local))
            {
                _quickTransferDragGhost.anchoredPosition = local;
            }
        }

        private void HandleInventoryDragEnded(int slotIndex, PointerEventData eventData)
        {
            int dropped = _quickDragSlot;
            CancelQuickDrag();

            if ((dropped < 0) || (_quickTransferBox == null) || (eventData == null))
            {
                return;
            }

            // IDropHandler 대신 직접 본다. 커서를 따라다니는 표시가 레이캐스트를 가리는 사고를 피한다
            if (!RectTransformUtility.RectangleContainsScreenPoint(_quickTransferBox,
                    eventData.position, eventData.pressEventCamera))
            {
                return;
            }

            _quickBoxSlot = dropped;
            SetText(_inventoryStatusText,
                "확인을 누르면 " + _quickRecipientName + "님에게 제안이 갑니다.");
            RefreshInventoryView();
        }

        private void CancelQuickDrag()
        {
            _quickDragSlot = -1;
            SetActive(_quickTransferDragGhost, false);
        }

        /// <summary> 상자에 올려 둔 장비를 곁에 선 파티원에게 제안한다 </summary>
        public void RequestQuickTransfer()
        {
            if ((_transferService == null) || (!IsQuickTransferMode) ||
                (_quickBoxSlot < 0) || (_quickBoxSlot >= _slots.Length))
            {
                SetText(_inventoryStatusText, "줄 장비를 먼저 상자에 올려 주세요.");
                return;
            }

            InventorySlotSnapshot slot = _slots[_quickBoxSlot];
            if (slot.Kind != InventoryItemKind.Equipment)
            {
                _quickBoxSlot = -1;
                SetText(_inventoryStatusText, "장비만 양도할 수 있습니다.");
                RefreshInventoryView();
                return;
            }

            if (HasPendingOffer)
            {
                SetText(_inventoryStatusText, "이미 진행 중인 양도 제안이 있습니다.");
                return;
            }

            uint requestId = _transferService.RequestOffer(_quickRecipientNetId, _quickBoxSlot,
                slot.Equipment.InstanceId, _inventoryRevision);

            // 0은 보내지 못했다는 뜻이고, 양수라도 서버가 받아들였다는 뜻은 아니다
            SetText(_inventoryStatusText, (requestId == 0u)
                ? "지금은 양도 제안을 보낼 수 없습니다."
                : _quickRecipientName + "님에게 양도를 제안했습니다. 수락을 기다립니다.");
        }

        private void UpdateQuickTransferStatus(in EquipmentTransferResult result)
        {
            if (!result.Succeeded)
            {
                SetText(_inventoryStatusText, FunctionalUiText.GetTransactionRejection(result.Rejection));
                return;
            }

            // 제안이 떴을 뿐이고 장비는 아직 넘어가지 않았다. 상자는 그대로 둔다
            if (result.Offer.State == EquipmentTransferState.Offered)
            {
                SetText(_inventoryStatusText,
                    _quickRecipientName + "님에게 제안이 도착했습니다. 수락을 기다립니다.");
                return;
            }

            SetText(_inventoryStatusText, GetTransferCompletionText(result.Offer.State));
            _quickBoxSlot = -1;
            RefreshQuickTransferView();
        }

        private void RefreshQuickTransferView()
        {
            // 상자는 배낭을 보고 있을 때만 띄운다. 특성이나 객주로 넘어가면 끌 곳이 없다
            bool mode = IsQuickTransferMode && (_currentPage == PageKind.Inventory);

            SetActive(_quickTransferBox, mode);
            SetActive(_quickTransferConfirmButton, mode);
            SetActive(_quickTransferCancelButton, mode);
            SetActive(_quickTransferTitleText, mode);

            if (!mode)
            {
                CancelQuickDrag();
                return;
            }

            bool hasItem = (_quickBoxSlot >= 0) && (_quickBoxSlot < _slots.Length) &&
                (_slots[_quickBoxSlot].Kind == InventoryItemKind.Equipment);

            SetText(_quickTransferTitleText, _quickRecipientName + "에게 양도");
            SetText(_quickTransferBoxLabel, hasItem
                ? FunctionalUiText.DescribeWeapon(_slots[_quickBoxSlot].Equipment)
                : "줄 장비를 여기로 끌어다 놓기");

            // 이미 떠 있는 제안이 있으면 또 보내지 않는다. 서버는 OfferConflict로 돌려보내며,
            // 양도 페이지의 받는 사람 버튼도 같은 조건을 본다(RefreshRecipientView)
            SetInteractable(_quickTransferConfirmButton, hasItem && !HasPendingOffer);
        }

        private static void SetActive(Component target, bool active)
        {
            if ((target != null) && (target.gameObject.activeSelf != active))
            {
                target.gameObject.SetActive(active);
            }
        }

        public void RequestAcceptTransfer()
        {
            if ((_transferService == null) || (_inventory == null) ||
                !CanLocalAcceptOffer())
            {
                SetText(_transferStatusText, "수락할 양도 제안이 없습니다.");
                return;
            }

            _transferService.RequestAccept(_activeOffer.OfferId, _inventoryRevision);
            SetText(_transferStatusText, "양도 수락을 요청했습니다.");
        }

        public void RequestRejectTransfer()
        {
            if ((_transferService == null) || !CanLocalAcceptOffer())
            {
                SetText(_transferStatusText, "거절할 양도 제안이 없습니다.");
                return;
            }

            _transferService.RequestReject(_activeOffer.OfferId);
            SetText(_transferStatusText, "양도 제안을 거절했습니다.");
        }

        public void RequestCancelTransfer()
        {
            if ((_transferService == null) || !CanLocalCancelOffer())
            {
                SetText(_transferStatusText, "취소할 양도 제안이 없습니다.");
                return;
            }

            _transferService.RequestCancel(_activeOffer.OfferId);
            SetText(_transferStatusText, "양도 제안을 취소했습니다.");
        }

        private void BindButtons()
        {
            BindButton(_closeButton, Close);
            BindButton(_shopTabButton, () => Open(PageKind.Shop));
            BindButton(_equipButton, RequestSelectedPrimaryAction);
            BindButton(_unequipButton, RequestUnequip);
            BindButton(_usePotionButton, RequestUseSelectedPotion);
            BindButton(_useTicketButton, RequestUseSelectedTicket);
            BindButton(_targetEquippedButton, SelectEquippedTicketTarget);
            BindButton(_openTransferButton, OpenTransfer);
            BindButton(_talentResetButton, RequestResetTalents);
            BindButton(_acceptTransferButton, RequestAcceptTransfer);
            BindButton(_rejectTransferButton, RequestRejectTransfer);
            BindButton(_cancelTransferButton, RequestCancelTransfer);

            BindButton(_quickTransferConfirmButton, RequestQuickTransfer);
            BindButton(_quickTransferCancelButton, Close);

            BindIndexedButtons(_inventoryFilterButtons, SelectInventoryFilter);
            BindIndexedButtons(_inventoryButtons, SelectInventorySlot);

            // 배낭 칸은 클릭으로 장착하고, 끌면 양도 상자로 간다. 두 경로가 겹치지 않는다
            for (int i = 0; i < ArrayLength(_inventoryButtons); ++i)
            {
                if (_inventoryButtons[i] == null)
                {
                    continue;
                }

                _inventoryButtons[i].DragBegan += HandleInventoryDragBegan;
                _inventoryButtons[i].Dragged   += HandleInventoryDragged;
                _inventoryButtons[i].DragEnded += HandleInventoryDragEnded;
            }
            BindIndexedButtons(_talentButtons, RequestAllocateTalent);

            // 상세 설명은 특성 노드에서만 띄운다. 잠긴 노드도 hover는 들어온다
            for (int i = 0; i < ArrayLength(_talentButtons); ++i)
            {
                if (_talentButtons[i] != null)
                {
                    _talentButtons[i].HoverChanged += HandleTalentHover;
                }
            }

            BindIndexedButtons(_shopButtons, RequestPurchase);
            BindIndexedButtons(_recipientButtons, RequestOfferTransfer);
        }

        private void Open(PageKind page)
        {
            RunManager run = RunManager.LocalInstance;
            if ((run != null) && (run.Phase == RunPhase.Result))
            {
                return;
            }

            RefreshLocalBinding();
            if (!_isVisible)
            {
                // ESC 메뉴가 입력을 쥐고 있으면 창이 열리지 못한다. 메뉴를 먼저 거둔다
                EscapeMenuPanel menu = EscapeMenuPanel.Instance;
                if ((menu != null) && menu.IsVisible)
                {
                    menu.Close();
                }

                if (!LocalGameplayInputGate.AllowsGameplayInput)
                {
                    return;
                }

                _inputToken = LocalGameplayInputGate.Acquire();
            }

            SetVisible(true);
            SetPage(page);
            RefreshAllViews();
        }

        private void SetVisible(bool visible)
        {
            SetVisible(visible, false);
        }

        /// <param name="instant">
        /// 연출 없이 즉시 맞춘다. 초기화(Awake)와 비활성화(OnDisable)는 화면에 보여 줄 프레임이
        /// 없으므로 애니메이션을 돌리면 다음에 켰을 때 중간 자세로 남는다
        /// </param>
        private void SetVisible(bool visible, bool instant)
        {
            bool changed = (_isVisible != visible);

            _isVisible = visible;

            // 입력 차단은 연출을 기다리지 않는다. 닫는 중인 창이 클릭을 먹으면 오작동한다
            if (_canvasGroup != null)
            {
                _canvasGroup.interactable = visible;
                _canvasGroup.blocksRaycasts = visible;
            }

            if (instant || (_slideDuration <= 0f))
            {
                _revealProgress = visible ? 1f : 0f;
                ApplyRevealProgress();
            }

            if (!visible)
            {
                _botInventoryView?.Close();
                ReleaseInputToken();
                _activeShopPoint = null;
                _hoveredTalentIndex = -1;
                HideTalentTooltip();
            }

            if (changed)
            {
                VisibilityChanged?.Invoke(visible);
            }
        }

        /// <summary>
        /// 슬라이드 기준 자리를 한 번만 잡아 둔다. 연출로 옮긴 뒤의 좌표를 기준으로 삼으면
        /// 열고 닫을 때마다 창이 조금씩 밀려난다
        /// </summary>
        private void CacheSlideOrigin()
        {
            if (_hasSlideOrigin || (_slideRoot == null))
            {
                return;
            }

            _slideOpenPosition = _slideRoot.anchoredPosition;
            _hasSlideOrigin    = true;
        }

        private void AdvanceRevealAnimation()
        {
            float target = _isVisible ? 1f : 0f;
            if (Mathf.Approximately(_revealProgress, target))
            {
                return;
            }

            // 관리 창은 시간이 멈춘 상태에서도 열릴 수 있어 unscaled를 쓴다
            float step = (_slideDuration > 0f) ? (Time.unscaledDeltaTime / _slideDuration) : 1f;

            _revealProgress = Mathf.MoveTowards(_revealProgress, target, step);
            ApplyRevealProgress();
        }

        private void ApplyRevealProgress()
        {
            // 시작과 끝을 함께 눅인다. 등속으로 밀면 멈추는 순간이 딱딱하게 걸린다
            float eased = Mathf.SmoothStep(0f, 1f, _revealProgress);

            // 카메라가 이 값을 읽어 측면으로 비킨다. 창이 화면을 덮는 만큼 캐릭터가 반대쪽으로 간다
            CameraFrame.MenuRevealProgress = eased;

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = eased;
            }

            if (_slideRoot == null)
            {
                return;
            }

            CacheSlideOrigin();

            // 닫힌 자리는 오른쪽 화면 밖이다. 거기서 제자리로 당겨 온다
            _slideRoot.anchoredPosition = _slideOpenPosition
                + (Vector2.right * (_slideDistance * (1f - eased)));
        }

        private void SetPage(PageKind page)
        {
            _currentPage = page;

            // 객주를 열면 물건은 화면 가운데에 따로 뜨고 오른쪽 판은 배낭 그대로다.
            // 산 물건이 칸에 들어오는 것과 남은 칸을 보면서 고르게 한다
            bool showsInventory = (page == PageKind.Inventory) || (page == PageKind.Shop);
            SetText(_pageTitle, showsInventory ? "배낭" :
                page == PageKind.Talent ? "특성" : "양도");
            SetActive(_inventoryPage, showsInventory);
            SetActive(_talentPage, page == PageKind.Talent);
            SetActive(_shopPage, page == PageKind.Shop);
            SetActive(_transferPage, page == PageKind.Transfer);

            // 특성 탭은 화면 전체가 별하늘이다. 우측 판이 남으면 별자리 한쪽을 가린다
            for (int i = 0; i < _hideOnTalentPage.Length; ++i)
            {
                SetActive(_hideOnTalentPage[i], page != PageKind.Talent);
            }

            if (page != PageKind.Talent)
            {
                // 탭을 옮기면 포인터 나감이 오지 않는 경로가 있어 직접 지운다
                _hoveredTalentIndex = -1;
                HideTalentTooltip();
            }
        }

        private void RefreshLocalBinding()
        {
            BindLocalPlayer(NetworkClient.localPlayer);
        }

        private void BindLocalPlayer(NetworkIdentity nextIdentity)
        {
            if (ReferenceEquals(nextIdentity, _localIdentity))
            {
                return;
            }

            Close();
            UnbindLocalPlayer();
            ClearSelections();
            _localIdentity = nextIdentity;
            if (_localIdentity == null)
            {
                Close();
                return;
            }

            _inventory = _localIdentity.GetComponent<InventoryController>();
            _talents = _localIdentity.GetComponent<TalentController>();
            if (_inventory != null)
            {
                _inventory.EquippedWeaponChanged += HandleEquippedWeaponChanged;
                _inventory.EquipResult += HandleEquipResult;
            }

            if (_talents != null)
            {
                _talents.RevisionChanged += HandleTalentRevisionChanged;
                _talents.AllocationResult += HandleTalentResult;
            }

            RefreshAllViews();
        }

        private void UnbindLocalPlayer()
        {
            if (_inventory != null)
            {
                _inventory.EquippedWeaponChanged -= HandleEquippedWeaponChanged;
                _inventory.EquipResult -= HandleEquipResult;
            }

            if (_talents != null)
            {
                _talents.RevisionChanged -= HandleTalentRevisionChanged;
                _talents.AllocationResult -= HandleTalentResult;
            }

            _localIdentity = null;
            _inventory = null;
            _talents = null;
            _inventoryRevision = 0u;
            _hasInventorySnapshot = false;
            _talentRevision = 0u;

            // 다른 플레이어의 트리로 바뀌므로 앞 플레이어의 설명을 남기지 않는다
            _hoveredTalentIndex = -1;
            HideTalentTooltip();
        }

        private void RefreshObservedContext()
        {
            uint runId = (_shopTransactions == null) ? 0u :
                _shopTransactions.ObservedRunId.Value;
            uint sceneRevision = (_shopTransactions == null) ? 0u :
                _shopTransactions.ObservedSceneRevision.Value;
            if ((runId == 0u) || (sceneRevision == 0u))
            {
                return;
            }

            if ((_observedRunId != 0u) &&
                ((_observedRunId != runId) ||
                 (_observedSceneRevision != sceneRevision)))
            {
                ClearSelections();
                Close();
            }

            _observedRunId = runId;
            _observedSceneRevision = sceneRevision;

            RunManager run = RunManager.LocalInstance;
            if ((run != null) && (run.Phase == RunPhase.Result))
            {
                Close();
            }
        }

        private void RefreshAllViews()
        {
            RefreshInventorySnapshot();
            RefreshTalentSnapshot();
            RefreshInventoryView();
            RefreshTalentView();
            RefreshShopView();
            RefreshRecipientView();
            RefreshQuickTransferView();
        }

        private void RefreshInventorySnapshot()
        {
            if ((_inventory == null) ||
                (!_inventory.TryCopyOwnerSlots(_slots, out uint revision)))
            {
                return;
            }

            _inventoryRevision = revision;
            _hasInventorySnapshot = true;
            if ((_selectedSlot >= 0) && _slots[_selectedSlot].IsEmpty)
            {
                _selectedSlot = -1;
            }

            if ((_selectedTicketSlot >= 0) &&
                ((_slots[_selectedTicketSlot].Kind != InventoryItemKind.Consumable) ||
                 !IsTicket(_slots[_selectedTicketSlot].ItemId)))
            {
                _selectedTicketSlot = -1;
            }

            if ((_selectedEquipmentSlot >= 0) &&
                (_slots[_selectedEquipmentSlot].Kind != InventoryItemKind.Equipment))
            {
                _selectedEquipmentSlot = -1;
            }
        }

        private void RefreshTalentSnapshot()
        {
            if ((_talents == null) ||
                (!_talents.TryGetOwnerAllocation(out TalentAllocationSnapshot allocation)))
            {
                return;
            }

            // Initial weapon publication can fill HasWeapon while intentionally
            // keeping revision 1, so revision alone cannot identify this snapshot
            _allocation = allocation;
            _talentRevision = allocation.Revision;
        }

        private void RefreshInventoryView()
        {
            SetText(_equippedWeaponText, (_inventory == null)
                ? "장착 장비를 확인하고 있습니다."
                : "현재 장비\n" + FunctionalUiText.DescribeWeapon(
                    _inventory.EquippedWeapon));

            for (int i = 0; i < ArrayLength(_inventoryButtons); ++i)
            {
                FunctionalUiIndexedButton button = _inventoryButtons[i];
                if (button == null)
                {
                    continue;
                }

                string prefix = (i == _selectedSlot) ? "선택 " : string.Empty;
                button.SetLabel(prefix + FunctionalUiText.DescribeSlot(_slots[i]));
                button.SetInteractable(_hasInventorySnapshot);
                button.SetHighlighted(MatchesInventoryFilter(_slots[i]));
            }

            if ((_selectedSlot < 0) || (_selectedSlot >= _slots.Length))
            {
                SetText(_inventoryDetailText,
                    "장비, 물약 또는 티켓을 선택해 주세요.");
            }
            else
            {
                InventorySlotSnapshot selected = _slots[_selectedSlot];
                string detail = FunctionalUiText.DescribeSlot(selected);
                if (selected.Kind == InventoryItemKind.Equipment)
                {
                    detail = FunctionalUiText.DescribeWeapon(selected.Equipment);
                }

                string target = _targetEquipped
                    ? "\n티켓 대상은 현재 장착 장비입니다."
                    : ((_selectedEquipmentSlot >= 0)
                        ? "\n티켓 대상 장비를 선택했습니다."
                        : string.Empty);
                SetText(_inventoryDetailText, detail + target);
            }

            bool selectingEquipment = HasSelectedKind(InventoryItemKind.Equipment);
            // 제작 UI는 장착/해제가 같은 자리를 쓴다. 비활성 버튼이 클릭을 가로막지 않게 한다
            if (_equipButton != null) { SetActive(_equipButton.gameObject, selectingEquipment); }
            if (_unequipButton != null) { SetActive(_unequipButton.gameObject, !selectingEquipment); }
            SetInteractable(_equipButton, selectingEquipment);
            SetInteractable(_unequipButton, (_inventory != null) &&
                _inventory.EquippedWeapon.HasEquipment);
            SetInteractable(_usePotionButton, SelectedConsumableMatches(true));
            SetInteractable(_useTicketButton, (_selectedTicketSlot >= 0) &&
                (_targetEquipped || (_selectedEquipmentSlot >= 0)));
            SetInteractable(_targetEquippedButton, (_inventory != null) &&
                _inventory.EquippedWeapon.HasEquipment);

            RefreshFilterButtons();
            RefreshQuickTransferView();
        }

        private void RefreshFilterButtons()
        {
            for (int i = 0; i < ArrayLength(_inventoryFilterButtons); ++i)
            {
                if (_inventoryFilterButtons[i] == null)
                {
                    continue;
                }

                // 누른 버튼도 같은 흰 테두리로 표시한다. 칸과 버튼이 같은 문법이면 눈이 따라가기 쉽다
                _inventoryFilterButtons[i].SetHighlighted(_inventoryFilter == ToInventoryFilter(i));
            }
        }

        private void RefreshTalentView()
        {
            int available = (_talents == null) ? 0 : _talents.AvailablePoints;
            SetText(_talentPointText, (_talentRevision == 0u)
                ? "특성 정보를 확인하고 있습니다."
                : $"사용 가능 포인트 {available}");

            // 이 갱신은 0.2초마다 돈다. 가리키던 노드가 그대로면 hover를 유지해야 하므로
            // 지우기 전에 어떤 노드를 보고 있었는지 남긴다
            int hoveredNodeBefore = ResolveHoveredTalentNodeId();

            Array.Clear(_visibleTalentNodeIds, 0, _visibleTalentNodeIds.Length);
            Array.Clear(_drawnTalentNodeIds, 0, _drawnTalentNodeIds.Length);

            if ((_talentRevision == 0u) || !_allocation.HasWeapon ||
                !GameDB.IsInitialized || (GameDB.Talent == null))
            {
                SetTalentButtonsUnavailable();
                ReleaseStaleTalentHover(hoveredNodeBefore);
                return;
            }

            // 무기마다 갈래 수와 갈래별 노드 수가 다르다. 격자를 통째로 도는 대신
            // 실제 정의된 열/노드만 그린다 - 없는 칸에 빈 별이 남으면 안 된다
            TalentTreeSO tree = GameDB.Talent;
            int localArm = ResolveTalentArms();
            int missing = 0;

            for (int arm = 0; arm < TALENT_ARM_COUNT; ++arm)
            {
                bool mine = (arm == localArm);

                // 내 가지는 특성 배분이 들고 있는 무기를 따른다. 버튼 판정이 같은 값을 쓰기 때문이다
                WeaponKind weapon = _allocation.Weapon;
                bool hasWeapon = mine || TryGetTalentArmWeapon(arm, out weapon);

                RefreshTalentArmLabel(arm, mine, hasWeapon, weapon);
                if (hasWeapon)
                {
                    missing += DrawTalentArm(tree, arm, weapon, mine, available);
                }
            }

            RefreshTalentColumnLabels(tree, localArm);

            // 이번에 쓰지 않은 칸만 뒤늦게 숨긴다. 쓰는 노드를 먼저 껐다 켜면 포인터가
            // 그대로인데도 hover가 끊겨 설명이 0.2초마다 사라진다
            int slotCount = Mathf.Min(ArrayLength(_talentButtons), _drawnTalentNodeIds.Length);
            for (int i = 0; i < slotCount; ++i)
            {
                if (_drawnTalentNodeIds[i] > 0)
                {
                    continue;
                }

                SetTalentButtonInteractable(_talentButtons[i], false);
                SetTalentButtonActive(_talentButtons[i], false);
            }

            if (missing > 0)
            {
                // 고르지 못하는 노드가 있는 화면을 조용히 넘기지 않는다
                Debug.LogError($"[FunctionalManagementPanel] 특성 칸이 {missing}개 모자라 " +
                    "일부 노드를 표시하지 못했다. 트리가 가지 한 칸 수(4열 x 5행)를 넘었다", this);
            }

            RefreshTalentEdges(tree, localArm);
            SetInteractable(_talentResetButton, _allocation.SpentPoints > 0);

            ReleaseStaleTalentHover(hoveredNodeBefore);

            // 포인트나 배분이 바뀌면 떠 있는 설명의 상태 줄도 같이 갱신해야 한다
            RefreshTalentTooltip();
        }

        /// <summary>
        /// 가지 하나를 그린다. 내 가지는 고를 수 있는 노드를 열고, 남의 가지는 모양만 보인다
        /// 칸이 모자라 그리지 못한 노드 수를 돌려준다
        /// </summary>
        private int DrawTalentArm(TalentTreeSO tree, int arm, WeaponKind weapon, bool mine,
            int available)
        {
            int missing = 0;
            int columnCount = tree.CountColumns(weapon);
            for (int column = 0; column < columnCount; ++column)
            {
                if (!tree.TryGetColumn(weapon, column, out TalentTreeSO.ColumnEntry columnEntry))
                {
                    continue;
                }

                for (int row = 0; row < columnEntry.NodeCount; ++row)
                {
                    if (!tree.TryGetNode(weapon, column, row, out TalentTreeSO.NodeEntry node))
                    {
                        continue;
                    }

                    int index = (arm * TALENT_SLOTS_PER_ARM) + (column * TALENT_ROWS_PER_COLUMN) + row;
                    if ((column >= TALENT_COLUMNS_PER_ARM) || (row >= TALENT_ROWS_PER_COLUMN) ||
                        (index >= ArrayLength(_talentButtons)))
                    {
                        ++missing;
                        continue;
                    }

                    FunctionalUiIndexedButton button = _talentButtons[index];
                    SetTalentButtonActive(button, true);
                    ((RectTransform)button.transform).anchoredPosition =
                        TalentPosition(tree, arm, weapon, column, row);
                    _drawnTalentNodeIds[index] = node.NodeId;

                    if (!mine)
                    {
                        // 남이 고른 노드는 아직 받지 않는다. 뿌리만 별로 두어 가지 모양을 알아보게 한다
                        bool root = column == 0;
                        SetTalentButtonInteractable(button, false);
                        ApplyTalentNodeLook(index, root ? _talentStarSprite : _talentDotSprite,
                            root ? _talentConstellation.OtherRootSize : _talentConstellation.OtherSize,
                            G_TalentOther);
                        continue;
                    }

                    _visibleTalentNodeIds[index] = node.NodeId;
                    bool allocated = _allocation.Contains(node.NodeId);
                    TalentAllocationResult allowed = TalentRules.CanAllocate(tree,
                        _allocation, available + _allocation.SpentPoints,
                        node.NodeId, out _);
                    bool open = (!allocated) && (allowed == TalentAllocationResult.Success);
                    SetTalentButtonInteractable(button, open);

                    if (allocated)
                    {
                        ApplyTalentNodeLook(index, _talentStarSprite,
                            _talentConstellation.AllocatedSize, G_TalentAllocated);
                    }
                    else if (open)
                    {
                        ApplyTalentNodeLook(index, _talentStarSprite,
                            _talentConstellation.OpenSize, G_TalentOpen);
                    }
                    else
                    {
                        ApplyTalentNodeLook(index, _talentDotSprite,
                            _talentConstellation.LockedSize, G_TalentLocked);
                    }
                }
            }

            return missing;
        }

        /// <summary>
        /// 파티원을 netId 순으로 세워 가지를 나눈다. 파티 HUD와 같은 순서라 세 화면이 같다
        /// 내가 목록에 없으면(네트워크 없이 연 창) 1P 자리를 내게 준다. 내 가지 번호를 돌려준다
        /// </summary>
        private int ResolveTalentArms()
        {
            _talentArmNetIds.Clear();
            foreach (uint netId in _partyWeapons.Keys)
            {
                _talentArmNetIds.Add(netId);
            }

            _talentArmNetIds.Sort();

            uint localNetId = (_localIdentity == null) ? 0u : _localIdentity.netId;
            int localArm = (localNetId == 0u) ? -1 : _talentArmNetIds.IndexOf(localNetId);
            if ((localArm < 0) || (localArm >= TALENT_ARM_COUNT))
            {
                _talentArmNetIds.Remove(localNetId);
                _talentArmNetIds.Insert(0, 0u);
                localArm = 0;
            }

            return localArm;
        }

        private bool TryGetTalentArmWeapon(int arm, out WeaponKind weapon)
        {
            weapon = default;
            if ((arm >= _talentArmNetIds.Count) ||
                !_partyWeapons.TryGetValue(_talentArmNetIds[arm], out PlayerWeapon player) ||
                (player == null) || !player.HasWeapon)
            {
                return false;
            }

            weapon = player.Weapon;
            return true;
        }

        private void RefreshTalentArmLabel(int arm, bool mine, bool hasWeapon, WeaponKind weapon)
        {
            if (arm >= _talentArmTexts.Length)
            {
                return;
            }

            TMP_Text label = _talentArmTexts[arm];
            label.rectTransform.anchoredPosition = _talentConstellation.ArmLabelPosition(arm);
            label.color = mine ? G_TalentArmLabel : G_TalentArmOther;

            string seat = $"{arm + 1}P";
            SetText(label, !hasWeapon ? $"{seat} 빈 자리" :
                mine ? $"{seat} {FunctionalUiText.GetWeaponRoleName(weapon)} (나)" :
                $"{seat} {FunctionalUiText.GetWeaponRoleName(weapon)}");
        }

        /// <summary>
        /// 갈래 이름은 내 가지에만 단다. 남의 가지까지 달면 별보다 글자가 많아진다
        /// 줄기(0열) 이름은 가지 주인 이름표와 자리가 겹쳐 달지 않는다 - 뿌리 설명에 나온다
        /// </summary>
        private void RefreshTalentColumnLabels(TalentTreeSO tree, int localArm)
        {
            WeaponKind weapon = _allocation.Weapon;
            int columnCount = tree.CountColumns(weapon);
            int trunkRows = tree.TryGetColumn(weapon, 0, out TalentTreeSO.ColumnEntry trunk)
                ? trunk.NodeCount : 1;

            for (int i = 0; i < ArrayLength(_talentColumnTexts); ++i)
            {
                TMP_Text label = _talentColumnTexts[i];
                TalentTreeSO.ColumnEntry entry = default;
                bool shown = (i > 0) && (i < columnCount) && tree.TryGetColumn(weapon, i, out entry);
                SetActive(label.gameObject, shown);
                if (!shown)
                {
                    continue;
                }

                SetText(label, string.IsNullOrWhiteSpace(entry.DisplayName)
                    ? $"{FunctionalUiText.GetWeaponName(weapon)} 특성" : entry.DisplayName);
                label.rectTransform.anchoredPosition = _talentConstellation.BranchLabelPosition(
                    localArm, i, entry.NodeCount, columnCount, trunkRows);
            }
        }

        /// <summary> 노드 상태를 별 모양, 크기, 색으로 보인다. 같은 값이면 건드리지 않는다 </summary>
        private void ApplyTalentNodeLook(int index, Sprite sprite, float size, Color color)
        {
            Image image = (index < _talentNodeImages.Length) ? _talentNodeImages[index] : null;
            if (image == null)
            {
                return;
            }

            if (image.sprite != sprite)
            {
                image.sprite = sprite;
            }

            image.color = color;
            image.rectTransform.sizeDelta = Vector2.one * size;
        }

        /// <summary> 지금 가리키는 칸이 들고 있는 노드 ID. 가리키는 칸이 없으면 0 </summary>
        private int ResolveHoveredTalentNodeId()
        {
            return ((_hoveredTalentIndex >= 0) &&
                (_hoveredTalentIndex < _visibleTalentNodeIds.Length))
                ? _visibleTalentNodeIds[_hoveredTalentIndex] : 0;
        }

        /// <summary>
        /// 무기나 트리가 바뀌어 가리키던 칸의 노드가 달라졌으면 hover를 놓는다
        /// 같은 노드가 그대로면 유지해야 갱신마다 설명이 깜빡이지 않는다
        /// </summary>
        private void ReleaseStaleTalentHover(int previousNodeId)
        {
            if ((_hoveredTalentIndex >= 0) && (ResolveHoveredTalentNodeId() != previousNodeId))
            {
                _hoveredTalentIndex = -1;
            }
        }

        /// <summary> 값이 같으면 건드리지 않는다. 껐다 켜면 그 칸의 포인터 상태가 끊긴다 </summary>
        private static void SetTalentButtonActive(FunctionalUiIndexedButton button, bool active)
        {
            if (button.gameObject.activeSelf != active)
            {
                button.gameObject.SetActive(active);
            }
        }

        /// <summary> 값이 같으면 건드리지 않는다. 매번 false로 되돌리면 눌린 표시가 튄다 </summary>
        private static void SetTalentButtonInteractable(FunctionalUiIndexedButton button,
            bool interactable)
        {
            if (button.Interactable != interactable)
            {
                button.SetInteractable(interactable);
            }
        }

        private void SetTalentButtonsUnavailable()
        {
            // 확인 중이라는 안내는 포인트 줄이 한다. 노드에는 글자를 두지 않는다
            // 노드와 선도 숨긴다. 아직 모양을 받지 못한 별 그림은 스프라이트 없는 100px 흰 판이라
            // 켜 둔 채면 프리팹 자리에 흰 기둥이 선다
            for (int i = 0; i < ArrayLength(_talentButtons); ++i)
            {
                _talentButtons[i].SetInteractable(false);
                SetTalentButtonActive(_talentButtons[i], false);
            }

            for (int i = 0; i < _talentEdges.Count; ++i)
            {
                SetActive(_talentEdges[i].gameObject, false);
            }

            SetInteractable(_talentResetButton, false);
            HideTalentTooltip();
        }

        private void RefreshShopView()
        {
            SetText(_walletText, (_currency == null)
                ? "노잣돈을 확인하고 있습니다."
                : $"공용 노잣돈 {_currency.Balance}냥");

            bool canPurchase = (_activeShopPoint != null) &&
                _activeShopPoint.IsOpen && _hasInventorySnapshot;
            for (int i = 0; i < ArrayLength(_shopButtons); ++i)
            {
                ShopItemKind kind = G_ShopItems[i];
                string label = FunctionalUiText.GetShopItemName(kind);
                if (GameDB.IsInitialized && (GameDB.Shop != null) &&
                    GameDB.Shop.TryGetItem(kind, out ShopSO.ItemEntry item))
                {
                    label += $"\n{item.Price}냥";
                }

                _shopButtons[i].SetLabel(label);
                _shopButtons[i].SetInteractable(canPurchase);
            }

            if (canPurchase)
            {
                SetText(_shopHintText, "객주 거래가 열렸습니다.");
                return;
            }

            float nearestDistance = FindNearestOpenShopDistance();
            if (nearestDistance < float.MaxValue)
            {
                SetText(_shopHintText,
                    $"객주까지 약 {nearestDistance:0.#}미터입니다. 가까이에서 상호작용해 주세요.");
            }
            else
            {
                SetText(_shopHintText, "지금 이용할 수 있는 객주를 찾지 못했습니다.");
            }
        }

        private void RefreshRecipientView()
        {
            _visibleRecipients.Clear();
            foreach (RecipientEntry entry in _recipients.Values)
            {
                if ((entry.Identity != null) &&
                    (_localIdentity != null) && (entry.Identity != _localIdentity))
                {
                    _visibleRecipients.Add(entry);
                }
            }

            _visibleRecipients.Sort((left, right) =>
                string.CompareOrdinal(left.DisplayName, right.DisplayName));
            int count = Mathf.Min(ArrayLength(_recipientButtons),
                _visibleRecipients.Count);
            for (int i = 0; i < ArrayLength(_recipientButtons); ++i)
            {
                bool hasRecipient = i < count;
                _recipientButtons[i].SetLabel(hasRecipient
                    ? $"{_visibleRecipients[i].DisplayName}에게 양도"
                    : "받을 참가자 없음");
                _recipientButtons[i].SetInteractable(hasRecipient &&
                    (_selectedEquipmentSlot >= 0) &&
                    (_activeOffer.State != EquipmentTransferState.Offered));
            }

            SetInteractable(_acceptTransferButton, CanLocalAcceptOffer());
            SetInteractable(_rejectTransferButton, CanLocalAcceptOffer());
            SetInteractable(_cancelTransferButton, CanLocalCancelOffer());
        }

        private void RefreshNetworkCache()
        {
            _staleKeys.Clear();
            foreach (KeyValuePair<uint, NetworkIdentity> pair in _scannedIdentities)
            {
                if (!NetworkClient.spawned.TryGetValue(pair.Key,
                        out NetworkIdentity current) || (current != pair.Value))
                {
                    _staleKeys.Add(pair.Key);
                }
            }

            for (int i = 0; i < _staleKeys.Count; ++i)
            {
                uint key = _staleKeys[i];
                if (_shopPoints.TryGetValue(key, out ShopInteractionPoint point) &&
                    (point != null))
                {
                    point.OpenRequested -= OpenShop;
                }

                _shopPoints.Remove(key);
                _recipients.Remove(key);
                _partyWeapons.Remove(key);
                _scannedIdentities.Remove(key);
            }

            foreach (KeyValuePair<uint, NetworkIdentity> pair in NetworkClient.spawned)
            {
                NetworkIdentity identity = pair.Value;
                if (identity == null)
                {
                    continue;
                }

                if (_scannedIdentities.TryGetValue(pair.Key,
                        out NetworkIdentity scanned) && (scanned == identity))
                {
                    continue;
                }

                _scannedIdentities[pair.Key] = identity;
                ShopInteractionPoint shopPoint =
                    identity.GetComponent<ShopInteractionPoint>();
                if (shopPoint != null)
                {
                    _shopPoints[pair.Key] = shopPoint;
                    shopPoint.OpenRequested += OpenShop;
                }

                PlayerWeapon weapon = identity.GetComponent<PlayerWeapon>();
                if (weapon != null)
                {
                    _partyWeapons[pair.Key] = weapon;
                }

                if (identity.GetComponent<InventoryController>() != null)
                {
                    PlayerNameTag nameTag = identity.GetComponent<PlayerNameTag>();
                    string displayName = (nameTag == null) ||
                        string.IsNullOrWhiteSpace(nameTag.DisplayName)
                        ? "동료" : nameTag.DisplayName;
                    _recipients.Add(pair.Key,
                        new RecipientEntry(pair.Key, identity, displayName));
                }
            }

            RefreshRecipientView();
        }

        private void UnbindShopPoints()
        {
            foreach (ShopInteractionPoint point in _shopPoints.Values)
            {
                if (point != null)
                {
                    point.OpenRequested -= OpenShop;
                }
            }

            _shopPoints.Clear();
            _recipients.Clear();
            _partyWeapons.Clear();
            _scannedIdentities.Clear();
        }

        private float FindNearestOpenShopDistance()
        {
            if (_localIdentity == null)
            {
                return float.MaxValue;
            }

            float nearest = float.MaxValue;
            foreach (ShopInteractionPoint point in _shopPoints.Values)
            {
                if ((point == null) || !point.IsOpen)
                {
                    continue;
                }

                float distance = Vector3.Distance(_localIdentity.transform.position,
                    point.transform.position);
                nearest = Mathf.Min(nearest, distance);
            }

            return nearest;
        }

        private void HandleEquippedWeaponChanged(WeaponCombatSnapshot _)
        {
            _targetEquipped = false;
            RefreshAllViews();
        }

        private void HandleEquipResult(uint _, InventoryResult result, uint __)
        {
            SetText(_inventoryStatusText, FunctionalUiText.GetInventoryResult(result));
            RefreshAllViews();
        }

        private void HandleTalentRevisionChanged(uint revision)
        {
            _talentRevision = revision;
            RefreshTalentSnapshot();
            RefreshTalentView();
        }

        private void HandleTalentResult(TalentAllocationResult result)
        {
            SetText(_talentStatusText, FunctionalUiText.GetTalentResult(result));
            RefreshTalentSnapshot();
            RefreshTalentView();
        }

        private void HandleBalanceChanged(int _)
        {
            RefreshShopView();
        }

        private void HandleShopTransaction(ShopTransactionResult result)
        {
            string message = FunctionalUiText.GetTransactionResult(result);
            SetText(_shopStatusText, message);
            SetText(_inventoryStatusText, message);
            RefreshAllViews();
        }

        private bool IsLocalTransferRecipient(EquipmentTransferOfferSnapshot offer)
        {
            return (_localIdentity != null) && (offer.RecipientNetId == _localIdentity.netId);
        }

        private void HandleTransferChanged(EquipmentTransferResult result)
        {
            // 곁에 선 파티원에게 바로 주는 중이면 배낭 안내 줄에도 결과를 적는다.
            // 아래 양도 페이지 문구는 그 페이지를 볼 때만 보이므로, 배낭에 선 사람은 결과를 못 본다
            if (IsQuickTransferMode)
            {
                UpdateQuickTransferStatus(result);
            }

            if (result.Offer.State == EquipmentTransferState.Offered)
            {
                _activeOffer = result.Offer;
                _preserveTransferFeedback = !result.Succeeded;
                SetText(_transferStatusText, result.Succeeded
                    ? "장비 양도 제안이 도착했습니다."
                    : FunctionalUiText.GetTransactionRejection(result.Rejection));

                // 레일이 사라져 수신자가 스스로 양도 페이지로 들어갈 길이 없다. 제안이 오면 열어 준다
                if (result.Succeeded && IsLocalTransferRecipient(result.Offer))
                {
                    Open(PageKind.Transfer);
                }
            }
            else
            {
                _preserveTransferFeedback = false;
                SetText(_transferStatusText, result.Succeeded
                    ? GetTransferCompletionText(result.Offer.State)
                    : FunctionalUiText.GetTransactionRejection(result.Rejection));
                _activeOffer = default;
            }

            RefreshAllViews();
        }

        private void RefreshTransferCountdown()
        {
            if (_activeOffer.State != EquipmentTransferState.Offered)
            {
                return;
            }

            double remaining = _activeOffer.ExpiresAtGameplayTime -
                RunTimeLocator.Current.GameplayElapsedSeconds;
            if (remaining <= 0d)
            {
                _activeOffer = default;
                _preserveTransferFeedback = false;
                SetText(_transferStatusText, "장비 양도 제안의 유효 시간이 끝났습니다.");
                RefreshRecipientView();
                return;
            }

            if (_preserveTransferFeedback)
            {
                return;
            }

            SetText(_transferStatusText,
                $"장비 양도 제안이 진행 중입니다. 남은 시간 {Mathf.CeilToInt((float)Mathf.Max(0f, (float)remaining))}초");
        }

        private bool CanLocalAcceptOffer()
        {
            return (_localIdentity != null) &&
                (_activeOffer.State == EquipmentTransferState.Offered) &&
                (_activeOffer.RecipientNetId == _localIdentity.netId);
        }

        private bool CanLocalCancelOffer()
        {
            return (_localIdentity != null) &&
                (_activeOffer.State == EquipmentTransferState.Offered) &&
                (_activeOffer.SenderNetId == _localIdentity.netId);
        }

        private bool TryGetSelectedConsumable(out InventorySlotSnapshot slot)
        {
            slot = default;
            if ((_selectedSlot < 0) || (_selectedSlot >= _slots.Length))
            {
                return false;
            }

            slot = _slots[_selectedSlot];
            return slot.Kind == InventoryItemKind.Consumable;
        }

        private bool HasSelectedKind(InventoryItemKind kind)
        {
            return (_selectedSlot >= 0) && (_selectedSlot < _slots.Length) &&
                (_slots[_selectedSlot].Kind == kind);
        }

        private bool SelectedConsumableMatches(bool potion)
        {
            return TryGetSelectedConsumable(out InventorySlotSnapshot slot) &&
                (potion ? IsPotion(slot.ItemId) : IsTicket(slot.ItemId));
        }

        private void ClearSelections()
        {
            _selectedSlot = -1;
            _selectedTicketSlot = -1;
            _selectedEquipmentSlot = -1;
            _targetEquipped = false;
            _preserveTransferFeedback = false;
            _activeOffer = default;
            _activeShopPoint = null;
        }

        private void ReleaseInputToken()
        {
            if (_inputToken == null)
            {
                return;
            }

            _inputToken.Dispose();
            _inputToken = null;
        }

        private static bool IsPotion(ShopItemKind kind)
        {
            return (kind == ShopItemKind.HealthPotion) ||
                (kind == ShopItemKind.SpiritPotion);
        }

        private static bool IsTicket(ShopItemKind kind)
        {
            return (kind == ShopItemKind.RerollTicket) ||
                (kind == ShopItemKind.UpgradeTicket);
        }

        private static string GetTransferCompletionText(EquipmentTransferState state)
        {
            switch (state)
            {
                case EquipmentTransferState.Accepted:
                    return "장비 양도가 완료되었습니다.";
                case EquipmentTransferState.Rejected:
                    return "장비 양도 제안이 거절되었습니다.";
                case EquipmentTransferState.Expired:
                    return "장비 양도 제안 시간이 끝났습니다.";
                case EquipmentTransferState.Cancelled:
                    return "장비 양도 제안을 취소했습니다.";
                default:
                    return "장비 양도 상태가 바뀌었습니다.";
            }
        }

        private static void BindButton(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.AddListener(action);
            }
        }

        private static void BindIndexedButtons(FunctionalUiIndexedButton[] buttons,
            System.Action<int> action)
        {
            for (int i = 0; i < ArrayLength(buttons); ++i)
            {
                if (buttons[i] != null)
                {
                    buttons[i].Clicked += action;
                }
            }
        }

        private static int ArrayLength<T>(T[] array)
        {
            return (array == null) ? 0 : array.Length;
        }

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null)
            {
                target.text = value;
            }
        }

        private static string ReadText(TMP_Text target)
        {
            return (target == null) ? string.Empty : target.text;
        }

        private static void SetInteractable(Button button, bool interactable)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }

        private static void SetActive(GameObject target, bool active)
        {
            if ((target != null) && (target.activeSelf != active))
            {
                target.SetActive(active);
            }
        }
    }
}
