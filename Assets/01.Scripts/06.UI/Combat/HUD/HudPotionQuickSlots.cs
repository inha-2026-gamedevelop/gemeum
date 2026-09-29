// System
using System;

// Unity
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Third party
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Systems;

namespace Geumeum.UI
{
    /// <summary>
    /// HUD 물약 퀵슬롯. 체력 물약과 신력 물약을 종류별로 한 칸씩 보이고, 키를 누르면 한 개를 마신다
    ///
    /// 판정은 서버가 한다. 여기서는 배낭에서 그 종류의 칸을 찾아 기존 물약 사용 요청을 보낼 뿐이다.
    /// 칸을 따로 등록하지 않는다 - 종류로 정해진 칸이라 배낭 어디에 있든 첫 칸을 쓴다
    /// </summary>
    public sealed class HudPotionQuickSlots : MonoBehaviour
    {
        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        private sealed class SlotView
        {
            public ShopItemKind Kind = ShopItemKind.HealthPotion;
            public KeyCode      Key  = KeyCode.Alpha1;
            public Image        Frame;
            public TMP_Text     CountLabel;

            [Tooltip("누르면 마신다. 비워 두면 키로만 쓴다")]
            public Button       Button;

            [NonSerialized] public int Count;
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private SlotView[] _slots = new SlotView[0];

        [Tooltip("키를 읽는다. 같은 칸을 배낭에도 두면 한쪽만 읽어야 한 번 누를 때 두 개를 마시지 않는다")]
        [SerializeField] private bool _readsKeys = true;

        [Tooltip("물약이 있을 때 칸 그림")]
        [SerializeField] private Sprite _readySprite;

        [Tooltip("물약이 없을 때 칸 그림")]
        [SerializeField] private Sprite _emptySprite;

        [Tooltip("물약이 없을 때 수량 글자 알파")]
        [SerializeField] private float _emptyLabelAlpha = 0.4f;

        [Tooltip("보낸 요청의 결과가 이 시간(초) 안에 오지 않으면 다시 누를 수 있게 한다")]
        [SerializeField] private float _requestTimeout = 1f;

        private readonly InventorySlotSnapshot[] _snapshot =
            new InventorySlotSnapshot[InventorySnapshot.SLOT_COUNT];

        private ShopTransactionService _shop;
        private NetworkIdentity        _boundIdentity;
        private InventoryController    _inventory;
        private uint                   _shownRevision;
        private bool                   _hasSnapshot;

        // 한 번에 요청 하나만 보낸다. 서버에 쿨다운이 없어 연타하면 한꺼번에 여러 개를 마신다
        private uint  _pendingRequestId;
        private float _pendingDeadline;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            // HUD는 SessionManagers 아래에 붙는다. 프리팹 간 참조를 두지 않고 부모에서 한 번 찾는다
            _shop = GetComponentInParent<ShopTransactionService>(true);
            if (_shop == null)
            {
                Debug.LogError("[HudPotionQuickSlots] 부모에서 ShopTransactionService를 찾지 못했다. 물약을 쓸 수 없다", this);
            }

            for (int i = 0; i < _slots.Length; ++i)
            {
                SlotView view = _slots[i];
                if (view.Button != null)
                {
                    view.Button.onClick.AddListener(() => TryUse(view));
                }
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _slots.Length; ++i)
            {
                if (_slots[i].Button != null)
                {
                    _slots[i].Button.onClick.RemoveAllListeners();
                }
            }
        }

        private void OnEnable()
        {
            if (_shop != null)
            {
                _shop.TransactionCompleted += HandleTransactionCompleted;
            }

            _hasSnapshot = false;
        }

        private void OnDisable()
        {
            if (_shop != null)
            {
                _shop.TransactionCompleted -= HandleTransactionCompleted;
            }

            _pendingRequestId = 0u;
        }

