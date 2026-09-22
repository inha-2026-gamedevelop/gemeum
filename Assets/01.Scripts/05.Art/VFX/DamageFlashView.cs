// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// Project
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Art
{
    /// <summary>
    /// 동기화된 체력이 줄었을 때 렌더러 색을 잠깐 덮는다. 플레이어, 잡귀, 보스가 같이 쓴다
    /// 서버가 확정한 체력만 보고 반응하므로 세 화면이 같은 피격에 번쩍인다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DamageFlashView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("번쩍일 렌더러의 최상위. 비우면 이 오브젝트 아래 전부. 보스처럼 부위 표시가 섞여 있으면 모델만 지정한다")]
        [SerializeField] private Transform _visualRoot;

        // 피격마다 목록을 새로 만들지 않도록 재사용한다
        private readonly List<Renderer> _candidates = new List<Renderer>();
        private readonly List<Renderer> _tinted     = new List<Renderer>();

        private IDamagePresentationSource _source;
        private float _remainingSeconds;
        private bool  _isFlashing;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _source = GetComponent<IDamagePresentationSource>();
        }

        private void OnEnable()
        {
            if (_source == null)
            {
                Debug.LogError("[DamageFlashView] IDamagePresentationSource가 같은 오브젝트에 없다", this);
                return;
            }

            _source.HealthDecreased += HandleHealthDecreased;
        }

        private void Update()
        {
            if ((!_isFlashing) || RunTimeLocator.Current.IsPaused)
            {
                return;
            }

            _remainingSeconds -= Time.unscaledDeltaTime;
            if (_remainingSeconds <= 0f)
            {
                EndFlash();
            }
        }

        private void OnDisable()
        {
            if (_source != null)
            {
                _source.HealthDecreased -= HandleHealthDecreased;
            }

            EndFlash();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void HandleHealthDecreased()
        {
            PresentationConfigSO config = GameDB.IsInitialized ? GameDB.Presentation : null;
            if ((config == null) || (config.DamageFlashSeconds <= 0f))
            {
                return;
            }

            // 번쩍이는 중에 또 맞으면 색은 그대로 두고 시간만 늘린다
            if (!_isFlashing)
            {
                BeginFlash(config.DamageFlashColor);
            }

            _remainingSeconds = Mathf.Max(_remainingSeconds, config.DamageFlashSeconds);
            _isFlashing       = true;
        }

        private void BeginFlash(Color color)
        {
            // 모델 교체가 있으므로 캐싱하지 않고 번쩍일 때마다 다시 모은다
            Transform root = (_visualRoot != null) ? _visualRoot : transform;
            root.GetComponentsInChildren(true, _candidates);

            for (int i = 0; i < _candidates.Count; ++i)
            {
                if (RendererTintRegistry.Push(_candidates[i], RendererTintLayer.Damage, color))
                {
                    _tinted.Add(_candidates[i]);
                }
            }

            _candidates.Clear();
        }

        private void EndFlash()
        {
            for (int i = 0; i < _tinted.Count; ++i)
            {
                RendererTintRegistry.Pop(_tinted[i], RendererTintLayer.Damage);
            }

            _tinted.Clear();
            _remainingSeconds = 0f;
            _isFlashing       = false;
        }
    }
}
