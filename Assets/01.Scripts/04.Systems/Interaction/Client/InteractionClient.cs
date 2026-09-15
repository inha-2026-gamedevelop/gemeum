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

        public string PromptText => (_localCandidate != null) ? _localCandidate.PromptText : null;

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

                if (!interactable.CanInteract(_requests.ActorNetId))
                {
                    continue;
                }

                float distanceSquared = (identity.transform.position - _requests.ActorPosition).sqrMagnitude;
                if (distanceSquared >= nearestSquared)
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
