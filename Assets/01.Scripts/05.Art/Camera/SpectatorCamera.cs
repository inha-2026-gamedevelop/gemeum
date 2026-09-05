// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Combat;
using Geumeum.Common;

namespace Geumeum.Art
{
    /// <summary>
    /// 사망 중 다른 생존자를 비추는 관전 카메라.
    /// 카메라를 두 벌 만들지 않는다 - CameraRig를 그대로 쓰고 Target만 바꾼다.
    /// 완전히 로컬이고 판정에 관여하지 않는다
    /// </summary>
    // 관전 지속 시간(30초, 팀 누적 사망에 따라 최대 75초)은 여기가 아니라 부활 시스템이 소유한다.
    // 이 컴포넌트는 PlayerStatus.LifeState를 따라갈 뿐이다 -
    // 시간을 여기서 재면 서버가 정한 부활 시점과 화면이 어긋난다.
    // 05.Art가 02.Combat의 SyncVar를 읽는 방향이라 의존 방향에 맞는다
    [RequireComponent(typeof(CameraRig))]
    public class SpectatorCamera : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private CameraRig    _rig;
        private PlayerStatus _localStatus;
        private Transform    _localTransform;

        // 지금 보고 있는 생존자. 좌클릭으로 넘길 때 다음 후보를 고르는 기준이 된다
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
            // 자기 캐릭터는 카메라보다 늦게 스폰된다. 잡을 때까지만 확인한다
            if (_localStatus == null)
            {
                TryBindLocalPlayer();
                return;
            }

            if (!_isSpectating)
            {
                return;
            }

            // 보던 사람이 죽거나 사라지면 자동으로 다음 사람을 잡는다
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

            // 이미 죽은 상태로 합류했을 수 있다. 훅은 그때 울리지 않으므로 지금 한 번 맞춘다
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

            // 부활하면 자기 몸으로 돌아온다
            _watching   = null;
            _rig.Target = _localTransform;
        }

        /// <summary>
        /// 가장 가까운 생존자를 잡는다. 9.7의 구조 기믹(쓰러진 사람을 10초 안에 끌어내기)이 여기 걸려 있다 -
        /// 구하러 오는 팀원이 보여야 긴장이 생기므로 가까운 쪽을 먼저 준다
        /// </summary>
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

            // 전멸했으면 아무도 없다. 그때는 쓰러진 자기 몸을 그대로 본다
            _watching   = nearest;
            _rig.Target = (nearest != null) ? nearest : _localTransform;
        }

        /// <summary> 지금 보는 사람 다음 생존자로 넘긴다. 순회 순서는 Mirror의 spawned 순서를 그대로 쓴다 </summary>
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

            // 끝까지 갔으면 처음으로 돌아온다. 생존자가 하나뿐이면 그 사람을 다시 잡는다
            if (first != null)
            {
                _watching   = first;
                _rig.Target = first;
            }
        }

        // 자기 자신과 죽은 사람은 후보가 아니다
        private Transform ResolveSurvivor(NetworkIdentity identity)
        {
            if ((identity == null) || (identity.isLocalPlayer))
            {
                return null;
            }

            if (!identity.TryGetComponent(out PlayerStatus status))
            {
                return null; // 플레이어가 아닌 스폰 개체(잡귀, 화살 등)
            }

            return (status.IsDead) ? null : identity.transform;
        }

        // 보던 대상이 죽거나 파괴됐는지. Unity의 null 비교라 파괴된 Transform도 걸러진다
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
