// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

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
        private static readonly Color PARRY_WINDOW_COLOR = new Color(1f, 0.92f, 0.4f, 1f); // 옅은 금색, 예고 발광과 구분되는 강조색

        private readonly List<Renderer> _candidates = new List<Renderer>();
        private readonly List<Renderer> _tinted     = new List<Renderer>();

        private IEnemyPresentationSource _controller;

        private void Awake()
        {
            _controller = GetComponent<EnemyController>();
        }

        private void OnEnable()
        {
            _controller.ParryWindowVisuallyOpenChanged += HandleParryWindowVisuallyOpenChanged;
            HandleParryWindowVisuallyOpenChanged(_controller.IsParryWindowVisuallyOpen);
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
            GetComponentsInChildren(true, _candidates);
            for (int i = 0; i < _candidates.Count; ++i)
            {
                if (RendererTintRegistry.Push(_candidates[i], RendererTintLayer.ParryWindow, color))
                {
                    _tinted.Add(_candidates[i]);
                }
            }

            _candidates.Clear();
        }

        // 피격 플래시와 겹쳐도 원래 색이 굳지 않도록 되돌리기는 레지스트리에 맡긴다
        private void RestoreRenderers()
        {
            for (int i = 0; i < _tinted.Count; ++i)
            {
                RendererTintRegistry.Pop(_tinted[i], RendererTintLayer.ParryWindow);
            }

            _tinted.Clear();
        }
    }
}
