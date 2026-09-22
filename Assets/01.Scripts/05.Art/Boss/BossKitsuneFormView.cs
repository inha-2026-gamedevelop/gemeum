// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Art
{
    /// <summary>
    /// 여우 귀, 꼬리를 1페이즈에는 숨기고 2페이즈 진입 시 드러낸다 - 정체가 드러나는 연출
    /// 서버가 확정한 BossPhaseSnapshot.PhaseIndex만 읽어 표현만 한다, 판정에는 관여하지 않는다
    /// 꼬리 판정 콜라이더(BossPartHitbox)는 별개 오브젝트라 안 건드린다 - 그쪽은 이미
    /// ActivePartId가 2페이즈에서만 켜져서 1페이즈엔 자체적으로 비활성 상태다
    /// </summary>
    public sealed class BossKitsuneFormView : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int KITSUNE_FORM_PHASE_INDEX = 2; // 이 페이즈부터 귀, 꼬리를 드러낸다

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private GameObject _ears;
        [SerializeField] private GameObject _tail;

        private IBossPhaseSource _phaseSource;
        private bool             _isSubscribed;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _phaseSource = GetComponentInParent<IBossPhaseSource>();
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void TrySubscribe()
        {
            if ((_isSubscribed) || (_phaseSource == null))
            {
                return;
            }

            _phaseSource.PhaseChanged += HandlePhaseChanged;
            HandlePhaseChanged(_phaseSource.PhaseSnapshot);
            _isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_isSubscribed)
            {
                return;
            }

            _phaseSource.PhaseChanged -= HandlePhaseChanged;
            _isSubscribed = false;
        }

        private void HandlePhaseChanged(BossPhaseSnapshot snapshot)
        {
            bool revealed = snapshot.PhaseIndex >= KITSUNE_FORM_PHASE_INDEX;
            if (_ears != null)
            {
                _ears.SetActive(revealed);
            }

            if (_tail != null)
            {
                _tail.SetActive(revealed);
            }
        }
    }
}
