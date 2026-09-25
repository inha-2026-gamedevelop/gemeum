// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Combat;
using Geumeum.Data;

namespace Geumeum.Art
{
    /// <summary>
    /// ICombatFeedback의 아트 구현체. 카메라 셰이크와 화면 플래시를 재생한다
    /// 내가 준 타격의 멈춤, 셰이크, 플래시는 서버 피해 결과를 받는 ImpactDirector가 한 곳에서 맡는다
    /// 전투 계약 밖이지만 "내가 맞았다"도 여기서 받는다 - 체력 SyncVar 훅이 이미 세 화면에 같은 값을 준다
    /// </summary>
    [RequireComponent(typeof(CameraRig))]
    public class CombatFeedbackImpl : MonoBehaviour, ICombatFeedback
    {
        /****************************************
        *                Fields
        ****************************************/

        private CameraRig      _rig;
        private ImpactDirector _impact;

        // 피격을 구독 중인 내 캐릭터. 스폰·재접속으로 바뀌면 옮겨 단다
        private PlayerStatus _observedLocal;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _rig = GetComponent<CameraRig>();

            // 씬을 고치지 않고 생기도록 여기서 붙인다. 이미 붙어 있으면 그대로 쓴다
            if (!TryGetComponent(out _impact))
            {
                _impact = gameObject.AddComponent<ImpactDirector>();
            }
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

        /// <summary>
        /// 타격 순간. 계약 때문에 남겨 두지만 셰이크, 히트스톱은 하지 않는다
        /// 이 호출(근접 RpcMeleeResult)에는 대상, 처치, 실행 단위가 없어 여러 적을 맞힌 공격을 한 번으로 묶거나
        /// 처치 강조, 무기 배수를 고를 수 없다. 같은 타격을 두 경로가 재생하면 겹치므로
        /// 서버 피해 결과(LocalDamageResultFeed)를 받는 ImpactDirector만 재생한다
        /// </summary>
        public void PlayHit(HitStrength strength, float duration)
        {
        }

        /// <summary> 패링 성공. T3 소리, 카메라의 중요도라 셰이크와 화면 플래시만 주고 멈춤은 걸지 않는다 </summary>
        public void PlayParrySuccess()
        {
            if (!TryGetConfig(out CameraConfigSO config))
            {
                return;
            }

            Shake(config.ShakeParrySuccess);
            _impact.PlayParryFlash();
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

        /// <summary> 보스 그로기 진입(T5). 파티 전원이 같은 셰이크와 플래시를 본다 </summary>
        public void PlayGroggyEnter()
        {
            if (!TryGetConfig(out CameraConfigSO config))
            {
                return;
            }

            Shake(config.ShakeGroggyEnter);
            _impact.PlayPartyMoment(config.ShakeGroggyEnter.Duration);
        }

        /// <summary> 보스 부위 파괴(꼬리 한 덩이, T5) </summary>
        public void PlayPartBreak()
        {
            if (TryGetConfig(out CameraConfigSO config))
            {
                Shake(config.ShakePartBreak);
                _impact.PlayPartyMoment(config.ShakePartBreak.Duration);
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
