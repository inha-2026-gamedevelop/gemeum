// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Combat;
using Geumeum.Data;

namespace Geumeum.Art
{
    /// <summary> ICombatFeedback의 아트 구현체. 카메라 셰이크를 재생한다 </summary>
    [RequireComponent(typeof(CameraRig))]
    public class CombatFeedbackImpl : MonoBehaviour, ICombatFeedback
    {
        /****************************************
        *                Fields
        ****************************************/

        private CameraRig _rig;

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
                animation.PlayHitStop(duration);
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

        private void Shake(CameraConfigSO.ShakeSpec spec)
        {
            _rig.AddShake(spec.Amplitude, spec.Duration);
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
