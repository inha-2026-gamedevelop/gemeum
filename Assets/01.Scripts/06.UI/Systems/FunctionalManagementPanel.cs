// System
using System;
using System.Collections.Generic;

// Unity
using TMPro;
using UnityEngine;
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
        // 4갈래 x 5행 격자. 검/활은 2열 5행, 방울은 뿌리 1 + 세 갈래 4개를 쓴다
        private const int TALENT_BUTTON_COUNT = 20;
        private const int TALENT_ROWS_PER_COLUMN = 5;

        // 특성 노드 설명 상자. 기준 좌표계(308 너비) 값이고 _talentLayoutScale로 함께 늘린다
        private const float TALENT_TOOLTIP_WIDTH = 260f;
        private const float TALENT_TOOLTIP_PADDING = 10f;
        private const float TALENT_TOOLTIP_GAP = 8f;
        private const float TALENT_TOOLTIP_FONT_SIZE = 13f;
        private const float TALENT_TOOLTIP_MIN_FONT_SIZE = 8f;

        // 제작 UI 팔레트와 같은 값을 쓴다
        private static readonly Color G_TooltipBackground = new Color(0.075f, 0.095f, 0.13f, 0.98f);
        private static readonly Color G_TooltipText = new Color(0.96f, 0.95f, 0.91f, 1f);

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
        [SerializeField] private Button _inventoryTabButton;
        [SerializeField] private Button _talentTabButton;
        [SerializeField] private Button _shopTabButton;
        [SerializeField] private Button _transferTabButton;
        [SerializeField] private GameObject _inventoryPage;
        [SerializeField] private GameObject _talentPage;
        [SerializeField] private GameObject _shopPage;
        [SerializeField] private GameObject _transferPage;
        [SerializeField] private RailRowVisual _inventoryRow;
        [SerializeField] private RailRowVisual _talentRow;
        [SerializeField] private RailRowVisual _transferRow;

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

        [Header("특성")]
        [SerializeField] private TMP_Text _talentPointText;
        [SerializeField] private TMP_Text _talentStatusText;
        [SerializeField] private TMP_Text[] _talentColumnTexts;
        [SerializeField] private FunctionalUiIndexedButton[] _talentButtons;
        [SerializeField] private Button _talentResetButton;

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

        private readonly InventorySlotSnapshot[] _slots =
            new InventorySlotSnapshot[InventorySnapshot.SLOT_COUNT];
        private readonly int[] _visibleTalentNodeIds = new int[TALENT_BUTTON_COUNT];
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

        private void Awake()
        {
            Instance = this;
            EnsureTalentViewCapacity();
            if (_inventoryPage != null && _inventoryDetailText != null)
            { _botInventoryView = BotInventoryView.Create(_inventoryPage, _inventoryDetailText.font); }
            BindButtons();
            SetVisible(false);
        }

        private readonly List<Image> _talentEdges = new List<Image>();
        private float _talentLayoutScale = 1f;
        private Vector2 _talentLayoutOffset;

        // 특성 노드 상세 설명. 프리팹을 고치지 않도록 런타임에 만든다
        private GameObject _talentTooltip;
        private RectTransform _talentTooltipRect;
        private TMP_Text _talentTooltipText;
        private int _hoveredTalentIndex = -1;

        // 지금 열려 있는 탭. 계층 활성 상태가 아니라 이 값으로 판단해야 키보드 select 잔류나
        // 바깥에서 들어온 이벤트로 특성 설명이 다른 탭 위에 뜨지 않는다
        private PageKind _currentPage = PageKind.Inventory;

        // 기존 프리팹의 버튼/폰트를 재사용해 씬마다 오래된 10칸 UI가 남지 않게 한다
        private void EnsureTalentViewCapacity()
        {
            if (_talentButtons == null || _talentButtons.Length == 0 || _talentButtons[0] == null) { return; }
            // 프리팹마다 페이지 위치/배율이 다르므로 기존 콘텐츠 좌표계에서 배치한다
            _talentLayoutScale = _talentPointText == null ? 1f : _talentPointText.rectTransform.rect.width / 308f;
            float center = _talentPointText == null ? 476f : _talentPointText.rectTransform.anchoredPosition.x;
            float top = ((RectTransform)_talentButtons[0].transform).anchoredPosition.y;
            _talentLayoutOffset = new Vector2(center - 476f * _talentLayoutScale, top - 118f * _talentLayoutScale);
            int previous = _talentButtons.Length;
            Array.Resize(ref _talentButtons, TALENT_BUTTON_COUNT);
            for (int i = previous; i < TALENT_BUTTON_COUNT; ++i)
            {
                var button = Instantiate(_talentButtons[0], _talentButtons[0].transform.parent);
                button.name = "TalentNode" + i;
                button.SetIndex(i);
                _talentButtons[i] = button;
            }
            if (_talentColumnTexts != null && _talentColumnTexts.Length > 0)
            {
                previous = _talentColumnTexts.Length;
                Array.Resize(ref _talentColumnTexts, 4);
                for (int i = previous; i < 4; ++i)
                { _talentColumnTexts[i] = Instantiate(_talentColumnTexts[0], _talentColumnTexts[0].transform.parent); }
            }

            EnsureTalentTooltip();
        }

        private Vector2 TalentPosition(int column, int row)
        {
            if (GameDB.Talent.MaxColumnsPerWeapon > 2)
            { return TalentLayoutPoint(column == 0 ? new Vector2(476, 136) : new Vector2(370 + (column - 1) * 106, 42 - row * 60)); }
            return TalentLayoutPoint(new Vector2(397 + column * 158, 118 - row * 62));
        }

        private Vector2 TalentLayoutPoint(Vector2 point) => point * _talentLayoutScale + _talentLayoutOffset;

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

            float padding = TALENT_TOOLTIP_PADDING * _talentLayoutScale;
            RectTransform textRect = (RectTransform)textObject.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(padding, padding);
            textRect.offsetMax = new Vector2(-padding, -padding);

            TextMeshProUGUI label = textObject.AddComponent<TextMeshProUGUI>();
            label.font = ResolveTalentFont();
            label.fontSize = TALENT_TOOLTIP_FONT_SIZE * _talentLayoutScale;
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

            float padding = TALENT_TOOLTIP_PADDING * _talentLayoutScale;
            float width = Mathf.Min(TALENT_TOOLTIP_WIDTH * _talentLayoutScale,
                Mathf.Max(1f, bounds.width));

            // 앞선 노드에서 켜 둔 자동 축소가 남아 있으면 이번 높이를 잘못 잰다. 늘 같은
            // 글자 크기로 재고 나서 넘칠 때만 줄인다
            float fontSize = TALENT_TOOLTIP_FONT_SIZE * _talentLayoutScale;
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

            float offset = (anchor.sizeDelta.x * 0.5f) + (width * 0.5f) +
                (TALENT_TOOLTIP_GAP * _talentLayoutScale);
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

        private void RefreshTalentEdges(TalentTreeSO tree)
        {
            int used = 0;
            for (int i = 0; i < _visibleTalentNodeIds.Length; ++i)
            {
                if (!tree.TryGetNode(_visibleTalentNodeIds[i], out var node) || !node.HasExplicitPrerequisites) { continue; }
                foreach (int parent in node.Prerequisites)
                {
                    if (!tree.TryGetNode(parent, out var prerequisite)) { continue; }
                    if (used == _talentEdges.Count)
                    {
                        var go = new GameObject("TalentConnection", typeof(RectTransform), typeof(Image));
                        go.transform.SetParent(_talentButtons[i].transform.parent, false);
                        go.transform.SetAsFirstSibling();
                        var image = go.GetComponent<Image>(); image.raycastTarget = false;
                        _talentEdges.Add(image);
                    }
                    var line = _talentEdges[used++]; line.gameObject.SetActive(true);
                    var start = TalentPosition(prerequisite.ColumnIndex, prerequisite.RowIndex);
                    var end = TalentPosition(node.ColumnIndex, node.RowIndex);
                    var rect = line.rectTransform;
                    var template = (RectTransform)_talentButtons[i].transform;
                    rect.anchorMin = template.anchorMin; rect.anchorMax = template.anchorMax;
                    rect.anchoredPosition = (start + end) * .5f;
                    rect.sizeDelta = new Vector2(Vector2.Distance(start, end), 2);
                    rect.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg);
                    line.color = _allocation.Contains(parent) ? new Color(.65f, .8f, .85f, .8f) : new Color(.3f, .36f, .4f, .6f);
                }
            }
            for (int i = used; i < _talentEdges.Count; ++i) { _talentEdges[i].gameObject.SetActive(false); }
        }

        private void OnEnable()
        {
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

            SetVisible(false);
        }

        private void OnDestroy()
        {
            ReleaseInputToken();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
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

            LocalGameplayInputGate.Maintain(_inputToken);
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }

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
            SetVisible(false);
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
            BindButton(_inventoryTabButton, OpenInventory);
            BindButton(_talentTabButton, OpenTalents);
            BindButton(_shopTabButton, () => Open(PageKind.Shop));
            BindButton(_transferTabButton, OpenTransfer);
            BindButton(_equipButton, RequestSelectedPrimaryAction);
            BindButton(_unequipButton, RequestUnequip);
            BindButton(_usePotionButton, RequestUseSelectedPotion);
            BindButton(_useTicketButton, RequestUseSelectedTicket);
            BindButton(_targetEquippedButton, SelectEquippedTicketTarget);
            BindButton(_talentResetButton, RequestResetTalents);
            BindButton(_acceptTransferButton, RequestAcceptTransfer);
            BindButton(_rejectTransferButton, RequestRejectTransfer);
            BindButton(_cancelTransferButton, RequestCancelTransfer);

            BindIndexedButtons(_inventoryButtons, SelectInventorySlot);
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
            bool changed = (_isVisible != visible);

            _isVisible = visible;
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = visible ? 1f : 0f;
                _canvasGroup.interactable = visible;
                _canvasGroup.blocksRaycasts = visible;
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

        private void SetPage(PageKind page)
        {
            _currentPage = page;
            SetText(_pageTitle, page == PageKind.Inventory ? "배낭" :
                page == PageKind.Talent ? "특성" : page == PageKind.Shop ? "객주" : "양도");
            SetActive(_inventoryPage, page == PageKind.Inventory);
            SetActive(_talentPage, page == PageKind.Talent);
            SetActive(_shopPage, page == PageKind.Shop);
            SetActive(_transferPage, page == PageKind.Transfer);

            SetRowActive(_inventoryRow, page == PageKind.Inventory);
            SetRowActive(_talentRow, page == PageKind.Talent);
            SetRowActive(_transferRow, page == PageKind.Transfer);

            if (page != PageKind.Talent)
            {
                // 탭을 옮기면 포인터 나감이 오지 않는 경로가 있어 직접 지운다
                _hoveredTalentIndex = -1;
                HideTalentTooltip();
            }
        }

        private static void SetRowActive(RailRowVisual row, bool isActive)
        {
            if (row != null)
            {
                row.SetActiveRow(isActive);
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
        }

        private void RefreshInventorySnapshot()
        {
            if ((_inventory == null) ||
                (!_inventory.TryCopyOwnerSlots(_slots, out uint revision)))
            {
                return;
            }

            _inventoryRevision = revision;
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
                button.SetInteractable(_inventoryRevision != 0u);
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

            for (int i = 0; i < _visibleTalentNodeIds.Length; ++i)
            {
                _visibleTalentNodeIds[i] = 0;
            }

            if ((_talentRevision == 0u) || !_allocation.HasWeapon ||
                !GameDB.IsInitialized || (GameDB.Talent == null))
            {
                SetTalentButtonsUnavailable();
                ReleaseStaleTalentHover(hoveredNodeBefore);
                return;
            }

            // 무기마다 갈래 수와 갈래별 노드 수가 다르다. 격자를 통째로 도는 대신
            // 실제 정의된 열/노드만 그린다 - 없는 칸에 "1단계 특성"이 남으면 안 된다
            TalentTreeSO tree = GameDB.Talent;
            int columnCount = tree.CountColumns(_allocation.Weapon);
            int missing = 0;

            for (int i = 0; i < ArrayLength(_talentColumnTexts); ++i)
            {
                SetText(_talentColumnTexts[i], string.Empty);
                _talentColumnTexts[i].gameObject.SetActive(i < columnCount);
                var labelRect = _talentColumnTexts[i].rectTransform;
                labelRect.anchoredPosition = TalentLayoutPoint(GameDB.Talent.MaxColumnsPerWeapon > 2 ?
                    (i == 0 ? new Vector2(476, 173) : new Vector2(370 + (i - 1) * 106, 82)) : new Vector2(397 + i * 158, 168));
                labelRect.sizeDelta = new Vector2(GameDB.Talent.MaxColumnsPerWeapon > 2 ? 102 : 150, 28) * _talentLayoutScale;
            }

            for (int column = 0; column < columnCount; ++column)
            {
                if (!tree.TryGetColumn(_allocation.Weapon, column,
                        out TalentTreeSO.ColumnEntry columnEntry))
                {
                    continue;
                }

                if (column < ArrayLength(_talentColumnTexts))
                {
                    SetText(_talentColumnTexts[column],
                        string.IsNullOrWhiteSpace(columnEntry.DisplayName)
                            ? $"{FunctionalUiText.GetWeaponName(_allocation.Weapon)} 특성"
                            : columnEntry.DisplayName);
                }

                for (int row = 0; row < columnEntry.NodeCount; ++row)
                {
                    if (!tree.TryGetNode(_allocation.Weapon, column, row,
                            out TalentTreeSO.NodeEntry node))
                    {
                        continue;
                    }

                    int index = (column * TALENT_ROWS_PER_COLUMN) + row;
                    if ((index >= ArrayLength(_talentButtons)) ||
                        (index >= _visibleTalentNodeIds.Length))
                    {
                        ++missing;
                        continue;
                    }

                    FunctionalUiIndexedButton button = _talentButtons[index];
                    SetTalentButtonActive(button, true);
                    var rect = (RectTransform)button.transform;
                    rect.anchoredPosition = TalentPosition(column, row);
                    rect.sizeDelta = new Vector2(GameDB.Talent.MaxColumnsPerWeapon > 2 ? 100 : 150, 50) * _talentLayoutScale;
                    _visibleTalentNodeIds[index] = node.NodeId;
                    bool allocated = _allocation.Contains(node.NodeId);
                    string name = string.IsNullOrWhiteSpace(node.DisplayName)
                        ? $"{row + 1}단계 특성" : node.DisplayName;
                    string state = allocated ? "선택됨" :
                        FunctionalUiText.DescribeTalentValue(node.EffectKind, node.Value);
                    button.SetLabel($"{name}\n{state}");
                    TalentAllocationResult allowed = TalentRules.CanAllocate(tree,
                        _allocation, available + _allocation.SpentPoints,
                        node.NodeId, out _);
                    SetTalentButtonInteractable(button, (!allocated) &&
                        (allowed == TalentAllocationResult.Success));
                }
            }

            // 이번에 쓰지 않은 칸만 뒤늦게 숨긴다. 쓰는 노드를 먼저 껐다 켜면 포인터가
            // 그대로인데도 hover가 끊겨 설명이 0.2초마다 사라진다
            int slotCount = Mathf.Min(ArrayLength(_talentButtons), _visibleTalentNodeIds.Length);
            for (int i = 0; i < slotCount; ++i)
            {
                if (_visibleTalentNodeIds[i] > 0)
                {
                    continue;
                }

                _talentButtons[i].SetLabel(string.Empty);
                SetTalentButtonInteractable(_talentButtons[i], false);
                SetTalentButtonActive(_talentButtons[i], false);
            }

            if (missing > 0)
            {
                // 고르지 못하는 노드가 있는 화면을 조용히 넘기지 않는다
                Debug.LogError($"[FunctionalManagementPanel] 특성 버튼이 {missing}개 모자라 " +
                    "일부 노드를 표시하지 못했다. 관리 창 프리팹을 다시 생성해야 한다", this);
            }

            RefreshTalentEdges(tree);
            SetInteractable(_talentResetButton, _allocation.SpentPoints > 0);

            ReleaseStaleTalentHover(hoveredNodeBefore);

            // 포인트나 배분이 바뀌면 떠 있는 설명의 상태 줄도 같이 갱신해야 한다
            RefreshTalentTooltip();
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
            for (int i = 0; i < ArrayLength(_talentButtons); ++i)
            {
                _talentButtons[i].SetLabel("특성 확인 중");
                _talentButtons[i].SetInteractable(false);
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
                _activeShopPoint.IsOpen && (_inventoryRevision != 0u);
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

        private void HandleTransferChanged(EquipmentTransferResult result)
        {
            if (result.Offer.State == EquipmentTransferState.Offered)
            {
                _activeOffer = result.Offer;
                _preserveTransferFeedback = !result.Succeeded;
                SetText(_transferStatusText, result.Succeeded
                    ? "장비 양도 제안이 도착했습니다."
                    : FunctionalUiText.GetTransactionRejection(result.Rejection));
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
