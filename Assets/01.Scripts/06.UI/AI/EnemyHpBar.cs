// Unity
using UnityEngine;
using UnityEngine.UI;

// 프로젝트 내부
using Geumeum.AI;

namespace Geumeum.UI
{
    /// <summary>
    /// 잡귀가 맞으면 머리 위에 체력 바를 잠깐 띄운다
    ///
    /// 체력은 SyncVar라 클라이언트도 읽지만 최대 체력은 아직 서버에만 있다(EnemyStatus.server.cs)
    /// 결계 단계 강화와 몰이 강화가 최대 체력을 바꾸므로 밸런스 원본으로 비율을 만들 수 없다
    /// 최대 체력이 동기화되기 전까지 MaxHealth는 클라이언트에서 0이고, 그동안 바를 띄우지 않는다
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
            _target.HealthDecreased += OnHealthDecreased;
            HideNow();
        }

        private void OnDisable()
        {
            // Awake의 참조 검사에서 꺼진 경우에도 여기로 들어온다
            if ((_target == null) || (_canvasGroup == null) || (_hpFill == null))
            {
                return;
            }

            _target.HealthDecreased -= OnHealthDecreased;
            HideNow();
        }

        private void LateUpdate()
        {
            if (_remainingSeconds <= 0f)
            {
                return;
            }

            // TODO: 사망 시 즉시 숨김
            // TODO: 흐려지는 구간 알파 처리
            _remainingSeconds -= Time.deltaTime;

            FaceCamera();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnHealthDecreased()
        {
            float max = _target.MaxHealth;

            // TODO: 최대 체력 동기화 후 이 분기를 제거
            if (max <= 0f)
            {
                return;
            }

            _hpFill.fillAmount = Mathf.Clamp01(_target.Health / max);
            _remainingSeconds  = (_holdSeconds + _fadeSeconds);
            _canvasGroup.alpha = 1f;
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
