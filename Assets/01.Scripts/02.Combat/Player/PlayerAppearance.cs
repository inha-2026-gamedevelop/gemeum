// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary>
    /// 로비에서 고른 외형 모델을 켠다. 서버가 확정하고 클라이언트는 읽기만 하며,
    /// 판정에는 관여하지 않는 표현 전용 컴포넌트다
    /// </summary>
    public class PlayerAppearance : NetworkBehaviour
    {
        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        private struct AppearanceModelBinding
        {
            public AppearanceKind Appearance;
            public GameObject     Model;
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private AppearanceModelBinding[] _bindings = Array.Empty<AppearanceModelBinding>();

        [SyncVar(hook = nameof(OnAppearanceChanged))]    private AppearanceKind _appearance    = default;
        [SyncVar(hook = nameof(OnHasAppearanceChanged))] private bool           _hasAppearance = false;

        private PlayerAnimationController _animation;

        private AppearanceKind _appliedAppearance    = default;
        private bool           _hasAppliedAppearance = false;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 런 시작 전에는 외형이 확정되지 않는다, 기본값을 선택 결과로 읽지 않도록 이 값을 먼저 본다 </summary>
        public bool HasAppearance => _hasAppearance;

        public AppearanceKind Appearance => _appearance;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _animation = GetComponent<PlayerAnimationController>();
        }

        /****************************************
        *              SyncVar Hooks
        ****************************************/

        /// <summary> 서버·클라이언트 모두에서 값이 바뀔 때 호출된다, 판정에는 관여하지 않는다 </summary>
        private void OnAppearanceChanged(AppearanceKind oldAppearance, AppearanceKind newAppearance)
        {
            RefreshAppearance();
        }

        // Jeonrip이 enum 기본값이라 전립을 배정받으면 _appearance 값이 바뀌지 않아 위 훅이 호출되지 않는다.
        // 확정 여부는 반드시 false에서 true로 바뀌므로 외형 적용을 여기에도 건다.
        // _appearance가 먼저 선언되어 있어 스폰 payload를 읽을 때 이 시점의 _appearance는 이미 확정된 값이다
        private void OnHasAppearanceChanged(bool oldHasAppearance, bool newHasAppearance)
        {
            RefreshAppearance();
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary>
        /// 런 시작 시 확정된 외형을 적용한다. LobbyController만 호출한다.
        /// 이미 확정된 뒤의 재호출은 거부한다, 런 도중 외형이 바뀌면 파티원 식별이 흔들린다
        /// </summary>
        [Server]
        public bool ServerSetAppearance(AppearanceKind appearance)
        {
            if (_hasAppearance)
            {
                Debug.LogWarning($"[PlayerAppearance] 이미 외형이 확정된 플레이어에 다시 적용하려 했다: {_appearance}", this);
                return false;
            }

            _appearance    = appearance;
            _hasAppearance = true;
            return true;
        }

        /****************************************
        *                Methods
        ****************************************/

        private void RefreshAppearance()
        {
            if (!_hasAppearance)
            {
                return;
            }

            ApplyAppearance(_appearance);
        }

        /// <summary>
        /// 외형에 매칭되는 모델만 켠다. 매칭되는 모델이 없으면 현재 외형을 보존한다.
        /// 새 외형이 준비되지 않은 상태에서 기존 모델부터 끄면 플레이어가 통째로 사라지기 때문이다
        /// </summary>
        private void ApplyAppearance(AppearanceKind appearance)
        {
            // 외형과 확정 여부 두 SyncVar가 각각 훅을 걸고 있어 같은 외형으로 두 번 들어올 수 있다
            if ((_hasAppliedAppearance) && (_appliedAppearance == appearance))
            {
                return;
            }

            GameObject matchedModel = null;

            for (int i = 0; i < _bindings.Length; ++i)
            {
                AppearanceModelBinding binding = _bindings[i];

                if ((binding.Appearance != appearance) || (binding.Model == null))
                {
                    continue;
                }

                matchedModel = binding.Model;
                break;
            }

            if (matchedModel == null)
            {
                Debug.LogWarning($"[PlayerAppearance] {appearance}에 매칭되는 모델 바인딩이 없어 현재 외형을 유지한다", this);
                return;
            }

            // 사용할 모델을 먼저 확정한 뒤 한 번에 전환한다. 같은 모델을 여러 외형의 임시 모델로
            // 공유해도 마지막 바인딩 순서에 따라 다시 꺼지지 않는다
            for (int i = 0; i < _bindings.Length; ++i)
            {
                GameObject model = _bindings[i].Model;
                if (model != null)
                {
                    model.SetActive(model == matchedModel);
                }
            }

            _appliedAppearance    = appearance;
            _hasAppliedAppearance = true;

            if (_animation != null)
            {
                _animation.RefreshAnimator();
            }
        }
    }
}