        private void Update()
        {
            RefreshBinding();
            RefreshCounts();

            if ((_pendingRequestId != 0u) && (Time.unscaledTime >= _pendingDeadline))
            {
                _pendingRequestId = 0u;
            }

            // 관리 창이나 메뉴가 열려 있으면 전투 입력과 함께 막는다. 배낭 안의 칸은 클릭으로만 쓴다
            if ((!_readsKeys) || (!LocalGameplayInputGate.AllowsGameplayInput))
            {
                return;
            }

            for (int i = 0; i < _slots.Length; ++i)
            {
                if (Input.GetKeyDown(_slots[i].Key))
                {
                    TryUse(_slots[i]);
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void RefreshBinding()
        {
            NetworkIdentity local = NetworkClient.localPlayer;
            if (local == _boundIdentity)
            {
                return;
            }

            // 재접속 등으로 로컬 플레이어가 바뀌었다. 이전 배낭으로 판단하지 않는다
            _boundIdentity = local;
            _inventory = (local == null) ? null : local.GetComponent<InventoryController>();
            _hasSnapshot = false;
            _pendingRequestId = 0u;
        }

        /// <summary> 배낭이 바뀐 때만 칸을 다시 읽어 종류별 수량을 센다 </summary>
        private void RefreshCounts()
        {
            if (_inventory == null)
            {
                if (_hasSnapshot)
                {
                    _hasSnapshot = false;
                    ClearCounts();
                }

                return;
            }

            if (_hasSnapshot && (_inventory.Revision == _shownRevision))
            {
                return;
            }

            if (!_inventory.TryCopyOwnerSlots(_snapshot, out uint revision))
            {
                return;
            }

            _shownRevision = revision;
            _hasSnapshot = true;
            for (int i = 0; i < _slots.Length; ++i)
            {
                _slots[i].Count = CountOf(_slots[i].Kind);
                ApplyView(_slots[i]);
            }
        }

        private void ClearCounts()
        {
            for (int i = 0; i < _slots.Length; ++i)
            {
                _slots[i].Count = 0;
                ApplyView(_slots[i]);
            }
        }

        private int CountOf(ShopItemKind kind)
        {
            int total = 0;
            for (int i = 0; i < _snapshot.Length; ++i)
            {
                if ((_snapshot[i].Kind == InventoryItemKind.Consumable) && (_snapshot[i].ItemId == kind))
                {
                    total += _snapshot[i].Count;
                }
            }

            return total;
        }

        private int FindSlot(ShopItemKind kind)
        {
            for (int i = 0; i < _snapshot.Length; ++i)
            {
                if ((_snapshot[i].Kind == InventoryItemKind.Consumable) && (_snapshot[i].ItemId == kind) &&
                    (_snapshot[i].Count > 0))
                {
                    return i;
                }
            }

            return -1;
        }

        private void TryUse(SlotView view)
        {
            if ((_shop == null) || (!_hasSnapshot) || (_pendingRequestId != 0u))
            {
                return;
            }

            int slot = FindSlot(view.Kind);
            if (slot < 0)
            {
                return;
            }

            uint requestId = _shop.RequestUsePotion(slot, view.Kind, _shownRevision);
            if (requestId == 0u)
            {
                return;
            }

            _pendingRequestId = requestId;
            _pendingDeadline = Time.unscaledTime + _requestTimeout;
        }

        private void HandleTransactionCompleted(ShopTransactionResult result)
        {
            if ((_pendingRequestId != 0u) && (result.RequestId == _pendingRequestId))
            {
                _pendingRequestId = 0u;
            }
        }

        private void ApplyView(SlotView view)
        {
            bool hasPotion = view.Count > 0;
            if (view.Frame != null)
            {
                Sprite sprite = hasPotion ? _readySprite : _emptySprite;
                if ((sprite != null) && (view.Frame.sprite != sprite))
                {
                    view.Frame.sprite = sprite;
                }
            }

            if (view.CountLabel != null)
            {
                view.CountLabel.text = view.Count.ToString();
                view.CountLabel.alpha = hasPotion ? 1f : _emptyLabelAlpha;
            }

            if ((view.Button != null) && (view.Button.interactable != hasPotion))
            {
                view.Button.interactable = hasPotion;
            }
        }
    }
}
