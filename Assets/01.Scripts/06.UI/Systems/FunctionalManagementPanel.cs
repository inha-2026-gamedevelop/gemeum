// System
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
        private const int TALENT_BUTTON_COUNT = 10;

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
        [SerializeField] private GameObject _inventoryPage;
        [SerializeField] private GameObject _talentPage;
        [SerializeField] private GameObject _shopPage;

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
        }

        private void Awake()
        {
            Instance = this;
            BindButtons();
            SetVisible(false);
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
        }

        public void RequestSelectedPrimaryAction()
        {
            if ((_inventory == null) || (_selectedSlot < 0) ||
                (_selectedSlot >= _slots.Length))
            {
                SetText(_inventoryStatusText, "먼저 인벤토리 칸을 선택해 주세요.");
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
                SetText(_inventoryStatusText, "인벤토리를 아직 불러오지 못했습니다.");
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
                SetText(_transferStatusText, "인벤토리에 있는 장비만 양도할 수 있습니다.");
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
            _isVisible = visible;
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = visible ? 1f : 0f;
                _canvasGroup.interactable = visible;
                _canvasGroup.blocksRaycasts = visible;
            }

            if (!visible)
            {
                ReleaseInputToken();
                _activeShopPoint = null;
            }
        }

        private void SetPage(PageKind page)
        {
            SetActive(_inventoryPage, page == PageKind.Inventory);
            SetActive(_talentPage, page == PageKind.Talent);
            SetActive(_shopPage, page == PageKind.Shop);
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
            // keeping revision 1, so revision alone cannot identify this snapshot.
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

            SetInteractable(_equipButton, HasSelectedKind(InventoryItemKind.Equipment));
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

            for (int i = 0; i < _visibleTalentNodeIds.Length; ++i)
            {
                _visibleTalentNodeIds[i] = 0;
            }

            if ((_talentRevision == 0u) || !_allocation.HasWeapon ||
                !GameDB.IsInitialized || (GameDB.Talent == null))
            {
                SetTalentButtonsUnavailable();
                return;
            }

            TalentTreeSO tree = GameDB.Talent;
            for (int column = 0; column < tree.ColumnsPerWeapon; ++column)
            {
                if ((column < ArrayLength(_talentColumnTexts)) &&
                    tree.TryGetColumn(_allocation.Weapon, column,
                        out TalentTreeSO.ColumnEntry columnEntry))
                {
                    SetText(_talentColumnTexts[column],
                        string.IsNullOrWhiteSpace(columnEntry.DisplayName)
                            ? $"{FunctionalUiText.GetWeaponName(_allocation.Weapon)} 특성"
                            : columnEntry.DisplayName);
                }

                for (int row = 0; row < tree.NodesPerColumn; ++row)
                {
                    int index = (column * tree.NodesPerColumn) + row;
                    if ((index >= ArrayLength(_talentButtons)) ||
                        !tree.TryGetNode(_allocation.Weapon, column, row,
                            out TalentTreeSO.NodeEntry node))
                    {
                        continue;
                    }

                    FunctionalUiIndexedButton button = _talentButtons[index];
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
                    button.SetInteractable(!allocated &&
                        (allowed == TalentAllocationResult.Success));
                }
            }

            SetInteractable(_talentResetButton, _allocation.SpentPoints > 0);
        }

        private void SetTalentButtonsUnavailable()
        {
            for (int i = 0; i < ArrayLength(_talentButtons); ++i)
            {
                _talentButtons[i].SetLabel("특성 확인 중");
                _talentButtons[i].SetInteractable(false);
            }

            SetInteractable(_talentResetButton, false);
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
