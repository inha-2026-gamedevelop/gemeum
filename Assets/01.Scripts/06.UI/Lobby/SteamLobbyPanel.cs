// System
using System.Collections;

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
    /// <summary>
    /// Steam 로비 조작 화면.
    ///
    /// 방을 만들거나 로비 ID로 참가하고, 정원이 다 차면 소유자가 호스트를 연다.
    /// 상태는 SteamSessionCoordinator와 SteamLobbyService가 들고 있고 여기서는 읽기만 한다 -
    /// 화면이 원본을 따로 기억하면 두 값이 어긋나는 순간을 아무도 못 잡는다.
    /// </summary>
    public class SteamLobbyPanel : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        // 런 단계는 클라이언트로 SyncVar 훅만 오고 구독할 이벤트가 없다.
        // 로비 화면 하나 갱신하는 데 매 프레임을 쓸 이유는 없어 낮은 주기로만 확인한다
        private const float PHASE_POLL_SECONDS = 0.25f;

        /****************************************
        *                Fields
        ****************************************/

        [Header("패널")]
        [SerializeField] private GameObject _root;

        [Header("상태 표시")]
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private TMP_Text _memberText;
        [SerializeField] private TMP_Text _lobbyIdText;
        [SerializeField] private TMP_Text _hintText;

        [Header("방")]
        [SerializeField] private Button _createButton;
        [SerializeField] private Button _copyIdButton;
        [SerializeField] private Button _leaveButton;

        [Header("참가")]
        [SerializeField] private TMP_InputField _joinInput;
        [SerializeField] private Button _joinButton;
        [SerializeField] private Button _pasteButton;

        [Header("시작")]
        [SerializeField] private Button _startButton;

        private SteamLobbyService _lobby;
        private bool              _isSubscribed;

        // 화면이 떠 있는 동안 커서와 게임 입력을 가진다.
        // 이게 없으면 뒤에 선 플레이어가 커서를 잠가 ESC를 눌러야만 버튼이 눌린다
        private LocalGameplayInputToken _inputToken;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Start()
        {
            if (HasMissingReference())
            {
                enabled = false;
                return;
            }

            EnsureEventSystem();

            _createButton.onClick.AddListener(CreateLobby);
            _copyIdButton.onClick.AddListener(CopyLobbyId);
            _leaveButton.onClick.AddListener(LeaveLobby);
            _joinButton.onClick.AddListener(JoinTypedLobby);
            _pasteButton.onClick.AddListener(PasteLobbyId);
            _startButton.onClick.AddListener(StartHost);

            Subscribe();
            Refresh();

            StartCoroutine(CoWatchRunPhase());
        }

        /// <summary>
        /// CameraRig가 매 프레임 커서를 다시 잠그므로 그 뒤에 한 번 더 되돌린다.
        /// Maintain은 토큰이 살아 있을 때만 동작해 다른 모달과 겹쳐도 안전하다
        /// </summary>
        private void LateUpdate()
        {
            LocalGameplayInputGate.Maintain(_inputToken);
        }

        private void OnDestroy()
        {
            Unsubscribe();
            ReleaseInput();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// uGUI 클릭을 받으려면 씬에 EventSystem이 있어야 한다.
        ///
        /// 출시 씬(Boot, Field, Result ...)에는 EventSystem이 없고 Dev_HUD와 Dev_Title에만 있다.
        /// 없으면 버튼도 입력칸도 눌리지 않으므로 - 화면은 보이는데 아무 반응이 없다 -
        /// 여기서 하나 만든다. 씬 파일을 고치면 머지가 안 되므로 실행 중에 만든다.
        ///
        /// 씬을 넘어가도 계속 필요해 DontDestroyOnLoad로 남긴다.
        /// 이미 있으면 만들지 않는다 - EventSystem이 둘이면 Unity가 경고를 낸다
        /// </summary>
        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var host = new GameObject("EventSystem(자동 생성)");
            host.AddComponent<EventSystem>();

            // 이 프로젝트는 레거시 Input(activeInputHandler=0)을 쓴다
            host.AddComponent<StandaloneInputModule>();

            Object.DontDestroyOnLoad(host);
            Debug.Log("[SteamLobbyPanel] 씬에 EventSystem이 없어 하나 만들었다");
        }

        /// <summary> 화면을 열고 닫으면서 커서 소유권도 같이 넘긴다 </summary>
        private void SetPanelVisible(bool isVisible)
        {
            if (_root.activeSelf != isVisible)
            {
                _root.SetActive(isVisible);
            }

            if (isVisible)
            {
                if (_inputToken == null)
                {
                    _inputToken = LocalGameplayInputGate.Acquire();
                }

                return;
            }

            ReleaseInput();
        }

        private void ReleaseInput()
        {
            if (_inputToken == null)
            {
                return;
            }

            _inputToken.Dispose();
            _inputToken = null;
        }

        private bool HasMissingReference()
        {
            if ((_root == null) || (_statusText == null) || (_memberText == null) ||
                (_lobbyIdText == null) || (_hintText == null) || (_createButton == null) ||
                (_copyIdButton == null) || (_leaveButton == null) || (_joinInput == null) ||
                (_joinButton == null) || (_pasteButton == null) || (_startButton == null))
            {
                Debug.LogError("[SteamLobbyPanel] 화면 참조가 연결되지 않았다", this);
                return true;
            }

            return false;
        }

        private void Subscribe()
        {
            _lobby = SteamLobbyService.Instance;
            if (_lobby == null)
            {
                return;
            }

            _lobby.LobbyEntered   += OnLobbyEntered;
            _lobby.LobbyLeft      += OnLobbyLeft;
            _lobby.MembersChanged += Refresh;
            _lobby.GameServerSet  += OnGameServerSet;

            SteamSessionCoordinator session = SteamSessionCoordinator.Instance;
            if (session != null)
            {
                session.StateChanged += OnSessionStateChanged;
            }

            _isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_isSubscribed)
            {
                return;
            }

            if (_lobby != null)
            {
                _lobby.LobbyEntered   -= OnLobbyEntered;
                _lobby.LobbyLeft      -= OnLobbyLeft;
                _lobby.MembersChanged -= Refresh;
                _lobby.GameServerSet  -= OnGameServerSet;
            }

            SteamSessionCoordinator session = SteamSessionCoordinator.Instance;
            if (session != null)
            {
                session.StateChanged -= OnSessionStateChanged;
            }

            _isSubscribed = false;
        }

        private void OnLobbyEntered(ulong lobbyId) => Refresh();

        private void OnLobbyLeft(string reason) => Refresh();

        private void OnGameServerSet(ulong hostSteamId) => Refresh();

        private void OnSessionStateChanged(SteamSessionState state) => Refresh();

        /// <summary>
        /// 런이 시작되면 화면을 접는다.
        ///
        /// 단계 변화는 클라이언트에 SyncVar 훅으로만 오고 구독할 이벤트가 없어
        /// 여기서만 낮은 주기로 본다. 로비를 벗어나면 코루틴도 같이 끝낸다
        /// </summary>
        private IEnumerator CoWatchRunPhase()
        {
            var wait = new WaitForSeconds(PHASE_POLL_SECONDS);

            while (true)
            {
                yield return wait;

                if (IsRunInProgress())
                {
                    SetPanelVisible(false);
                    yield break;
                }

                Refresh();
            }
        }

        private static bool IsRunInProgress()
        {
            RunManager run = RunManager.LocalInstance;
            return ((run != null) && (run.Phase != RunPhase.Lobby));
        }

        private void Refresh()
        {
            if (!SteamTransportSelector.IsSteamTransport(Transport.active))
            {
                SetPanelVisible(false);
                return;
            }

            SteamSessionCoordinator session = SteamSessionCoordinator.Instance;
            SteamLobbyService       lobby   = SteamLobbyService.Instance;
            if ((session == null) || (lobby == null))
            {
                SetPanelVisible(true);
                _statusText.text = "세션 구성이 씬에 없다";
                _hintText.text   = "SessionNetwork 프리팹을 확인할 것";
                SetInteractable(false, false, false, false);
                return;
            }

            // 시작을 누른 뒤로는 접속과 런 시작이 자동으로 이어지므로 화면을 접는다.
            // 호스트를 열지 못해 Gathering으로 되돌아오면 다시 뜬다
            bool isPastLobby = ((session.State == SteamSessionState.StartingHost) ||
                                (session.State == SteamSessionState.Connecting) ||
                                (session.State == SteamSessionState.Ready));

            SetPanelVisible((!IsRunInProgress()) && (!isPastLobby));

            _statusText.text = $"단계: {session.State}";

            if (!lobby.IsInLobby)
            {
                _memberText.text  = "인원: -";
                _lobbyIdText.text = "로비: 없음";
                _hintText.text    = "방을 만들거나, 받은 로비 ID로 참가할 것";
                SetInteractable(true, true, false, false);
                return;
            }

            int members = lobby.Members.Count;
            _memberText.text  = $"인원: {members} / {SteamLobbyService.MAX_MEMBERS}";
            _lobbyIdText.text = $"로비: {lobby.LobbyId}";

            bool canStart = session.CanStartHost;
            _hintText.text = ResolveHint(lobby, members, canStart);

            SetInteractable(false, false, canStart, true);
        }

        private static string ResolveHint(SteamLobbyService lobby, int members, bool canStart)
        {
            if (canStart)
            {
                return "정원이 찼다. 시작을 누르면 게임이 시작된다";
            }

            if (!lobby.IsOwner)
            {
                return "방을 만든 사람만 시작할 수 있다";
            }

            int missing = SteamLobbyService.MAX_MEMBERS - members;
            return $"{missing}명을 더 기다린다. 로비 ID를 복사해 전달할 것";
        }

        private void SetInteractable(bool canCreate, bool canJoin, bool canStart, bool canLeave)
        {
            _createButton.interactable = canCreate;
            _joinButton.interactable   = canJoin;
            _pasteButton.interactable  = canJoin;
            _joinInput.interactable    = canJoin;
            _startButton.interactable  = canStart;
            _copyIdButton.interactable = canLeave;
            _leaveButton.interactable  = canLeave;
        }

        /****************************************
        *              Button Handler
        ****************************************/

        private void CreateLobby()
        {
            SteamSessionCoordinator session = SteamSessionCoordinator.Instance;
            if (session == null)
            {
                return;
            }

            Debug.Log("[SteamLobbyPanel] 방 만들기", this);
            session.CreateLobby();
        }

        private void JoinTypedLobby()
        {
            SteamSessionCoordinator session = SteamSessionCoordinator.Instance;
            if (session == null)
            {
                return;
            }

            string typed = (_joinInput.text == null) ? string.Empty : _joinInput.text.Trim();
            if (!ulong.TryParse(typed, out ulong lobbyId))
            {
                _hintText.text = "로비 ID가 숫자가 아니다";
                Debug.LogWarning($"[SteamLobbyPanel] 로비 ID를 읽지 못했다: \"{typed}\"", this);
                return;
            }

            Debug.Log($"[SteamLobbyPanel] 로비 {lobbyId}에 참가한다", this);
            session.JoinLobby(lobbyId);
        }

        /// <summary>
        /// 클립보드의 로비 ID를 입력칸에 넣는다.
        ///
        /// 메신저로 주고받으면 앞뒤 공백이나 줄바꿈이 섞여 들어오고, 그대로 넣으면
        /// 숫자로 읽히지 않는다. 숫자만 남기고 넣은 뒤 무엇이 들어갔는지 알린다
        /// </summary>
        private void PasteLobbyId()
        {
            string clipboard = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrWhiteSpace(clipboard))
            {
                _hintText.text = "클립보드가 비어 있다";
                return;
            }

            string digits = ExtractDigits(clipboard);
            if (digits.Length == 0)
            {
                _hintText.text = "클립보드에 숫자가 없다";
                return;
            }

            _joinInput.text = digits;
            _hintText.text  = $"로비 ID {digits}를 넣었다. 참가를 누를 것";
        }

        private static string ExtractDigits(string source)
        {
            var builder = new System.Text.StringBuilder(source.Length);
            for (int i = 0; i < source.Length; ++i)
            {
                if (char.IsDigit(source[i]))
                {
                    builder.Append(source[i]);
                }
            }

            return builder.ToString();
        }

        private void CopyLobbyId()
        {
            SteamLobbyService lobby = SteamLobbyService.Instance;
            if ((lobby == null) || (!lobby.IsInLobby))
            {
                return;
            }

            GUIUtility.systemCopyBuffer = lobby.LobbyId.ToString();
            _hintText.text = "로비 ID를 복사했다. 나머지 두 명에게 전달할 것";
        }

        private void StartHost()
        {
            SteamSessionCoordinator session = SteamSessionCoordinator.Instance;
            if (session == null)
            {
                return;
            }

            Debug.Log($"[SteamLobbyPanel] 호스트 시작 요청: 단계={session.State}, " +
                      $"가능={session.CanStartHost}", this);
            session.StartHost();
        }

        private void LeaveLobby()
        {
            SteamSessionCoordinator session = SteamSessionCoordinator.Instance;
            if (session == null)
            {
                return;
            }

            session.LeaveLobby();
        }
    }
}
