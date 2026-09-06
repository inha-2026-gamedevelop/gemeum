// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

#if !DISABLESTEAMWORKS
// 서드파티
using Steamworks;
#endif

namespace Geumeum.Network
{
    /// <summary>
    /// Steamworks 로비 API와 콜백의 유일한 창구. CSteamID와 콜백 구조체가 여기서 끝나고
    /// 바깥에는 ulong과 문자열만 나간다, 그래야 로비 위의 코드가 KCP 경로에서도 그대로 돈다
    /// </summary>
    public class SteamLobbyService : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        /// <summary> 정원. 설계안 6.1의 최대 3명 </summary>
        public const int MAX_MEMBERS = 3;

        /****************************************
        *                Fields
        ****************************************/

        private readonly List<ulong> _members = new List<ulong>();

        /****************************************
        *              Properties
        ****************************************/

        /// <summary>
        /// 씬에 하나만 둔다. 인증기와 접속 연결부가 찾아 쓴다.
        /// 구독자의 OnEnable이 이 컴포넌트의 Awake보다 먼저 돌 수 있어, 아직 비어 있으면 한 번 찾아 채운다
        /// </summary>
        public static SteamLobbyService Instance
        {
            get
            {
                if ((_instance == null) && (!_searched))
                {
                    _searched = true;
                    _instance = FindAnyObjectByType<SteamLobbyService>(FindObjectsInactive.Include);
                }

                return _instance;
            }

            private set
            {
                _instance = value;
                _searched = true;
            }
        }

        private static SteamLobbyService _instance;
        private static bool              _searched;

        /// <summary> 현재 들어가 있는 로비의 SteamID. 없으면 0 </summary>
        public ulong LobbyId { get; private set; } = 0UL;

        /// <summary> 로비에 들어가 있는가 </summary>
        public bool IsInLobby => (LobbyId != 0UL);

        /// <summary> 로비 소유자의 SteamID. 없으면 0 </summary>
        public ulong OwnerId { get; private set; } = 0UL;

        /// <summary> 현재 멤버 목록. 호출자가 바꾸지 못하게 읽기 전용으로 준다 </summary>
        public IReadOnlyList<ulong> Members => _members;

        /****************************************
        *                Events
        ****************************************/

        /// <summary> 로비에 들어갔다. 생성과 참가 모두 이 경로로 끝난다 </summary>
        public event Action<ulong> LobbyEntered;

        /// <summary> 로비를 떠났거나 생성, 참가에 실패했다. 사유 문자열이 따라온다 </summary>
        public event Action<string> LobbyLeft;

        /// <summary> 멤버가 들어오거나 나갔다 </summary>
        public event Action MembersChanged;

        /// <summary> 공용 데이터나 소유자가 바뀌었다. Steam은 소유권 이전도 이 콜백으로 알린다 </summary>
        public event Action LobbyDataChanged;

        /// <summary> 호스트가 SetLobbyGameServer로 접속 신호를 보냈다. 인자는 호스트 SteamID </summary>
        public event Action<ulong> GameServerSet;

        /// <summary> 초대 수락이나 친구 목록에서 참가 요청이 왔다. 인자는 로비 SteamID </summary>
        public event Action<ulong> JoinRequested;

#if !DISABLESTEAMWORKS

        /// <summary> 내가 소유자인가. 공용 데이터는 소유자만 쓸 수 있다 </summary>
        public bool IsOwner => (IsInLobby) && (OwnerId != 0UL)
            && SteamLocalUser.TryGetSteamId(out ulong me) && (me == OwnerId);

        /// <summary>
        /// Steam 오버레이를 쓸 수 있는가. Steam이 실행한 프로세스가 아니면 주입되지 않아
        /// 초대 창이 조용히 열리지 않는다. 이때는 로비 ID 직접 참가로 우회한다
        /// </summary>
        public bool IsOverlayEnabled => SteamManager.Initialized && SteamUtils.IsOverlayEnabled();

        private CallResult<LobbyCreated_t> _lobbyCreated;
        private CallResult<LobbyEnter_t>   _lobbyEnterResult;

        private Callback<LobbyEnter_t>             _lobbyEnter;
        private Callback<LobbyChatUpdate_t>        _lobbyChatUpdate;
        private Callback<LobbyDataUpdate_t>        _lobbyDataUpdate;
        private Callback<LobbyGameCreated_t>       _lobbyGameCreated;
        private Callback<GameLobbyJoinRequested_t> _joinRequested;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if ((_instance != null) && (_instance != this))
            {
                Debug.LogError("[SteamLobbyService] 씬에 둘 이상 있다, 이 컴포넌트를 지운다", this);
                Destroy(this);
                return;
            }

