// System
using System.Collections.Generic;

// Unity
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Third party
using Mirror;

// Project
using Geumeum.Common;
using Geumeum.Network;

namespace Geumeum.UI
{
    /// <summary> 오른쪽 아래 파티 대화와 개인 시스템 안내. 입력 중에만 게임 입력을 소유한다 </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class PartyChatPanel : MonoBehaviour
    {
        private const string PREFAB_RESOURCE = "UI/PartyChatPanel";
        private const float BOTTOM_THRESHOLD = 0.02f;
        private const int TAB_COUNT = 3;

        [SerializeField] private Color _partyColor = new Color(0.66f, 0.88f, 0.95f, 1f);
        [SerializeField] private Color _systemColor = new Color(0.96f, 0.79f, 0.46f, 1f);
        [SerializeField] private Color _selectedTabColor = new Color(0.39f, 0.31f, 0.21f, 0.85f);
        [SerializeField] private Color _inactiveTabColor = new Color(0.13f, 0.14f, 0.14f, 0.55f);
        [SerializeField] private float _collapsedHeight = 34f;

        [SerializeField] private Canvas _canvas;
        [SerializeField] private RectTransform _panel;
        [SerializeField] private GameObject _body;
        [SerializeField] private ScrollRect _scroll;
        [SerializeField] private RectTransform _content;
        [SerializeField] private TMP_InputField _input;
        [SerializeField] private Button _latestButton;
        [SerializeField] private TMP_Text _collapseLabel;
        [SerializeField] private Button _collapseButton;
        [SerializeField] private TMP_Text _messageTemplate;
        [SerializeField] private Button[] _tabs = new Button[TAB_COUNT];
        private Vector2 _expandedSize;
        private readonly List<TMP_Text> _rows = new List<TMP_Text>();
        private LocalGameplayInputToken _inputToken;
        private bool _isTyping;
        private bool _isCollapsed;
        private bool _followLatest = true;
        private bool _refreshing;
        private bool _wasConnected;
        private int _activeTab;
        private int _suppressEnterFrame = -1;
        private int _focusRequestedFrame = -1;

        public static PartyChatPanel Instance { get; private set; }
        public TMP_InputField InputField => _input;
        public bool IsTyping => _isTyping;
        public bool IsCollapsed => _isCollapsed;
        public int ActiveTab => _activeTab;
        public int VisibleMessageCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if ((Instance != null) || (Application.isBatchMode &&
                SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null))
            {
                return;
            }

            GameObject prefab = Resources.Load<GameObject>(PREFAB_RESOURCE);
            if (prefab == null)
            {
                Debug.LogError("[PartyChat] 채팅 프리팹이 없습니다");
                return;
            }

            GameObject panel = Instantiate(prefab);
            DontDestroyOnLoad(panel);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic() { Instance = null; }

        private void Awake()
        {
            if ((Instance != null) && (Instance != this))
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _expandedSize = _panel.sizeDelta;
            BindView();
            SelectTab(0);
            _canvas.enabled = false;
        }

        private void OnEnable() { LocalChatLog.Changed += RefreshMessages; }

        private void OnDisable()
        {
            LocalChatLog.Changed -= RefreshMessages;
            CancelInput();
        }

        private void OnDestroy()
        {
            CancelInput();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            // 키와 접속 상태만 확인한다. 내용은 로그 변경 이벤트에서 갱신한다
            bool connected = NetworkClient.active && NetworkClient.isConnected;
            if (connected != _wasConnected)
            {
                _wasConnected = connected;
                _canvas.enabled = connected;
                CancelInput();
            }

            if (!connected)
            {
                return;
            }

            if (_isTyping)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    CancelInput();
                }
                else if (!_input.isFocused && (Time.frameCount != _focusRequestedFrame))
                {
                    CancelInput();
                }
            }
            else if ((Time.frameCount != _suppressEnterFrame) &&
                (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
            {
                FocusInput();
            }
        }

        private void LateUpdate()
        {
            if (_isTyping)
            {
                LocalGameplayInputGate.Maintain(_inputToken);
            }
        }

        public void FocusInput()
        {
            if (!_isTyping && !LocalGameplayInputGate.AllowsGameplayInput)
            {
                return;
            }

            SetCollapsed(false);
            BeginInput();
            // TMP는 다음 LateUpdate에 선택을 완료하므로 시작 프레임의 미선택은 취소가 아니다
            _focusRequestedFrame = Time.frameCount;
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(_input.gameObject);
            }

            _input.ActivateInputField();
        }

