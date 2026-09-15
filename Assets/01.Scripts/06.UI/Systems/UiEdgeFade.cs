// Unity
using UnityEngine;
using UnityEngine.UI;

namespace Geumeum.UI
{
    /// <summary> 판 한쪽 끝의 알파를 0까지 빼서 게임 화면과 이어 붙인다 </summary>
    [RequireComponent(typeof(Graphic))]
    [DisallowMultipleComponent]
    public sealed class UiEdgeFade : BaseMeshEffect
    {
        /****************************************
        *               Constants
        ****************************************/

        private const float EPSILON = 0.0001f;

        public enum FadeSide
        {
            Right = 0,
            Left  = 1,
        }

        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 투명해지는 쪽 </summary>
        [SerializeField] private FadeSide _transparentSide = FadeSide.Right;

        /****************************************
        *                Methods
        ****************************************/

        public override void ModifyMesh(VertexHelper helper)
        {
            if ((!IsActive()) || (helper == null) || (helper.currentVertCount == 0))
            {
                return;
            }

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            UIVertex vertex = default;

            for (int i = 0; i < helper.currentVertCount; ++i)
            {
                helper.PopulateUIVertex(ref vertex, i);
                if (vertex.position.x < minX)
                {
                    minX = vertex.position.x;
                }

                if (vertex.position.x > maxX)
                {
                    maxX = vertex.position.x;
                }
            }

            float width = (maxX - minX);
            if (width <= EPSILON)
            {
                return;
            }

            for (int i = 0; i < helper.currentVertCount; ++i)
            {
                helper.PopulateUIVertex(ref vertex, i);

                float ratio = ((vertex.position.x - minX) / width);
                float opacity = (_transparentSide == FadeSide.Right) ? (1f - ratio) : ratio;

                Color32 color = vertex.color;
                color.a = (byte) Mathf.RoundToInt(color.a * Mathf.Clamp01(opacity));
                vertex.color = color;

                helper.SetUIVertex(vertex, i);
            }
        }
    }
}
