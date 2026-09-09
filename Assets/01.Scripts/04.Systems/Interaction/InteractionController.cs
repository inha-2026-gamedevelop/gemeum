// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Systems
{
    /// <summary>
    /// 플레이어의 상호작용 창구. 물건마다 Command를 만들지 않고 여기 하나만 둔다.
    /// 클라이언트는 후보를 찾아 안내 문구를 띄우고 netId만 보내며, 판정은 전부 서버가 다시 한다.
    /// 클라이언트가 보낸 후보는 "무엇을 하려 했는가"일 뿐 승인 근거가 아니다
    /// </summary>
    [DisallowMultipleComponent]
    public class InteractionController : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("상호작용 키. 게임 디자인 6.5는 G로 확정했다")]
        [SerializeField] private KeyCode _interactKey = KeyCode.G;

        [Tooltip("후보를 다시 찾는 주기(초). 매 프레임 물리 질의를 돌리지 않는다")]
        [SerializeField] private float _scanInterval = 0.15f;

        // 로컬 표시용. 서버 판정과는 무관하다
        private IInteractable _localCandidate;
        private uint          _localCandidateNetId;
        private float         _nextScanTime;

        private static readonly Collider[] _scanBuffer = new Collider[16];

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 지금 띄울 안내 문구. 후보가 없으면 null이다. UI가 읽는다 </summary>
        public string PromptText => (_localCandidate != null) ? _localCandidate.PromptText : null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            if (!isLocalPlayer)
            {
                return;
            }

            if (Time.time >= _nextScanTime)
            {
                _nextScanTime = (Time.time + _scanInterval);
                RefreshCandidate();
            }

            if (LocalGameplayInputGate.AllowsGameplayInput &&
                (Input.GetKeyDown(_interactKey)) && (_localCandidateNetId != 0u))
            {
                CmdInteract(_localCandidateNetId);
            }
        }

        /****************************************
        *              Client Local
        ****************************************/

        /// <summary> 가장 가까운 후보를 찾는다. 표시용이며 서버는 이 결과를 신뢰하지 않는다 </summary>
        private void RefreshCandidate()
        {
            _localCandidate      = null;
            _localCandidateNetId = 0u;

            if (!GameDB.IsInitialized)
            {
                return;
            }

            float range = GameDB.Combat.InteractRange;
            int count = Physics.OverlapSphereNonAlloc(transform.position, range, _scanBuffer,
                ~0, QueryTriggerInteraction.Collide);

            float nearestSquared = float.MaxValue;
            for (int i = 0; i < count; ++i)
            {
                Collider hit = _scanBuffer[i];
                if (hit == null)
                {
                    continue;
                }

                var interactable = hit.GetComponentInParent<IInteractable>();
                if (interactable == null)
                {
                    continue;
                }

                var identity = hit.GetComponentInParent<NetworkIdentity>();
                if ((identity == null) || (identity.netId == 0u) || (identity == netIdentity))
                {
                    continue;
                }

                if (!interactable.CanInteract(netId))
                {
                    continue;
                }

                float distanceSquared = (identity.transform.position - transform.position).sqrMagnitude;
                if (distanceSquared >= nearestSquared)
                {
                    continue;
                }

                nearestSquared       = distanceSquared;
                _localCandidate      = interactable;
                _localCandidateNetId = identity.netId;
            }
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary>
        /// 상호작용 요청. 소유 플레이어 오브젝트에 붙어 있어 requiresAuthority 기본값(true)으로 충분하다.
        /// 대상이 실제로 상호작용 가능한지, 거리와 비전투 조건을 만족하는지는 대상이 다시 검사한다
        /// </summary>
        [Command]
        private void CmdInteract(uint targetNetId)
        {
            if (targetNetId == 0u)
            {
                return;
            }

            if (!NetworkServer.spawned.TryGetValue(targetNetId, out NetworkIdentity identity))
            {
                return;
            }

            var interactable = identity.GetComponent<IInteractable>();
            if (interactable == null)
            {
                return;
            }

            // 자기 자신이나 다른 플레이어를 대상으로 위조하는 요청을 막는다
            if (identity == netIdentity)
            {
                return;
            }

            interactable.Interact(netId);
        }
    }
}
