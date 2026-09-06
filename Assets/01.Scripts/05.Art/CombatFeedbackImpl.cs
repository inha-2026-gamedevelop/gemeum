// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Art
{
    /// <summary>
    /// ICombatFeedback의 아트 구현체. 전투는 타이밍만 호출하고 표현은 여기서 정한다.
    /// 지금 하는 일은 카메라 셰이크뿐이고, 히트 이펙트와 패링 슬로우모션은 아직 없다.
    /// 로컬 표현이라 동기화하지 않고 판정에 관여하지 않는다
    /// </summary>
    // 구조 설계는 이 컴포넌트를 `@Local`에 두는 것으로 적고 있으나,
    // 지금 하는 일이 카메라 셰이크뿐이라 카메라에 붙였다. 히트 이펙트가 붙어 카메라와
    // 무관한 일이 생기면 그때 `@Local`로 옮기고 리그 참조를 직렬화 필드로 받는다.
    // 수치는 전부 CameraConfigSO가 소유한다
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

        // 등록과 해제를 OnEnable/OnDisable로 짝지어야 컷신이 리그를 잡아갔다 돌려줄 때 짝이 맞는다
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

        /// <summary>
        /// 타격 순간. duration은 히트스톱 길이이지 셰이크 길이가 아니다 -
        /// 셰이크 길이는 강도별로 CameraConfigSO가 갖고 있다
        /// </summary>
        public void PlayHit(HitStrength strength, float duration)
        {
            if (!TryGetConfig(out CameraConfigSO config))
            {
                return;
            }

            Shake(config.ResolveHitShake(strength));
        }

        /// <summary>
        /// 패링 성공. 피격보다 세게 흔든다 - 결과를 알리는 셰이크는 세도 되지만
        /// 다음 패링을 방해하는 셰이크는 약해야 한다. 패링 창이 0.32초다
        /// </summary>
        public void PlayParrySuccess()
        {
            if (!TryGetConfig(out CameraConfigSO config))
            {
                return;
            }

            Shake(config.ShakeParrySuccess);
        }

        /// <summary> 치명타. multiplier는 아직 세기에 반영하지 않는다 - 배수마다 화면이 달라지면 예측이 안 된다 </summary>
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
            if (!TryGetConfig(out CameraConfigSO config))
            {
                return;
            }

            Shake(config.ShakePartBreak);
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Shake(CameraConfigSO.ShakeSpec spec)
        {
            _rig.AddShake(spec.Amplitude, spec.Duration);
        }

        // 부트 순서상 GameDB보다 먼저 호출될 수 있다. 연출이 없는 것은 게임을 멈출 이유가 아니다
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
