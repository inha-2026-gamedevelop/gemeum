// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Geumeum.AI
{
    /// <summary>
    /// EnemyController.ParryWindowVisuallyOpenChanged를 구독해, 실제 패링 판정 구간(근사치) 동안만
    /// 렌더러를 강조색으로 바꾼다. 판정에는 관여하지 않는 표현 전용 컴포넌트 — 이슈 #52: 기존에는
    /// 공격 모션 시작과 동시에 창이 열린 것처럼 보여 실제 판정 구간(모션 중반)과 신호가 어긋났다
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public sealed class EnemyParryWindowFlash : MonoBehaviour
    {
        private sealed class RendererState
        {
            public Renderer Renderer;
            public MaterialPropertyBlock Original;
        }

        private static readonly int G_BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int G_Color     = Shader.PropertyToID("_Color");

        private static readonly Color PARRY_WINDOW_COLOR = new Color(1f, 0.92f, 0.4f, 1f); // 옅은 금색, 예고 발광과 구분되는 강조색

        private readonly List<RendererState> _states = new List<RendererState>();

        private EnemyController _controller;

        private void Awake()
        {
            _controller = GetComponent<EnemyController>();
        }

        private void OnEnable()
        {
            _controller.ParryWindowVisuallyOpenChanged += HandleParryWindowVisuallyOpenChanged;
        }

        private void OnDisable()
        {
            _controller.ParryWindowVisuallyOpenChanged -= HandleParryWindowVisuallyOpenChanged;
            RestoreRenderers();
        }

        private void HandleParryWindowVisuallyOpenChanged(bool isOpen)
        {
            if (isOpen)
            {
                ApplyFlash(PARRY_WINDOW_COLOR);
            }
            else
            {
                RestoreRenderers();
            }
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
        }
    }
}
