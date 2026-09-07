// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Combat;
using Geumeum.Common;

namespace Geumeum.Art
{
    /// <summary> 사망 중 다른 생존자를 비추는 관전 카메라. CameraRig의 Target을 바꿔 처리한다 </summary>
    [RequireComponent(typeof(CameraRig))]
    public class SpectatorCamera : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private CameraRig    _rig;
        private PlayerStatus _localStatus;
        private Transform    _localTransform;

        // 지금 보고 있는 생존자
        private Transform _watching;

        private bool _isSpectating = false;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _rig = GetComponent<CameraRig>();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Update()
        {
            if (_localStatus == null)
            {
                TryBindLocalPlayer();
                return;
            }

            if (!_isSpectating)
            {
                return;
            }

            if (!IsWatchable(_watching))
            {
                WatchNearestSurvivor();
                return;
            }

            if ((CameraFrame.InputEnabled) && (Input.GetMouseButtonDown(Constants.Player.MOUSE_BUTTON_ATTACK)))
            {
                WatchNextSurvivor();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void TryBindLocalPlayer()
        {
            NetworkIdentity localPlayer = NetworkClient.localPlayer;
            if (localPlayer == null)
            {
                return;
            }

            if (!localPlayer.TryGetComponent(out PlayerStatus status))
            {
                Debug.LogError("[SpectatorCamera] 자기 캐릭터에 PlayerStatus가 없어 관전 전환을 할 수 없다", this);
                return;
            }

            _localStatus    = status;
            _localTransform = localPlayer.transform;

            _localStatus.LifeStateChanged += OnLifeStateChanged;

            ApplyLifeState(_localStatus.LifeState);
        }

        private void Unsubscribe()
        {
            if (_localStatus == null)
            {
                return;
            }

            _localStatus.LifeStateChanged -= OnLifeStateChanged;
            _localStatus = null;
        }

        private void OnLifeStateChanged(PlayerLifeState lifeState)
        {
            ApplyLifeState(lifeState);
        }

        private void ApplyLifeState(PlayerLifeState lifeState)
        {
            bool isDead = ((lifeState == PlayerLifeState.Downed) || (lifeState == PlayerLifeState.Spectating));

            if (isDead == _isSpectating)
            {
                return;
            }

            _isSpectating = isDead;

            if (isDead)
            {
                WatchNearestSurvivor();
                return;
            }

            _watching   = null;
            _rig.Target = _localTransform;
        }

        /// <summary> 가장 가까운 생존자를 잡는다 </summary>
        private void WatchNearestSurvivor()
        {
            Vector3   origin  = (_localTransform != null) ? _localTransform.position : transform.position;
            Transform nearest = null;
            float     bestSqr = float.MaxValue;

            foreach (NetworkIdentity identity in NetworkClient.spawned.Values)
            {
                Transform candidate = ResolveSurvivor(identity);
                if (candidate == null)
                {
                    continue;
                }

                float sqr = (candidate.position - origin).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    nearest = candidate;
                }
            }

            _watching   = nearest;
            _rig.Target = (nearest != null) ? nearest : _localTransform;
        }

        /// <summary> 지금 보는 사람 다음 생존자로 넘긴다 </summary>
        private void WatchNextSurvivor()
        {
            Transform first = null;
            bool      isPastCurrent = false;

            foreach (NetworkIdentity identity in NetworkClient.spawned.Values)
            {
                Transform candidate = ResolveSurvivor(identity);
                if (candidate == null)
                {
                    continue;
                }

                if (first == null)
                {
                    first = candidate;
                }

                if (isPastCurrent)
                {
                    _watching   = candidate;
                    _rig.Target = candidate;
                    return;
                }

                if (candidate == _watching)
                {
                    isPastCurrent = true;
                }
            }

            if (first != null)
            {
                _watching   = first;
                _rig.Target = first;
            }
        }

        /// <summary> 관전 대상 후보를 거른다 </summary>
        private Transform ResolveSurvivor(NetworkIdentity identity)
        {
            if ((identity == null) || (identity.isLocalPlayer))
            {
                return null;
            }

            if (!identity.TryGetComponent(out PlayerStatus status))
            {
                return null;
            }

            return (status.IsDead) ? null : identity.transform;
        }

        /// <summary> 보던 대상이 죽거나 파괴됐는지 </summary>
        private bool IsWatchable(Transform target)
        {
            if (target == null)
            {
                return false;
            }

            return (target.TryGetComponent(out PlayerStatus status)) && (!status.IsDead);
        }
    }
}
