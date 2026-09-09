// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// Project
using Geumeum.Common;
using Geumeum.Combat;
using Geumeum.Data;

namespace Geumeum.Art
{
    /// <summary>Flashes renderer property blocks only when replicated health decreases.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerDamageFlash : MonoBehaviour
    {
        private sealed class RendererState
        {
            public Renderer Renderer;
            public MaterialPropertyBlock Original;
        }

        private static readonly int G_BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int G_Color = Shader.PropertyToID("_Color");

        private readonly List<RendererState> _states = new List<RendererState>();
        private PlayerStatus _status;
        private float _lastHealth;
        private float _remainingSeconds;
        private bool _isFlashing;

        private void Awake()
        {
            _status = GetComponent<PlayerStatus>();
        }

        private void OnEnable()
        {
            if (_status == null)
            {
                _status = GetComponent<PlayerStatus>();
            }

            if (_status == null)
            {
                Debug.LogError("[PlayerDamageFlash] PlayerStatus is unavailable", this);
                return;
            }

            _lastHealth = _status.Health;
            _status.HealthChanged += HandleHealthChanged;
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
                RestoreRenderers();
            }
        }

        private void OnDisable()
        {
            if (_status != null)
            {
                _status.HealthChanged -= HandleHealthChanged;
            }

            RestoreRenderers();
        }

        private void HandleHealthChanged(float current, float max)
        {
            bool tookDamage = current < (_lastHealth - Mathf.Epsilon);
            _lastHealth = current;
            if (!tookDamage)
            {
                return;
            }

            PresentationConfigSO config = GameDB.IsInitialized
                ? GameDB.Presentation
                : null;
            if ((config == null) || (config.DamageFlashSeconds <= 0f))
            {
                return;
            }

            ApplyFlash(config.DamageFlashColor);
            _remainingSeconds = Mathf.Max(_remainingSeconds,
                config.DamageFlashSeconds);
            _isFlashing = true;
        }

        private void ApplyFlash(Color color)
        {
            RestoreRenderers();
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; ++i)
            {
                Renderer renderer = renderers[i];
                Material material = (renderer == null) ? null : renderer.sharedMaterial;
                if (material == null)
                {
                    continue;
                }

                int colorProperty = material.HasProperty(G_BaseColor)
                    ? G_BaseColor
                    : (material.HasProperty(G_Color) ? G_Color : 0);
                if (colorProperty == 0)
                {
                    continue;
                }

                var original = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(original);
                var flash = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(flash);
                flash.SetColor(colorProperty, color);
                renderer.SetPropertyBlock(flash);
                _states.Add(new RendererState
                {
                    Renderer = renderer,
                    Original = original,
                });
            }
        }

        private void RestoreRenderers()
        {
            for (int i = 0; i < _states.Count; ++i)
            {
                RendererState state = _states[i];
                if (state.Renderer != null)
                {
                    state.Renderer.SetPropertyBlock(state.Original);
                }
            }

            _states.Clear();
            _remainingSeconds = 0f;
            _isFlashing = false;
        }
    }
}
