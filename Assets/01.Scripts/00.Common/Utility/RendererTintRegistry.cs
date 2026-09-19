// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> 같은 렌더러에 색을 덮는 표현이 겹칠 때의 우선순위. 값이 클수록 위에 그린다 </summary>
    public enum RendererTintLayer
    {
        ParryWindow = 0,
        Damage      = 1,
    }

    /// <summary>
    /// 렌더러 색 덮기를 한곳에서 관리한다.
    /// 컴포넌트마다 원래 블록을 따로 저장했다 되돌리면, 패링 창과 피격처럼 겹칠 때 한쪽이 남의 색을
    /// 원래 색으로 알고 되돌려 강조색이 굳는다. 원래 블록은 첫 요청 때 한 번만 저장한다
    /// </summary>
    public static class RendererTintRegistry
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int LAYER_COUNT = 2;

        private static readonly int G_BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int G_Color     = Shader.PropertyToID("_Color");

        private sealed class Entry
        {
            public readonly MaterialPropertyBlock Original = new MaterialPropertyBlock();
            public readonly MaterialPropertyBlock Applied  = new MaterialPropertyBlock();
            public readonly bool[]  IsActive = new bool[LAYER_COUNT];
            public readonly Color[] Colors   = new Color[LAYER_COUNT];
            public int ColorProperty;
            public int ActiveCount;
        }

        private static readonly Dictionary<Renderer, Entry> G_Entries = new Dictionary<Renderer, Entry>();
        private static readonly Stack<Entry>                G_Pool    = new Stack<Entry>();

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 색을 덮는다. 색 프로퍼티가 없는 재질이면 false </summary>
        public static bool Push(Renderer renderer, RendererTintLayer layer, Color color)
        {
            if (renderer == null)
            {
                return false;
            }

            int index = (int) layer;
            if (!G_Entries.TryGetValue(renderer, out Entry entry))
            {
                int colorProperty = ResolveColorProperty(renderer.sharedMaterial);
                if (colorProperty == 0)
                {
                    return false;
                }

                entry = (G_Pool.Count > 0) ? G_Pool.Pop() : new Entry();
                entry.ColorProperty = colorProperty;
                renderer.GetPropertyBlock(entry.Original);
                G_Entries.Add(renderer, entry);
            }

            if (!entry.IsActive[index])
            {
                entry.IsActive[index] = true;
                ++entry.ActiveCount;
            }

            entry.Colors[index] = color;
            Apply(renderer, entry);
            return true;
        }

        /// <summary> 덮었던 색을 거둔다. 남은 요청이 없으면 원래 블록으로 되돌린다 </summary>
        public static void Pop(Renderer renderer, RendererTintLayer layer)
        {
            // 파괴된 렌더러도 같은 인스턴스라 키로 찾을 수 있다. 되돌릴 대상이 없으니 항목만 치운다
            if (ReferenceEquals(renderer, null) || !G_Entries.TryGetValue(renderer, out Entry entry))
            {
                return;
            }

            int index = (int) layer;
            if (!entry.IsActive[index])
            {
                return;
            }

            entry.IsActive[index] = false;
            --entry.ActiveCount;

            bool isAlive = (renderer != null);
            if (entry.ActiveCount > 0)
            {
                if (isAlive)
                {
                    Apply(renderer, entry);
                }

                return;
            }

            if (isAlive)
            {
                renderer.SetPropertyBlock(entry.Original);
            }

            G_Entries.Remove(renderer);
            entry.Original.Clear();
            entry.Applied.Clear();
            G_Pool.Push(entry);
        }

        public static bool IsTinted(Renderer renderer)
        {
            return (!ReferenceEquals(renderer, null)) && G_Entries.ContainsKey(renderer);
        }

        private static void Apply(Renderer renderer, Entry entry)
        {
            int top = -1;
            for (int i = LAYER_COUNT - 1; i >= 0; --i)
            {
                if (entry.IsActive[i])
                {
                    top = i;
                    break;
                }
            }

            renderer.SetPropertyBlock(entry.Original);
            renderer.GetPropertyBlock(entry.Applied);
            entry.Applied.SetColor(entry.ColorProperty, entry.Colors[top]);
            renderer.SetPropertyBlock(entry.Applied);
        }

        private static int ResolveColorProperty(Material material)
        {
            if (material == null)
            {
                return 0;
            }

            if (material.HasProperty(G_BaseColor))
            {
                return G_BaseColor;
            }

            return material.HasProperty(G_Color) ? G_Color : 0;
        }
    }
}
