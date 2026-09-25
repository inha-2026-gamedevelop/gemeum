// Unity
using UnityEngine;
using UnityEngine.UI;

// 프로젝트 내부
using Geumeum.AI;
using Geumeum.Common;

namespace Geumeum.UI
{
    /// <summary>
    /// 이 화면의 플레이어가 잡귀를 때리면 그 머리 위에 체력 바를 잠깐 띄운다
    /// 파티원이 때린 잡귀의 바는 뜨지 않는다. 누가 때렸는지는 서버가 TargetRpc로 때린 사람에게만 알린다
    ///
    /// 체력과 최대 체력 모두 SyncVar라 클라이언트가 비율을 직접 만든다
    /// 결계 단계 강화와 몰이 강화가 최대 체력을 바꾸므로 밸런스 원본으로 대신하지 않는다
    /// 스폰 직후 최대 체력이 아직 0이면 바를 띄우지 않는다
    ///
    /// 적 프리팹 자식으로 붙는다. 배선은 EnemyHpBarWiringHelper가 한다
    ///
    /// 판정은 없다. 체력 값은 서버가 확정하고 여기는 보여주기만 한다
    /// </summary>
    public sealed class EnemyHpBar : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("대상")]
        [Tooltip("비워 두면 부모에서 찾는다")]
        [SerializeField] private EnemyStatus _target = null;

        [Header("표시")]
        [Tooltip("바 전체. 맞기 전에는 숨긴다")]
        [SerializeField] private CanvasGroup _canvasGroup = null;

        [Tooltip("체력 채움. Image Type이 Filled여야 한다")]
        [SerializeField] private Image _hpFill = null;

        [Header("시간")]
        [Tooltip("마지막 피격 뒤 또렷하게 떠 있는 시간(초)")]
        [SerializeField] private float _holdSeconds = 3f;

        [Tooltip("사라지는 데 걸리는 시간(초)")]
        [SerializeField] private float _fadeSeconds = 0.5f;

        // 0보다 크면 떠 있다
        private float _remainingSeconds = 0f;

        private Camera _camera = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_target == null)
            {
                _target = GetComponentInParent<EnemyStatus>();
            }

            if (HasMissingReference())
            {
                enabled = false;
                return;
            }
        }

        private void OnEnable()
        {
            _target.HitByLocalPlayer += OnHitByLocalPlayer;
            _target.HealthDecreased  += OnHealthDecreased;
            ClientPreferences.Changed += OnPreferencesChanged;
            HideNow();
        }

        private void OnDisable()
        {
            // Awake의 참조 검사에서 꺼진 경우에도 여기로 들어온다
            if ((_target == null) || (_canvasGroup == null) || (_hpFill == null))
            {
                return;
            }

            _target.HitByLocalPlayer -= OnHitByLocalPlayer;
            _target.HealthDecreased  -= OnHealthDecreased;
            ClientPreferences.Changed -= OnPreferencesChanged;
            HideNow();
        }

        private void LateUpdate()
        {
            if (_remainingSeconds <= 0f)
            {
                return;
            }

            _remainingSeconds -= Time.deltaTime;

            float alpha = (_fadeSeconds > 0f)
                ? Mathf.Clamp01(_remainingSeconds / _fadeSeconds)
                : ((_remainingSeconds > 0f) ? 1f : 0f);

            _canvasGroup.alpha = alpha;

            FaceCamera();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 바를 띄우는 유일한 입구다. 내가 때렸을 때만 들어온다 </summary>
        private void OnHitByLocalPlayer()
        {
            // 설정에서 끈 사람에게는 띄우지 않는다
            if (!ClientPreferences.ShowEnemyHpBar)
            {
                return;
            }

            if (!ApplyFill())
            {
                return;
            }

            _remainingSeconds  = (_holdSeconds + _fadeSeconds);
            _canvasGroup.alpha = 1f;

            // 뜨는 프레임부터 카메라를 본다. LateUpdate까지 기다리면 한 프레임 비스듬히 보인다
            FaceCamera();
        }

        /// <summary>
        /// 떠 있는 바의 값만 맞춘다. 알림 RPC가 체력 SyncVar보다 먼저 오면 바가 옛 체력으로 뜨는데,
        /// 뒤따라오는 이 훅이 바로잡는다. 파티원이 때려 줄어든 것도 여기서 반영된다.
        /// 숨어 있는 바는 띄우지 않는다
        /// </summary>
        private void OnHealthDecreased()
        {
            if (_remainingSeconds <= 0f)
            {
                return;
            }

            ApplyFill();
        }

        /// <summary> 채움을 맞춘다. 죽었거나 최대 체력을 아직 모르면 숨기고 false를 돌려준다 </summary>
        private bool ApplyFill()
        {
            float max    = _target.MaxHealth;
            float health = _target.Health;

            // 처치 타격은 알림 없이 체력만 0으로 온다. 빈 바를 남기지 않고 바로 숨긴다.
            // 사망 여부는 서버 전용 값이라 동기화된 체력으로 판단한다
            if ((max <= 0f) || (health <= Constants.Combat.HEALTH_EPSILON))
            {
                HideNow();
                return false;
            }

            _hpFill.fillAmount = Mathf.Clamp01(health / max);
            return true;
        }

        private void FaceCamera()
        {
            if (_camera == null)
            {
                _camera = Camera.main;

                if (_camera == null)
                {
                    return;
                }
            }

            transform.rotation = _camera.transform.rotation;
        }

        /// <summary> 떠 있는 동안 설정에서 끄면 남은 시간을 기다리지 않고 바로 거둔다 </summary>
        private void OnPreferencesChanged()
        {
            if (!ClientPreferences.ShowEnemyHpBar)
            {
                HideNow();
            }
        }

        private void HideNow()
        {
            _remainingSeconds  = 0f;
            _canvasGroup.alpha = 0f;
        }

        private bool HasMissingReference()
        {
            if (_target == null)
            {
                Debug.LogError("[EnemyHpBar] EnemyStatus를 찾지 못했다", this);
                return true;
            }

            if (_canvasGroup == null)
            {
                Debug.LogError("[EnemyHpBar] CanvasGroup 참조가 비어 있다", this);
                return true;
            }

            if (_hpFill == null)
            {
                Debug.LogError("[EnemyHpBar] 체력 채움 Image 참조가 비어 있다", this);
                return true;
            }

            return false;
        }
    }
}