        private void BeginInput()
        {
            if (_isTyping)
            {
                return;
            }

            if (!LocalGameplayInputGate.AllowsGameplayInput)
            {
                _input.DeactivateInputField();
                return;
            }

            _inputToken = LocalGameplayInputGate.Acquire();
            _isTyping = true;
        }

        public void CancelInput()
        {
            _isTyping = false;
            _inputToken?.Dispose();
            _inputToken = null;
            if (_input != null)
            {
                _input.DeactivateInputField();
            }
        }

        public void SubmitCurrentText()
        {
            string text = _input.text;
            _suppressEnterFrame = Time.frameCount;
            if (!string.IsNullOrWhiteSpace(text) && !PartyChatNetwork.Send(text))
            {
                // 실패하면 작성한 문장을 남겨 다시 보낼 수 있게 한다
                return;
            }

            _input.text = string.Empty;
            CancelInput();
        }

        public void SelectTab(int index)
        {
            _activeTab = Mathf.Clamp(index, 0, TAB_COUNT - 1);
            _followLatest = true;
            for (int i = 0; i < _tabs.Length; ++i)
            {
                _tabs[i].GetComponent<Image>().color = (i == _activeTab)
                    ? _selectedTabColor : _inactiveTabColor;
            }

            RefreshMessages();
        }

        public void SetCollapsed(bool collapsed)
        {
            _isCollapsed = collapsed;
            _panel.sizeDelta = new Vector2(_expandedSize.x, collapsed ? _collapsedHeight : _expandedSize.y);
            _body.SetActive(!collapsed);
            _collapseLabel.text = collapsed ? "+" : "-";
            if (collapsed)
            {
                CancelInput();
            }
        }

        private void ScrollToLatest()
        {
            _followLatest = true;
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 0f;
            _latestButton.gameObject.SetActive(false);
        }

        private void RefreshMessages()
        {
            if (_content == null)
            {
                return;
            }

            _refreshing = true;
            IReadOnlyList<ChatMessage> entries = LocalChatLog.Entries;
            int shown = 0;
            for (int i = 0; i < entries.Count; ++i)
            {
                ChatMessage message = entries[i];
                if (((_activeTab == 1) && (message.Channel != ChatChannel.Party)) ||
                    ((_activeTab == 2) && (message.Channel != ChatChannel.System)))
                {
                    continue;
                }

                if (shown >= _rows.Count)
                {
                    TMP_Text row = Instantiate(_messageTemplate, _content);
                    row.name = "Message";
                    _rows.Add(row);
                }

                TMP_Text current = _rows[shown];
                current.gameObject.SetActive(true);
                current.text = (message.Channel == ChatChannel.System)
                    ? "[시스템] " + message.Text
                    : "[파티] " + message.Sender + ": " + message.Text;
                current.color = (message.Channel == ChatChannel.System) ? _systemColor : _partyColor;
                ++shown;
            }

            for (int i = shown; i < _rows.Count; ++i)
            {
                _rows[i].gameObject.SetActive(false);
            }

            VisibleMessageCount = shown;
            if (_followLatest)
            {
                ScrollToLatest();
            }
            else
            {
                _latestButton.gameObject.SetActive(true);
            }

            _refreshing = false;
        }

        private void BindView()
        {
            // 계층과 모양은 프리팹에서 정한다. 여기서는 이벤트만 연결한다
            for (int i = 0; i < _tabs.Length; ++i)
            {
                int tab = i;
                _tabs[i].onClick.AddListener(() => SelectTab(tab));
            }

            _collapseButton.onClick.AddListener(() => SetCollapsed(!_isCollapsed));
            _latestButton.onClick.AddListener(ScrollToLatest);
            _scroll.onValueChanged.AddListener(position =>
            {
                if (!_refreshing)
                {
                    _followLatest = position.y <= BOTTOM_THRESHOLD;
                    _latestButton.gameObject.SetActive(!_followLatest);
                }
            });
            _input.onSelect.AddListener(_ => BeginInput());
            _input.onSubmit.AddListener(_ => SubmitCurrentText());
            _input.onEndEdit.AddListener(_ => CancelInput());
        }
    }
}
