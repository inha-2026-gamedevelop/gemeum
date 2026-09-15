// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI
{
    /// <summary>
    /// EnemyController가 동기화하는 EnemyVisualState를 Animator 파라미터로 반영하는 표현 전용 컴포넌트.
    /// 판정에는 관여하지 않는다. 실제 상태별 모션 연결은 각 몬스터의 Animator Controller에서
    /// VisualState 파라미터 조건으로 구성한다 — 몬스터가 늘어나도 이 컴포넌트는 그대로 재사용한다
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class EnemyAnimationController : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        // 문자열 기반 Animator 호출은 매번 해시를 재계산한다, 정적 해시로 캐싱해서 쓴다
        private static readonly int PARAM_VISUAL_STATE_HASH   = Animator.StringToHash("VisualState");
        private static readonly int PARAM_ATTACK_VARIANT_HASH = Animator.StringToHash("AttackVariant");

        // 창귀 점프 공격 전용 — 판정에는 영향 없는 시각 연출. 스테이트 이름이 없는 몬스터에서는 그냥 매 프레임 0을 더해 아무 효과가 없다
        private static readonly int JUMP_ATTACK_STATE_HASH = Animator.StringToHash("Attack_Jump");
        private const float JUMP_ATTACK_HOP_HEIGHT = 0.5f; // 최고점(m)
        private const float JUMP_ATTACK_HOP_EXTENT = 0.85f; // 이 정규화 시간까지 0→최고점→0을 마친다, Attack_Jump→Recovery 전환의 exitTime과 맞춰야 착지가 자연스럽다

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private Animator _animator;

        private IEnemyPresentationSource _controller;
        private Transform       _artTransform;
        private Vector3         _artBaseLocalPosition;
        private float           _lastHop;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _controller = GetComponent<EnemyController>();

            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
            }

            if (_animator == null)
            {
                Debug.LogWarning("[EnemyAnimationController] 자식에서 Animator를 찾지 못했다", this);
                return;
            }

            _artTransform         = _animator.transform;
            _artBaseLocalPosition = _artTransform.localPosition;
        }

        // NavMeshAgent가 이동을 담당하고 애니메이터 루트 모션은 안 쓰므로, 점프 공격의 상하 움직임은
        // Animator 재생 진행도만 보고 여기서 직접 얹는다 — 판정(Transform 루트)에는 영향을 주지 않는다
        private void LateUpdate()
        {
            if ((_animator == null) || (_artTransform == null))
            {
                return;
            }

            AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(0);
            float              hop = 0f;

            if (info.shortNameHash == JUMP_ATTACK_STATE_HASH)
            {
                float t = Mathf.Clamp01(info.normalizedTime / JUMP_ATTACK_HOP_EXTENT);
                hop = JUMP_ATTACK_HOP_HEIGHT * Mathf.Sin(t * Mathf.PI);
            }

            // hop이 계속 0이면 이미 기준 위치라 쓸 필요가 없다, 0으로 돌아오는 마지막 한 번만 반영한다
            if ((hop == 0f) && (_lastHop == 0f))
            {
                return;
            }

            _artTransform.localPosition = _artBaseLocalPosition + (Vector3.up * hop);
            _lastHop                    = hop;
        }

        private void OnEnable()
        {
            _controller.VisualStateChanged   += HandleVisualStateChanged;
            _controller.AttackVariantChanged += HandleAttackVariantChanged;
            HandleVisualStateChanged(_controller.VisualState);
            HandleAttackVariantChanged(_controller.AttackVariant);
        }

        private void OnDisable()
        {
            _controller.VisualStateChanged   -= HandleVisualStateChanged;
            _controller.AttackVariantChanged -= HandleAttackVariantChanged;
        }

        /****************************************
        *                Methods
        ****************************************/

        private void HandleVisualStateChanged(EnemyVisualState state)
        {
            if (_animator == null)
            {
                return;
            }

            _animator.SetInteger(PARAM_VISUAL_STATE_HASH, (int)state);
        }

        /// <summary> 여러 공격 모션이 있는 몬스터(예: 창귀)에서 이번 공격에 쓸 모션 인덱스를 반영한다 </summary>
        private void HandleAttackVariantChanged(int variant)
        {
            if (_animator == null)
            {
                return;
            }

            _animator.SetInteger(PARAM_ATTACK_VARIANT_HASH, variant);
        }
    }
}
