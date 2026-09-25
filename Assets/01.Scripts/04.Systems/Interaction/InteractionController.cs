// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    /// <summary>
    /// 플레이어의 상호작용 창구. 물건마다 Command를 만들지 않고 여기 하나만 둔다
    /// 클라이언트는 후보를 찾아 안내 문구를 띄우고 netId만 보내며, 판정은 전부 서버가 다시 한다
    /// 클라이언트가 보낸 후보는 "무엇을 하려 했는가"일 뿐 승인 근거가 아니다
    /// </summary>
    [DisallowMultipleComponent]
    public partial class InteractionController : NetworkBehaviour, IInteractionRequestSource
    {
        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("상호작용 키. 게임 디자인 6.5는 G로 확정했다")]
        [SerializeField] private KeyCode _interactKey = KeyCode.G;

        [Tooltip("후보를 다시 찾는 주기(초). 매 프레임 물리 질의를 돌리지 않는다")]
        [SerializeField] private float _scanInterval = 0.15f;

        private InteractionClient _client;

        public uint ActorNetId => netId;
        public Vector3 ActorPosition => transform.position;
        public string PromptText => (_client != null) ? _client.PromptText : null;
        public string NoticeText => (_client != null) ? _client.NoticeText : null;
        public KeyCode InteractKey => _interactKey;
        public uint CandidateNetId => _client == null ? 0u : _client.CandidateNetId;
        public event System.Action<InteractionResult, InventoryResult, WeaponCombatSnapshot> InteractionCompleted;

        private void Update()
        {
            if (!isLocalPlayer)
            {
                return;
            }

            if (_client == null)
            {
                _client = new InteractionClient(this);
            }

            _client.Tick(_interactKey, _scanInterval);
        }

        /// <summary> true는 요청 전송이며 서버 승인 결과가 아니다 </summary>
        public bool RequestInteract(uint targetNetId)
        {
            if ((!isLocalPlayer) || (!NetworkClient.active) ||
                (!LocalGameplayInputGate.AllowsGameplayInput) ||
                (targetNetId == 0u) || (targetNetId == netId))
            {
                return false;
            }

            CmdInteract(targetNetId);
            return true;
        }

        /****************************************
        *           Client Callbacks
        ****************************************/

        public override void OnStopLocalPlayer()
        {
            _client = null;
            base.OnStopLocalPlayer();
        }
    }
}
