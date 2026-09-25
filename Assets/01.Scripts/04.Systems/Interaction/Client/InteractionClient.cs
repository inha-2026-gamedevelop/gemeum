// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Systems
{
    /// <summary> 후보 탐색과 입력을 처리하고 서버 요청 창구만 호출하는 로컬 소비자 </summary>
    public sealed class InteractionClient
    {
        private readonly IInteractionRequestSource _requests;
        private readonly Collider[] _scanBuffer = new Collider[16];
        private IInteractable _localCandidate;
        private uint _localCandidateNetId;
        private float _nextScanTime;

        // 곁에 있지만 아직 이용할 수 없는 대상의 안내. 지금은 문을 열기 전의 객주 하나뿐이다.
        // 후보에서 빼기만 하면 가까이 가도 아무것도 뜨지 않아 고장으로 보인다
        private string _localNotice;

        public string PromptText => (_localCandidate != null) ? _localCandidate.PromptText : null;

        /// <summary> 누를 수 있는 후보가 없을 때만 띄울 안내. 키를 붙이지 않는다 </summary>
        public string NoticeText => (_localCandidate == null) ? _localNotice : null;
        public uint CandidateNetId => _localCandidateNetId;

        public InteractionClient(IInteractionRequestSource requests)
        {
            _requests = requests;
        }

        public void Tick(KeyCode interactKey, float scanInterval)
        {
            if (Time.time >= _nextScanTime)
            {
                _nextScanTime = Time.time + scanInterval;
                RefreshCandidate();
            }

            if ((LocalGameplayInputGate.AllowsGameplayInput) && (Input.GetKeyDown(interactKey)) &&
                (_localCandidateNetId != 0u))
            {
                _requests.RequestInteract(_localCandidateNetId);
            }
        }

        private void RefreshCandidate()
        {
            _localCandidate      = null;
            _localCandidateNetId = 0u;
            _localNotice         = null;

            if (!GameDB.IsInitialized)
            {
                return;
            }

            float range = GameDB.Combat.InteractRange;
            int count = Physics.OverlapSphereNonAlloc(_requests.ActorPosition, range, _scanBuffer,
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
                if ((identity == null) || (identity.netId == 0u) || (identity.netId == _requests.ActorNetId))
                {
                    continue;
                }

                float distanceSquared = (identity.transform.position - _requests.ActorPosition).sqrMagnitude;

                // 호스트도 순수 클라이언트와 같은 후보를 보여 준다. 실패 이유는 서버 응답으로 알린다
                bool preview = interactable is DroppedItem drop ? drop.Equipment.HasEquipment :
                    interactable is ShopInteractionPoint shop ? shop.IsOpen : interactable.CanInteract(_requests.ActorNetId);
                if (!preview)
                {
                    if ((interactable is ShopInteractionPoint closedShop) && (distanceSquared <= range * range))
                    {
                        _localNotice = closedShop.ClosedNoticeText;
                    }

                    continue;
                }

                if (distanceSquared > range * range || distanceSquared >= nearestSquared)
                {
                    continue;
                }

                nearestSquared       = distanceSquared;
                _localCandidate      = interactable;
                _localCandidateNetId = identity.netId;
            }
        }
    }
}
