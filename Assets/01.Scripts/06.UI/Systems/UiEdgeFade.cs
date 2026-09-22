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

        // 직렬화된 값이다. 기존 번호를 바꾸거나 재배열하지 않는다
        public enum FadeSide
        {
            Right  = 0,
            Left   = 1,
            Bottom = 2,
            Top    = 3,
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

            bool isVertical = IsVertical(_transparentSide);

            // Right와 Top은 값이 큰 쪽이 투명해진다
            bool fadesAtMaximum = ((_transparentSide == FadeSide.Right) ||
                (_transparentSide == FadeSide.Top));

            float minimum = float.MaxValue;
            float maximum = float.MinValue;
            UIVertex vertex = default;

            for (int i = 0; i < helper.currentVertCount; ++i)
            {
                helper.PopulateUIVertex(ref vertex, i);
                float axis = isVertical ? vertex.position.y : vertex.position.x;
                if (axis < minimum)
                {
                    minimum = axis;
                }

                if (axis > maximum)
                {
                    maximum = axis;
                }
            }

            float span = (maximum - minimum);
            if (span <= EPSILON)
            {
                return;
            }

            for (int i = 0; i < helper.currentVertCount; ++i)
            {
                helper.PopulateUIVertex(ref vertex, i);

                float axis = isVertical ? vertex.position.y : vertex.position.x;
                float ratio = ((axis - minimum) / span);
                float opacity = fadesAtMaximum ? (1f - ratio) : ratio;

                Color32 color = vertex.color;
                color.a = (byte) Mathf.RoundToInt(color.a * Mathf.Clamp01(opacity));
                vertex.color = color;

                helper.SetUIVertex(vertex, i);
            }
        }

        private static bool IsVertical(FadeSide side)
        {
            return ((side == FadeSide.Bottom) || (side == FadeSide.Top));
        }
    }
}
