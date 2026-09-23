// Unity
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 프로젝트 내부
using Geumeum.Network;

namespace Geumeum.UI
{
    /// <summary>
    /// 출시 Title 씬의 첫 화면. 런처 로그인 세션으로 원정대를 찾거나 만든다.
    /// 아무 키 안내 -> 메뉴 -> (원정대 이름 입력) 순서이며 방 목록, 파티는 Lobby 씬의 AccountLobbyPanel이 맡는다.
    /// </summary>
    public sealed class AccountTitleMenu : MonoBehaviour
    {
        [Header("안내")]
        [SerializeField] private GameObject _pressAnyKeyPanel;
        [SerializeField] private Button _promptButton;

        [Header("메뉴")]
        [SerializeField] private GameObject _menuPanel;
        [SerializeField] private Button _findButton;
        [SerializeField] private Button _createButton;
        [SerializeField] private Button _quitButton;

        [Header("원정대 만들기")]
        [SerializeField] private GameObject _createPanel;
        [SerializeField] private TMP_InputField _roomNameInput;
        [SerializeField] private Button _createConfirmButton;
        [SerializeField] private Button _createCancelButton;

        [Header("상태")]
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private TMP_Text _identityText;

        // 한 번 안내를 넘기면 한 판을 마치고 돌아왔을 때 다시 묻지 않는다.
        private static bool G_PromptDismissed;

        private string _state = string.Empty;
        private string _shownStatus;
        private string _shownIdentity;

        public static AccountTitleMenu Instance { get; private set; }
        public string StateName => _state;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            G_PromptDismissed = false;
            Instance = null;
        }

        private void Awake()
        {
            if (!HasRequiredReferences())
            {
                enabled = false;
                return;
            }
            Instance = this;
            _promptButton.onClick.AddListener(ShowMenu);
            _findButton.onClick.AddListener(FindRooms);
            _createButton.onClick.AddListener(OpenCreatePanel);
            _quitButton.onClick.AddListener(QuitGame);
            _createConfirmButton.onClick.AddListener(ConfirmCreate);
            _createCancelButton.onClick.AddListener(CloseCreatePanel);
            _roomNameInput.onSubmit.AddListener(_ => ConfirmCreate());
            _roomNameInput.characterLimit = AccountLobbyPanel.MAX_ROOM_NAME_LENGTH;
            _createPanel.SetActive(false);
            if (G_PromptDismissed)
            {
                ShowMenu();
            }
            else
            {
                ShowPrompt();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (_state == "prompt")
            {
                if (Input.anyKeyDown)
                {
                    ShowMenu();
                }
                RenderStatus();
                return;
            }
            if ((_state == "create") && (Input.GetKeyDown(KeyCode.Escape)))
            {
                CloseCreatePanel();
            }

            bool usable = (AccountLobbyPanel.Instance != null) && (AccountLobbyPanel.Instance.CanUseTitleMenu);
            _findButton.interactable = usable;
            _createButton.interactable = usable;
            _createConfirmButton.interactable = usable;
            _roomNameInput.interactable = usable;

            // 자체 EventSystem은 계정 화면이 한 프레임 뒤에 켜므로 선택은 준비된 뒤에 맞춘다.
            EventSystem system = EventSystem.current;
            if ((system != null) && (system.currentSelectedGameObject == null))
            {
                system.SetSelectedGameObject(_state == "create"
                    ? _roomNameInput.gameObject : _findButton.gameObject);
            }
            RenderStatus();
        }

        public void ShowMenu()
        {
            G_PromptDismissed = true;
            _pressAnyKeyPanel.SetActive(false);
            _menuPanel.SetActive(true);
            _createPanel.SetActive(false);
            _state = "menu";
            Select(_findButton.gameObject);
        }

        public void FindRooms()
        {
            AccountLobbyPanel panel = AccountLobbyPanel.Instance;
            if (panel != null)
            {
                panel.OpenRoomsFromTitle();
            }
        }

        public void OpenCreatePanel()
        {
            AccountLobbyPanel panel = AccountLobbyPanel.Instance;
            if ((panel == null) || (!panel.CanUseTitleMenu))
            {
                return;
            }
            // 입력 창이 열린 동안 뒤 메뉴가 눌리거나 방향키로 선택되지 않게 숨긴다.
            _menuPanel.SetActive(false);
            panel.ClearRoomNameError();
            _createPanel.SetActive(true);
            _roomNameInput.text = string.Empty;
            _state = "create";
            Select(_roomNameInput.gameObject);
            _roomNameInput.ActivateInputField();
        }

        public void CloseCreatePanel()
        {
            _createPanel.SetActive(false);
            _menuPanel.SetActive(true);
            _state = "menu";
            Select(_createButton.gameObject);
        }

        public void ConfirmCreate()
        {
            AccountLobbyPanel panel = AccountLobbyPanel.Instance;
            if (panel != null)
            {
                panel.CreateRoomFromTitle(_roomNameInput.text);
            }
        }

        /// <summary>검증 도구가 실제 포인터 경로로 누를 조작 요소. 화면에 없으면 null.</summary>
        public Selectable FindControl(string id)
        {
            switch (id)
            {
                case "title.prompt": return _promptButton;
                case "title.find": return _findButton;
                case "title.create": return _createButton;
                case "title.quit": return _quitButton;
                case "title.create.input": return _roomNameInput;
                case "title.create.confirm": return _createConfirmButton;
                case "title.create.cancel": return _createCancelButton;
                default: return null;
            }
        }

        private void ShowPrompt()
        {
            _pressAnyKeyPanel.SetActive(true);
            _menuPanel.SetActive(false);
            _state = "prompt";
        }

        private void RenderStatus()
        {
            string status;
            string identity = string.Empty;
            if (!LauncherGameSession.IsFrontendMode)
            {
                status = "런처에서 로그인한 뒤 게임을 실행해 주세요.";
            }
            else
            {
                status = AccountLobbyPanel.Instance == null ? string.Empty : AccountLobbyPanel.Instance.StatusMessage;
                if (!string.IsNullOrWhiteSpace(LauncherGameSession.Nickname))
                {
                    identity = LauncherGameSession.Nickname + " 님";
                }
            }
            if (status != _shownStatus)
            {
                _shownStatus = status;
                _statusText.text = status;
            }
            if (identity != _shownIdentity)
            {
                _shownIdentity = identity;
                _identityText.text = identity;
            }
        }

        private static void Select(GameObject target)
        {
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(target);
            }
        }

        private void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private bool HasRequiredReferences()
        {
            bool complete = (_pressAnyKeyPanel != null) && (_promptButton != null) && (_menuPanel != null) &&
                (_findButton != null) && (_createButton != null) && (_quitButton != null) &&
                (_createPanel != null) && (_roomNameInput != null) && (_createConfirmButton != null) &&
                (_createCancelButton != null) && (_statusText != null) && (_identityText != null);
            if (!complete)
            {
                Debug.LogError("[AccountTitleMenu] 화면 참조가 빠졌다", this);
            }
            return complete;
        }
    }
}
