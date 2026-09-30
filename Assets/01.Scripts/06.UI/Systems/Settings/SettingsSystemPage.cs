// Unity
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using Mirror;

// Project
using Geumeum.Network;

namespace Geumeum.UI
{
    /// <summary>
    /// 시스템 탭. 로비로 돌아가기와 게임 종료를 누르면 바로 떠나지 않고 오른쪽에 확인을 띄운다.
    /// 코옵 런 중에 나가면 파티 전체가 영향을 받기 때문이다 — 손님이 빠지면 런이 멈추고, 방장이 빠지면 방이 닫힌다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsSystemPage : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private enum PendingAction
        {
            None        = 0,
            ReturnLobby = 1,
            Quit        = 2,
        }

        private const string CONFIRM_LOBBY_AS_HOST =
            "로비로 돌아갑니다.\n방장이 나가면 방이 닫히고 파티원 모두 로비로 돌아갑니다.";
        private const string CONFIRM_LOBBY_AS_GUEST =
            "로비로 돌아갑니다.\n진행 중인 원정은 당신이 돌아올 때까지 멈춥니다.";
        private const string CONFIRM_QUIT_AS_HOST =
            "게임을 종료합니다.\n방장이 나가면 방이 닫히고 파티원 모두 로비로 돌아갑니다.";
        private const string CONFIRM_QUIT_AS_GUEST =
            "게임을 종료합니다.\n진행 중인 원정은 당신이 돌아올 때까지 멈춥니다.";
        private const string CONFIRM_QUIT_OFFLINE = "게임을 종료합니다.";

        [Header("항목")]
        [SerializeField] private Button _returnLobbyButton;
        [SerializeField] private Button _quitButton;

        [Tooltip("로비 복귀를 못 쓸 때 줄 전체를 흐리게 할 그룹. 버튼 색 전환은 배경만 바꿔 글자가 그대로 남는다")]
        [SerializeField] private CanvasGroup _returnLobbyGroup;
        [SerializeField, Range(0f, 1f)] private float _unavailableAlpha = 0.4f;

        [Header("확인")]
        [Tooltip("항목을 누르면 켜지는 오른쪽 확인 영역")]
        [SerializeField] private GameObject _confirmRoot;
        [Tooltip("확인 문구의 첫 줄을 제목으로 띄울 글자. 비워 두면 문구 전체를 본문 글자 하나에 띄운다")]
        [SerializeField] private TMP_Text   _confirmTitleText;
        [SerializeField] private TMP_Text   _confirmText;
        [SerializeField] private Button     _confirmButton;
        [SerializeField] private Button     _cancelButton;

        private PendingAction _pending = PendingAction.None;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if ((_returnLobbyButton == null) || (_quitButton == null) || (_confirmRoot == null) ||
                (_confirmText == null) || (_confirmButton == null) || (_cancelButton == null))
            {
                Debug.LogError("[SettingsSystemPage] 버튼이나 확인 영역 참조가 비어 있다", this);
                return;
            }

            _returnLobbyButton.onClick.AddListener(AskReturnLobby);
            _quitButton.onClick.AddListener(AskQuit);
            _confirmButton.onClick.AddListener(Confirm);
            _cancelButton.onClick.AddListener(Cancel);
        }

        // 탭을 넘기거나 창을 닫았다 다시 열면 확인이 남아 있지 않게 한다
        private void OnEnable()
        {
            Refresh();
        }

        private void OnDestroy()
        {
            if (_returnLobbyButton != null)
            {
                _returnLobbyButton.onClick.RemoveListener(AskReturnLobby);
            }

            if (_quitButton != null)
            {
                _quitButton.onClick.RemoveListener(AskQuit);
            }

            if (_confirmButton != null)
            {
                _confirmButton.onClick.RemoveListener(Confirm);
            }

            if (_cancelButton != null)
            {
                _cancelButton.onClick.RemoveListener(Cancel);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 확인을 거두고 지금 연결 상태로 버튼을 다시 연다. 창을 열 때 부른다 </summary>
        public void Refresh()
        {
            SetPending(PendingAction.None);

            // 로비 복귀는 계정 경로에만 있다. 개발용 직접 연결에서는 눌러도 아무 일도 일어나지 않는다
            bool canReturn = LauncherGameSession.IsFrontendMode;
            if (_returnLobbyButton != null)
            {
                _returnLobbyButton.interactable = canReturn;
            }

            if (_returnLobbyGroup != null)
            {
                _returnLobbyGroup.alpha = canReturn ? 1f : _unavailableAlpha;
            }
        }

        private void AskReturnLobby()
        {
            SetPending(PendingAction.ReturnLobby);
        }

        private void AskQuit()
        {
            SetPending(PendingAction.Quit);
        }

        private void Cancel()
        {
            SetPending(PendingAction.None);
        }

        private void Confirm()
        {
            PendingAction action = _pending;
            SetPending(PendingAction.None);

            switch (action)
            {
                case PendingAction.ReturnLobby:
                    LauncherGameSession.ReturnToLobby();
                    break;

                case PendingAction.Quit:
                    Quit();
                    break;
            }
        }

        private void SetPending(PendingAction action)
        {
            _pending = action;

            if (_confirmRoot != null)
            {
                _confirmRoot.SetActive(action != PendingAction.None);
            }

            if ((_confirmText != null) && (action != PendingAction.None))
            {
                ShowConfirmText(BuildConfirmText(action));
            }
        }

        /// <summary> 첫 줄은 무엇을 하는지, 나머지는 파티에 미치는 영향이다. 제목 글자가 있으면 둘을 나눠 띄운다 </summary>
        private void ShowConfirmText(string message)
        {
            if (_confirmTitleText == null)
            {
                _confirmText.text = message;
                return;
            }

            int lineEnd = message.IndexOf('\n');
            string title = (lineEnd < 0) ? message : message.Substring(0, lineEnd);
            string body  = (lineEnd < 0) ? string.Empty : message.Substring(lineEnd + 1);

            // 제목 자리에는 마침표를 찍지 않는다
            _confirmTitleText.text = title.TrimEnd('.');
            _confirmText.text      = FunctionalUiText.KeepWordsTogether(body);
        }

        private static string BuildConfirmText(PendingAction action)
        {
            bool isOnline = NetworkClient.active || NetworkServer.active;
            bool isHost   = NetworkServer.active;

            if (action == PendingAction.ReturnLobby)
            {
                return isHost ? CONFIRM_LOBBY_AS_HOST : CONFIRM_LOBBY_AS_GUEST;
            }

            if (!isOnline)
            {
                return CONFIRM_QUIT_OFFLINE;
            }

            return isHost ? CONFIRM_QUIT_AS_HOST : CONFIRM_QUIT_AS_GUEST;
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