            Instance = this;
        }

        // 콜백은 등록한 채로 두면 비활성 상태에서도 불리므로 활성 구간에만 잡아 둔다
        private void OnEnable()
        {
            if (!SteamManager.Initialized)
            {
                Debug.LogWarning("[SteamLobbyService] Steam이 초기화되지 않아 로비 기능이 동작하지 않는다", this);
                return;
            }

            _lobbyCreated     = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            _lobbyEnterResult = CallResult<LobbyEnter_t>.Create(OnLobbyEnterResult);

            _lobbyEnter       = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            _lobbyChatUpdate  = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
            _lobbyDataUpdate  = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdate);
            _lobbyGameCreated = Callback<LobbyGameCreated_t>.Create(OnLobbyGameCreated);
            _joinRequested    = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
        }

        private void OnDisable()
        {
            _lobbyCreated?.Dispose();
            _lobbyEnterResult?.Dispose();
            _lobbyEnter?.Dispose();
            _lobbyChatUpdate?.Dispose();
            _lobbyDataUpdate?.Dispose();
            _lobbyGameCreated?.Dispose();
            _joinRequested?.Dispose();

            _lobbyCreated     = null;
            _lobbyEnterResult = null;
            _lobbyEnter       = null;
            _lobbyChatUpdate  = null;
            _lobbyDataUpdate  = null;
            _lobbyGameCreated = null;
            _joinRequested    = null;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                _searched = false;
            }
        }

        /****************************************
        *              Public API
        ****************************************/

        /// <summary>
        /// 로비를 만든다. AppID 480으로 개발하는 동안 Public은 쓰지 않는다 -
        /// 다른 개발자의 Spacewar 로비와 섞인다
        /// </summary>
        public void CreateLobby()
        {
            if (!EnsureReady())
            {
                return;
            }

            SteamAPICall_t call = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePrivate, MAX_MEMBERS);
            _lobbyCreated.Set(call);
        }

        /// <summary> 로비에 참가한다. 초대 수락과 +connect_lobby 둘 다 이 경로로 들어온다 </summary>
        public void JoinLobby(ulong lobbyId)
        {
            if (!EnsureReady())
            {
                return;
            }

            if (lobbyId == 0UL)
            {
                Debug.LogWarning("[SteamLobbyService] 참가할 로비 ID가 0이다", this);
                return;
            }

            Debug.Log($"[SteamLobbyService] 로비 참가를 요청한다: {lobbyId}");

            SteamAPICall_t call = SteamMatchmaking.JoinLobby(new CSteamID(lobbyId));
            _lobbyEnterResult.Set(call);
        }

        /// <summary> 로비를 떠난다. 이미 나가 있으면 아무것도 하지 않는다 </summary>
        public void LeaveLobby()
        {
            if ((!IsInLobby) || (!SteamManager.Initialized))
            {
                return;
            }

            SteamMatchmaking.LeaveLobby(new CSteamID(LobbyId));
            ClearLobby("직접 떠났다");
        }

        /// <summary> Steam 오버레이의 친구 초대 창을 연다 </summary>
        public void OpenInviteOverlay()
        {
            if ((!IsInLobby) || (!SteamManager.Initialized))
            {
                Debug.LogWarning("[SteamLobbyService] 로비에 들어가 있지 않아 초대 창을 열 수 없다", this);
                return;
            }

            // 오버레이가 없으면 이 호출은 실패도 예외도 없이 그냥 아무 일도 하지 않는다.
            // 원인을 못 찾고 헤매게 되므로 여기서 미리 알린다
            if (!IsOverlayEnabled)
            {
                Debug.LogWarning("[SteamLobbyService] Steam 오버레이를 쓸 수 없어 초대 창이 열리지 않는다. "
                    + $"로비 ID({LobbyId})를 상대에게 알려 직접 참가하게 할 것", this);
                return;
            }

            SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(LobbyId));
        }

        /// <summary>
        /// 친구에게 로비 초대를 직접 보낸다. 오버레이 없이 동작하므로
        /// 오버레이가 주입되지 않는 개발 실행에서도 초대 경로를 검증할 수 있다.
        /// 받는 쪽은 Steam 클라이언트 알림으로 받고, 수락하면 게임이 켜져 있으면 참가 콜백이,
        /// 꺼져 있으면 +connect_lobby 인자로 실행된다
        /// </summary>
        public bool InviteUser(ulong friendSteamId)
        {
            if ((!IsInLobby) || (!SteamManager.Initialized))
            {
                Debug.LogWarning("[SteamLobbyService] 로비에 들어가 있지 않아 초대할 수 없다", this);
                return false;
            }

            if (friendSteamId == 0UL)
            {
                Debug.LogWarning("[SteamLobbyService] 초대할 SteamID가 0이다", this);
                return false;
            }

            bool sent = SteamMatchmaking.InviteUserToLobby(new CSteamID(LobbyId), new CSteamID(friendSteamId));
            if (sent)
            {
                Debug.Log($"[SteamLobbyService] 초대를 보냈다: {friendSteamId}");
            }
            else
            {
                Debug.LogWarning($"[SteamLobbyService] 초대 전송에 실패했다: {friendSteamId}. 친구 관계인지 확인할 것", this);
            }

            return sent;
        }

        /// <summary> 공용 데이터를 읽는다. 없으면 빈 문자열 </summary>
        public string GetData(string key)
        {
            return IsInLobby ? SteamMatchmaking.GetLobbyData(new CSteamID(LobbyId), key) : string.Empty;
        }

        /// <summary> 공용 데이터를 쓴다. 소유자만 성공한다 </summary>
        public bool SetData(string key, string value)
        {
            if (!IsInLobby)
            {
                return false;
            }

            if (!IsOwner)
            {
                Debug.LogWarning($"[SteamLobbyService] 소유자가 아니라 공용 데이터를 쓸 수 없다: {key}", this);
                return false;
            }

            return SteamMatchmaking.SetLobbyData(new CSteamID(LobbyId), key, value);
        }

        /// <summary> 다른 멤버의 데이터를 읽는다 </summary>
        public string GetMemberData(ulong memberId, string key)
        {
            return IsInLobby
                ? SteamMatchmaking.GetLobbyMemberData(new CSteamID(LobbyId), new CSteamID(memberId), key)
                : string.Empty;
        }

        /// <summary> 자기 멤버 데이터를 쓴다. 자기 값만 쓸 수 있다 </summary>
        public void SetMemberData(string key, string value)
        {
            if (!IsInLobby)
            {
                return;
            }

            SteamMatchmaking.SetLobbyMemberData(new CSteamID(LobbyId), key, value);
        }

        /// <summary>
        /// 접속 신호를 보낸다. 전 멤버에게 LobbyGameCreated_t가 가므로 공용 데이터 폴링보다 깔끔하다.
        /// 공용 데이터의 host는 늦게 들어온 사람의 복구용으로 따로 쓴다
        /// </summary>
        public void SetGameServer(ulong hostSteamId)
        {
            if ((!IsInLobby) || (!SteamManager.Initialized))
            {
                return;
            }

            SteamMatchmaking.SetLobbyGameServer(new CSteamID(LobbyId), 0U, 0, new CSteamID(hostSteamId));
        }

        /// <summary> 이 SteamID가 현재 로비 멤버인가. 인증기가 로비 멤버십 검증에 쓴다 </summary>
        public bool IsMember(ulong steamId)
        {
            return _members.Contains(steamId);
        }

        /// <summary> 우리 게임의 로비이고 데이터 계약이 같은가. 480 공유 로비를 여기서 거른다 </summary>
        public bool IsGeumeumLobby()
        {
            return string.Equals(GetData(SteamLobbyKeys.GAME), SteamLobbyKeys.GAME_VALUE, StringComparison.Ordinal)
                && string.Equals(GetData(SteamLobbyKeys.SCHEMA), SteamLobbyKeys.SCHEMA_VALUE, StringComparison.Ordinal);
        }

        /****************************************
        *              Callbacks
        ****************************************/

        private void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
        {
            if ((ioFailure) || (result.m_eResult != EResult.k_EResultOK))
            {
                string reason = ioFailure ? "Steam 통신 실패" : result.m_eResult.ToString();
                Debug.LogError($"[SteamLobbyService] 로비 생성 실패: {reason}", this);
                LobbyLeft?.Invoke(reason);
                return;
            }

            // 소유자만 쓸 수 있는 값이라 생성 직후 여기서 채운다. 참가자는 이 값을 보고 우리 로비인지 판단한다
            EnterLobby(result.m_ulSteamIDLobby);

            SetData(SteamLobbyKeys.GAME,   SteamLobbyKeys.GAME_VALUE);
            SetData(SteamLobbyKeys.SCHEMA, SteamLobbyKeys.SCHEMA_VALUE);
            SetData(SteamLobbyKeys.BUILD,  Application.version);
            SetData(SteamLobbyKeys.STATE,  SteamLobbyKeys.State.GATHERING);
            SetData(SteamLobbyKeys.EPOCH,  "0");
        }

        private void OnLobbyEnterResult(LobbyEnter_t result, bool ioFailure)
        {
            if (ioFailure)
            {
                Debug.LogError("[SteamLobbyService] 로비 참가 실패: Steam 통신 실패", this);
                LobbyLeft?.Invoke("Steam 통신 실패");
                return;
            }

            OnLobbyEnter(result);
        }

        private void OnLobbyEnter(LobbyEnter_t result)
        {
            if (result.m_EChatRoomEnterResponse != (uint) EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                var response = (EChatRoomEnterResponse) result.m_EChatRoomEnterResponse;
                Debug.LogError($"[SteamLobbyService] 로비 참가 거부: {response}", this);
                LobbyLeft?.Invoke(response.ToString());
                return;
            }

            EnterLobby(result.m_ulSteamIDLobby);
        }

        private void OnLobbyChatUpdate(LobbyChatUpdate_t result)
        {
            if (result.m_ulSteamIDLobby != LobbyId)
            {
                return;
            }

            RefreshMembers();
            MembersChanged?.Invoke();
        }

        // Steam은 소유권 이전에 별도 콜백을 주지 않는다, 이 콜백에서 소유자를 다시 읽어야 안다
        private void OnLobbyDataUpdate(LobbyDataUpdate_t result)
        {
            if (result.m_ulSteamIDLobby != LobbyId)
            {
                return;
            }

            RefreshOwner();
            LobbyDataChanged?.Invoke();
        }

        private void OnLobbyGameCreated(LobbyGameCreated_t result)
        {
            if (result.m_ulSteamIDLobby != LobbyId)
            {
                return;
            }

            GameServerSet?.Invoke(result.m_ulSteamIDGameServer);
        }

        private void OnJoinRequested(GameLobbyJoinRequested_t result)
        {
            JoinRequested?.Invoke(result.m_steamIDLobby.m_SteamID);
        }

        /****************************************
        *                Methods
        ****************************************/

        private void EnterLobby(ulong lobbyId)
        {
            // 방을 만들면 LobbyCreated_t와 LobbyEnter_t가 같은 입장에 대해 각각 들어온다.
            // 그대로 두면 LobbyEntered가 두 번 발행되어 구독자가 두 번 반응한다
            if (LobbyId == lobbyId)
            {
                RefreshOwner();
                RefreshMembers();
                return;
            }

            LobbyId = lobbyId;

            RefreshOwner();
            RefreshMembers();

            Debug.Log($"[SteamLobbyService] 로비 입장: lobbyId={LobbyId}, 소유자={OwnerId}, 인원={_members.Count}");
            LobbyEntered?.Invoke(LobbyId);
        }

        private void ClearLobby(string reason)
        {
            LobbyId = 0UL;
            OwnerId = 0UL;
            _members.Clear();

            Debug.Log($"[SteamLobbyService] 로비 이탈: {reason}");
            LobbyLeft?.Invoke(reason);
        }

        private void RefreshOwner()
        {
            OwnerId = IsInLobby ? SteamMatchmaking.GetLobbyOwner(new CSteamID(LobbyId)).m_SteamID : 0UL;
        }

        private void RefreshMembers()
        {
            _members.Clear();

            if (!IsInLobby)
            {
                return;
            }

            var lobby = new CSteamID(LobbyId);
            int count = SteamMatchmaking.GetNumLobbyMembers(lobby);

            for (int i = 0; i < count; ++i)
            {
                _members.Add(SteamMatchmaking.GetLobbyMemberByIndex(lobby, i).m_SteamID);
            }
        }

        private bool EnsureReady()
        {
            if (!SteamManager.Initialized)
            {
                Debug.LogError("[SteamLobbyService] Steam이 초기화되지 않았다, Steam 클라이언트 실행과 steam_appid.txt를 확인할 것", this);
                return false;
            }

            return true;
        }

