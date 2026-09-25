// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 타이밍 섬광을 렌더러 색 덮기로 그린다. RendererTintRegistry의 ParryWindow 층을 쓰므로 피격 플래시(Damage 층)가
    /// 위에 겹쳐도 원래 색이 굳지 않는다. 무기 소켓 위치를 모르므로 모델 전체 틴트 펄스로만 표현한다
    /// 렌더러 목록은 표시를 시작할 때 한 번 모으고, 끝날 때까지 재사용한다
    /// </summary>
    public sealed class ParryFlashTint
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly int G_BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int G_Color     = Shader.PropertyToID("_Color");

        private readonly Transform _root;
        private readonly bool      _includeInactive;
        private readonly bool      _meshRenderersOnly;

        private readonly List<Renderer> _candidates = new List<Renderer>();
        private readonly List<Renderer> _tinted     = new List<Renderer>();
        private readonly List<Color>    _originals  = new List<Color>();

        /****************************************
        *              Properties
        ****************************************/

        public bool IsApplied => _tinted.Count > 0;

        /****************************************
        *              Constructors
        ****************************************/

        /// <param name="meshRenderersOnly"> true면 MeshRenderer, SkinnedMeshRenderer만 덮는다(파티클, 장판 제외) </param>
        public ParryFlashTint(Transform root, bool includeInactive, bool meshRenderersOnly)
        {
            _root              = root;
            _includeInactive   = includeInactive;
            _meshRenderersOnly = meshRenderersOnly;
        }

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 렌더러마다 원래 재질 색에서 flash 쪽으로 weight만큼 섞는다 </summary>
        public void ApplyBlend(Color flash, float weight)
        {
            EnsureCollected(flash);

            float w = Mathf.Clamp01(weight);
            for (int i = 0; i < _tinted.Count; ++i)
            {
                RendererTintRegistry.Push(_tinted[i], RendererTintLayer.ParryWindow,
                    Color.Lerp(_originals[i], flash, w));
            }
        }

        /// <summary> 모든 렌더러를 같은 색으로 덮는다 </summary>
        public void ApplySolid(Color color)
        {
            EnsureCollected(color);

            for (int i = 0; i < _tinted.Count; ++i)
            {
                RendererTintRegistry.Push(_tinted[i], RendererTintLayer.ParryWindow, color);
            }
        }

        /// <summary>
        /// 비대상 색은 어둡게 낮춘다. 밝은 반짝임은 "내가 패링할 수 있다"는 신호라 비대상과 겹쳐 보이면 안 된다
        /// 대상은 그대로 두고, 알파는 바꾸지 않는다
        /// </summary>
        public static Color ForAudience(Color color, bool isTarget, float nonTargetScale)
        {
            if (isTarget)
            {
                return color;
            }

            float scale = ParryFlashTiming.ClampScale(nonTargetScale);
            return new Color(color.r * scale, color.g * scale, color.b * scale, color.a);
        }

        /// <summary> 덮었던 색을 거둔다. 되돌리기는 레지스트리가 한다 </summary>
        public void Clear()
        {
            for (int i = 0; i < _tinted.Count; ++i)
            {
                RendererTintRegistry.Pop(_tinted[i], RendererTintLayer.ParryWindow);
            }

            _tinted.Clear();
            _originals.Clear();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void EnsureCollected(Color initial)
        {
            if ((_tinted.Count > 0) || (_root == null))
            {
                return;
            }

            _root.GetComponentsInChildren(_includeInactive, _candidates);
            for (int i = 0; i < _candidates.Count; ++i)
            {
                Renderer candidate = _candidates[i];
                if ((_meshRenderersOnly) && (!(candidate is MeshRenderer)) && (!(candidate is SkinnedMeshRenderer)))
                {
                    continue;
                }

                // 원래 색은 덮기 전에 읽는다. 레지스트리는 색 프로퍼티가 없는 재질을 거절한다
                Color original = ReadMaterialColor(candidate.sharedMaterial);
                if (RendererTintRegistry.Push(candidate, RendererTintLayer.ParryWindow, initial))
                {
                    _tinted.Add(candidate);
                    _originals.Add(original);
                }
            }

            _candidates.Clear();
        }

        private static Color ReadMaterialColor(Material material)
        {
            if (material == null)
            {
                return Color.white;
            }

            if (material.HasProperty(G_BaseColor))
            {
                return material.GetColor(G_BaseColor);
            }

            return material.HasProperty(G_Color) ? material.GetColor(G_Color) : Color.white;
        }
    }
}
