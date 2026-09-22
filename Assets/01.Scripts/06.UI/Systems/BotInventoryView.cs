using System.Collections.Generic;
using Geumeum.Combat;
using Geumeum.Common;
using Geumeum.Network;
using Geumeum.Systems;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Geumeum.UI
{
    /// <summary> 관리 창에서 같은 파티 봇의 장착 장비와 12칸을 조회한다. 장착/사용 요청은 제공하지 않는다 </summary>
    public sealed class BotInventoryView : MonoBehaviour
    {
        private readonly Dictionary<uint, PlayerStatus> _players = new Dictionary<uint, PlayerStatus>();
        private readonly List<uint> _stale = new List<uint>();
        private readonly List<PlayerStatus> _bots = new List<PlayerStatus>();
        private readonly Button[] _botButtons = new Button[2];
        private readonly TMP_Text[] _botLabels = new TMP_Text[2];
        private readonly TMP_Text[] _slots = new TMP_Text[InventorySnapshot.SLOT_COUNT];
        private GameObject _modal;
        private TMP_Text _equipped;
        private TMP_Text _detail;
        private TMP_FontAsset _font;
        private InventoryController _reader;
        private NetworkIdentity _owner;
        private uint _selectedBot;
        private int _selectedSlot = -1;
        private float _nextPoll;
        private BotInventorySnapshot _snapshot;

        public static BotInventoryView Create(GameObject page, TMP_FontAsset font)
        {
            var view = page.AddComponent<BotInventoryView>();
            view._font = font;
            view.Build();
            return view;
        }

        public void Close()
        {
            if (_modal != null) { _modal.SetActive(false); }
            _snapshot = default;
            _selectedBot = 0;
            _selectedSlot = -1;
        }

        private void OnDisable() { Close(); }

        private void Build()
        {
            Button open = MakeButton(transform, "동료 봇 배낭", Vector2.zero, new Vector2(170f, 36f), out _);
            // 제작된 배낭 페이지에는 별도 오프셋이 있으므로 창 좌표를 기준으로 배치한다
            open.transform.position = transform.parent.TransformPoint(new Vector3(-470f, -105f, 0f));
            open.onClick.AddListener(() => { _modal.SetActive(true); _nextPoll = 0f; });
            var root = new GameObject("BotInventoryModal", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(transform.parent, false);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, .85f);
            _modal = root;
            RectTransform body = MakeRect(root.transform, "BotInventory", Vector2.zero, new Vector2(880f, 600f));
            body.gameObject.AddComponent<Image>().color = new Color(.08f, .09f, .11f, 1f);
            Label(body, "동료 봇 배낭 - 조회 전용", new Vector2(0f, 262f), new Vector2(810f, 34f), 22f);
            MakeButton(body, "돌아가기", new Vector2(352f, 262f), new Vector2(120f, 32f), out _).onClick.AddListener(Close);
            for (int i = 0; i < _botButtons.Length; ++i)
            {
                int index = i;
                _botButtons[i] = MakeButton(body, "봇 없음", new Vector2(-200f + i * 400f, 216f), new Vector2(380f, 34f), out _botLabels[i]);
                _botButtons[i].onClick.AddListener(() =>
                {
                    if (index >= _bots.Count) { return; }
                    _selectedBot = _bots[index].netId; _selectedSlot = -1; _snapshot = default; _nextPoll = 0f;
                    Render();
                });
            }
            _equipped = Label(body, "봇을 확인하고 있습니다.", new Vector2(0f, 143f), new Vector2(810f, 100f), 15f);
            for (int i = 0; i < _slots.Length; ++i)
            {
                int slot = i;
                Button button = MakeButton(body, "", new Vector2(-205f + (i % 2) * 410f, 66f - (i / 2) * 38f), new Vector2(398f, 34f), out _slots[i]);
                button.onClick.AddListener(() => { _selectedSlot = slot; Render(); });
            }
            _detail = Label(body, "물품을 선택하면 상세 정보가 표시됩니다.", new Vector2(0f, -222f), new Vector2(810f, 118f), 15f);
            Close();
        }

        private void Update()
        {
            if (_modal == null || !_modal.activeSelf || FunctionalManagementPanel.Instance == null ||
                !FunctionalManagementPanel.Instance.IsVisible || Time.unscaledTime < _nextPoll) { return; }
            _nextPoll = Time.unscaledTime + .5f;
            if (_owner != NetworkClient.localPlayer)
            {
                _owner = NetworkClient.localPlayer;
                _reader = _owner == null ? null : _owner.GetComponent<InventoryController>();
                _players.Clear();
                _snapshot = default;
            }
            _stale.Clear();
            foreach (var pair in _players)
            { if (!NetworkClient.spawned.ContainsKey(pair.Key)) { _stale.Add(pair.Key); } }
            foreach (uint id in _stale) { _players.Remove(id); }
            foreach (var pair in NetworkClient.spawned)
            { if (!_players.ContainsKey(pair.Key)) { _players[pair.Key] = pair.Value.GetComponent<PlayerStatus>(); } }
            _bots.Clear();
            foreach (var player in _players.Values)
            { if (player != null && player.Participant.Kind == ParticipantKind.Bot) { _bots.Add(player); } }
            _bots.Sort((a, b) => a.Participant.CompareTo(b.Participant));
            if (!_bots.Exists(b => b.netId == _selectedBot))
            { _selectedBot = _bots.Count == 0 ? 0u : _bots[0].netId; _snapshot = default; _selectedSlot = -1; }
            for (int i = 0; i < _botButtons.Length; ++i)
            {
                _botButtons[i].interactable = i < _bots.Count;
                _botLabels[i].text = i < _bots.Count ? (_bots[i].netId == _selectedBot ? "선택 - " : "") + _bots[i].Participant : "봇 없음";
            }
            var run = RunManager.LocalInstance;
            var director = RunSceneDirector.LocalInstance;
            if (_reader != null && _selectedBot != 0u && run != null && director != null)
            {
                if (!_reader.TryGetBotInventory(_selectedBot, run.RunId.Value, director.Revision.Value, out _snapshot))
                { _snapshot = default; }
                _reader.RequestBotInventory(_selectedBot, run.RunId.Value, director.Revision.Value);
            }
            else { _snapshot = default; }
            Render();
        }

        private void Render()
        {
            bool available = _snapshot.Available && _snapshot.Slots != null && _snapshot.Slots.Length == _slots.Length;
            _equipped.text = available ? "현재 장착 장비\n" + FunctionalUiText.DescribeWeapon(_snapshot.Equipped)
                : (_selectedBot == 0u ? "현재 파티에 봇이 없습니다." : "봇의 배낭을 확인하고 있습니다.");
            for (int i = 0; i < _slots.Length; ++i)
            { _slots[i].text = (i + 1) + ". " + (available ? FunctionalUiText.DescribeSlot(_snapshot.Slots[i]) : "-"); }
            _detail.text = "조회 전용입니다. 봇이 획득/장착/사용한 결과가 자동으로 갱신됩니다.";
            if (available && _selectedSlot >= 0 && _selectedSlot < _slots.Length)
            {
                var slot = _snapshot.Slots[_selectedSlot];
                _detail.text = slot.Kind == InventoryItemKind.Equipment ? FunctionalUiText.DescribeWeapon(slot.Equipment) : FunctionalUiText.DescribeSlot(slot);
            }
        }

        private static RectTransform MakeRect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position; rect.sizeDelta = size;
            return rect;
        }
        private TMP_Text Label(Transform parent, string text, Vector2 position, Vector2 size, float fontSize)
        {
            var label = MakeRect(parent, "Label", position, size).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = _font; label.text = text; label.fontSize = fontSize;
            label.color = new Color(.9f, .87f, .76f); label.raycastTarget = false;
            return label;
        }
        private Button MakeButton(Transform parent, string text, Vector2 position, Vector2 size, out TMP_Text label)
        {
            var rect = MakeRect(parent, "Button", position, size);
            var background = rect.gameObject.AddComponent<Image>(); background.color = new Color(.19f, .22f, .26f);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = background;
            label = Label(rect, text, Vector2.zero, size - new Vector2(12f, 0f), 15f);
            label.alignment = TextAlignmentOptions.Center;
            return button;
        }
    }
}
