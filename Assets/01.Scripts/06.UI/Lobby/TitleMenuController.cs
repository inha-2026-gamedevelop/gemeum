// Unity
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Geumeum.UI
{
    /// <summary>
    /// 시작화면 메뉴의 조작과 각 항목의 동작
    /// 방향키 이동과 Enter 확인은 EventSystem이 처리하고, 여기서는 확인키 G만 더한다
    /// 네트워크를 붙이기 전이라 참가 주소는 받기만 하고 쓰지 않는다
    /// </summary>
    public class TitleMenuController : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const KeyCode KEY_CONFIRM = KeyCode.G;

        [Header("메뉴 항목")]
        [SerializeField] private Button _hostButton;
        [SerializeField] private Button _joinButton;
        [SerializeField] private Button _quitButton;

        [Header("참가 입력")]
        [SerializeField] private GameObject     _joinPanel;
        [SerializeField] private TMP_InputField _joinAddressInput;
        [SerializeField] private Button         _joinConfirmButton;
        [SerializeField] private Button         _joinCancelButton;

        [Header("이동할 씬")]
        [SerializeField] private string _lobbySceneName = "Dev_Lobby";

        private int _enabledFrame;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (HasMissingReference())
            {
                enabled = false;
                return;
            }

            _hostButton.onClick.AddListener(HostGame);
            _joinButton.onClick.AddListener(OpenJoinPanel);
            _quitButton.onClick.AddListener(QuitGame);

            _joinConfirmButton.onClick.AddListener(ConfirmJoin);
            _joinCancelButton.onClick.AddListener(CloseJoinPanel);

            _joinPanel.SetActive(false);
        }

        private void OnEnable()
        {
            _enabledFrame = Time.frameCount;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KEY_CONFIRM) == false)
            {
                return;
            }

            // 메뉴를 여는 키가 G였다면 같은 프레임에 첫 항목까지 눌려버린다
            if (Time.frameCount == _enabledFrame)
            {
                return;
            }

            // 주소를 입력하는 중에는 G가 글자여야 한다
            if (IsTypingAddress())
            {
                return;
            }

            SubmitSelected();
        }

        /****************************************
        *                Methods
        ****************************************/

        public void HostGame()
        {
            LoadLobby();
        }

        public void OpenJoinPanel()
        {
            _joinPanel.SetActive(true);
            _joinAddressInput.text = string.Empty;

            Select(_joinAddressInput.gameObject);
            _joinAddressInput.ActivateInputField();
        }

        public void CloseJoinPanel()
        {
            _joinPanel.SetActive(false);

            Select(_joinButton.gameObject);
        }

        // 네트워크를 붙이기 전이라 입력값을 검사하지 않는다. 비어 있지 않기만 하면 넘어간다
        public void ConfirmJoin()
        {
            if (string.IsNullOrWhiteSpace(_joinAddressInput.text))
            {
                return;
            }

            LoadLobby();
        }

        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // 씬 이동은 여기 한 곳에서만 한다
        // 네트워크를 붙일 때 로비는 씬이 아니라 패널이 되므로 이 메서드만 갈아끼운다
        private void LoadLobby()
        {
            SceneManager.LoadScene(_lobbySceneName);
        }

        /****************************************
        *                Utility
        ****************************************/

        private bool HasMissingReference()
        {
            if (_hostButton == null || _joinButton == null || _quitButton == null)
            {
                Debug.LogError("[TitleMenuController] 메뉴 버튼이 연결되지 않았다", this);
                return true;
            }

            if (_joinPanel == null || _joinAddressInput == null || _joinConfirmButton == null || _joinCancelButton == null)
            {
                Debug.LogError("[TitleMenuController] 참가 입력 요소가 연결되지 않았다", this);
                return true;
            }

            return false;
        }

        private bool IsTypingAddress()
        {
            return _joinPanel.activeSelf && _joinAddressInput.isFocused;
        }

        private void Select(GameObject target)
        {
            if (EventSystem.current == null)
            {
                return;
            }

            EventSystem.current.SetSelectedGameObject(target);
        }

        // Enter와 같은 경로로 보내야 항목마다 확인키를 따로 잇지 않아도 된다
        private void SubmitSelected()
        {
            if (EventSystem.current == null)
            {
                return;
            }

            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected == null)
            {
                return;
            }

            ExecuteEvents.Execute(selected, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
        }
    }
}