#else

        /// <summary> DISABLESTEAMWORKS 빌드에는 로비가 없으므로 항상 false </summary>
        public bool IsOwner => false;

        /// <summary> DISABLESTEAMWORKS 빌드에는 오버레이가 없다 </summary>
        public bool IsOverlayEnabled => false;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                _searched = false;
            }
        }

        /****************************************
        *              Public API
        ****************************************/

        // 호출자가 컴파일만 되게 두고 아무 일도 하지 않는다. 경로 분기는 부트 게이트가 이미 막는다

        public void CreateLobby()                               { WarnDisabled(); }
        public void JoinLobby(ulong lobbyId)                    { WarnDisabled(); }
        public void LeaveLobby()                                { }
        public void OpenInviteOverlay()                         { WarnDisabled(); }
        public string GetData(string key)                       { return string.Empty; }
        public bool SetData(string key, string value)           { return false; }
        public string GetMemberData(ulong memberId, string key) { return string.Empty; }
        public void SetMemberData(string key, string value)     { }
        public void SetGameServer(ulong hostSteamId)            { }
        public bool InviteUser(ulong friendSteamId)             { WarnDisabled(); return false; }
        public bool IsMember(ulong steamId)                     { return false; }
        public bool IsGeumeumLobby()                            { return false; }

        private void WarnDisabled()
        {
            Debug.LogWarning("[SteamLobbyService] DISABLESTEAMWORKS 빌드라 로비 기능이 없다", this);
        }

#endif
    }
}
