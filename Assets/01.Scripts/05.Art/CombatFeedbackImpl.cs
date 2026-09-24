// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Combat;
using Geumeum.Data;

namespace Geumeum.Art
{
    /// <summary>
    /// ICombatFeedback의 아트 구현체. 카메라 셰이크와 히트스톱을 재생한다
    /// 전투 계약 밖이지만 "내가 맞았다"도 여기서 받는다 - 체력 SyncVar 훅이 이미 세 화면에 같은 값을 준다
    /// </summary>
    [RequireComponent(typeof(CameraRig))]
    public class CombatFeedbackImpl : MonoBehaviour, ICombatFeedback
    {
        /****************************************
        *                Fields
        ****************************************/

        private CameraRig _rig;

        // 피격을 구독 중인 내 캐릭터. 스폰·재접속으로 바뀌면 옮겨 단다
        private PlayerStatus _observedLocal;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _rig = GetComponent<CameraRig>();
        }

        private void OnEnable()
        {
            CombatFeedbackLocator.Register(this);
        }

        private void OnDisable()
        {
            CombatFeedbackLocator.Unregister(this);
            ObserveLocal(null);
        }

        // 내 캐릭터가 생기고 사라지는 순간을 알려 주는 이벤트가 없어 참조만 비교한다
        private void Update()
        {
            PlayerStatus local = PlayerStatus.Local;
            if (local != _observedLocal)
            {
                ObserveLocal(local);
            }
        }

        /****************************************
        *              ICombatFeedback
        ****************************************/

        /// <summary> 타격 순간 </summary>
        public void PlayHit(HitStrength strength, float duration)
        {
            if (TryGetConfig(out CameraConfigSO config))
            {
                Shake(config.ResolveHitShake(strength));
            }

            PlayerStatus localStatus = PlayerStatus.Local;
            if ((localStatus != null) &&
                localStatus.TryGetComponent(out PlayerAnimationController animation))
            {
                PlayerWeapon weapon = localStatus.GetComponent<PlayerWeapon>();
                if (weapon != null && weapon.Weapon == WeaponKind.Bow) { return; }
                float stop = weapon != null && weapon.Weapon == WeaponKind.Bell && GameDB.IsInitialized
                    ? GameDB.Presentation.BellHitStopSeconds : duration;
                animation.PlayHitStop(stop);
            }
        }

        /// <summary> 패링 성공 </summary>
        public void PlayParrySuccess()
        {
            if (!TryGetConfig(out CameraConfigSO config))
            {
                return;
            }

            Shake(config.ShakeParrySuccess);
        }

        /// <summary> 치명타 </summary>
        public void PlayCritical(float multiplier)
        {
            if (!TryGetConfig(out CameraConfigSO config))
            {
                return;
            }

            Shake(config.ShakeCritical);
        }

        /// <summary> 보스 그로기 진입 </summary>
        public void PlayGroggyEnter()
        {
            if (!TryGetConfig(out CameraConfigSO config))
            {
                return;
            }

            Shake(config.ShakeGroggyEnter);
        }

        /// <summary> 보스 부위 파괴(꼬리 한 덩이) </summary>
        public void PlayPartBreak()
        {
            if (TryGetConfig(out CameraConfigSO config))
            {
                Shake(config.ShakePartBreak);
            }

            SfxPlayer.Play(SoundKey.BossTailBreak, false);
        }

        /****************************************
        *                Methods
        ****************************************/

        private void ObserveLocal(PlayerStatus local)
        {
            if (_observedLocal != null)
            {
                _observedLocal.HealthDecreased -= HandleLocalHealthDecreased;
            }

            _observedLocal = local;

            if (_observedLocal != null)
            {
                _observedLocal.HealthDecreased += HandleLocalHealthDecreased;
            }
        }

        /// <summary> 내가 맞은 순간. 옆으로 크게 흔들리고 다음 패링을 위해 빨리 빠진다 </summary>
        private void HandleLocalHealthDecreased()
        {
            if (TryGetConfig(out CameraConfigSO config))
            {
                Shake(config.ShakeHitTaken);
            }
        }

        private void Shake(in CameraConfigSO.ShakeSpec spec)
        {
            _rig.Shake(spec);
        }

        private bool TryGetConfig(out CameraConfigSO config)
        {
            config = null;

            if (!GameDB.IsInitialized)
            {
                return false;
            }

            config = GameDB.Camera;
            return (config != null);
        }
    }
}
