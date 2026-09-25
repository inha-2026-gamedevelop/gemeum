// Unity
using UnityEngine;

namespace Geumeum.UI
{
    /// <summary> 커서 상태. 우선순위는 Busy > Pressed > Hover > Default </summary>
    public enum CursorSkinState : byte
    {
        Default = 0,
        Hover   = 1,
        Pressed = 2,
        Busy    = 3,
    }

    /// <summary> 커서 크기 단계. 32px, 48px, 64px </summary>
    public enum CursorSkinSize : byte
    {
        Small  = 0,
        Medium = 1,
        Large  = 2,
    }

    /// <summary> 커서 모양을 고르는 순수 규칙. CursorSkin과 분리해 EditMode에서 그대로 테스트한다 </summary>
    public static class CursorSkinRules
    {
        public static CursorSkinState SelectState(bool isBusy, bool isPressed, bool isOverClickable)
        {
            if (isBusy)
            {
                return CursorSkinState.Busy;
            }

            if (isPressed)
            {
                return CursorSkinState.Pressed;
            }

            return (isOverClickable) ? CursorSkinState.Hover : CursorSkinState.Default;
        }

        public static CursorSkinSize SelectSize(int screenHeight, int mediumMinHeight, int largeMinHeight)
        {
            if (screenHeight >= largeMinHeight)
            {
                return CursorSkinSize.Large;
            }

            return (screenHeight >= mediumMinHeight) ? CursorSkinSize.Medium : CursorSkinSize.Small;
        }

        /// <summary> 비율 끝점을 텍스처 픽셀 좌표로 바꾼다. 텍스처 밖으로 나가면 운영체제가 커서를 거부하므로 안쪽으로 자른다 </summary>
        public static Vector2 HotspotPixels(Vector2 normalized, int width, int height)
        {
            if ((width <= 0) || (height <= 0))
            {
                return Vector2.zero;
            }

            float x = Mathf.Clamp01(float.IsNaN(normalized.x) ? 0f : normalized.x) * width;
            float y = Mathf.Clamp01(float.IsNaN(normalized.y) ? 0f : normalized.y) * height;

            return new Vector2(Mathf.Clamp(Mathf.Round(x), 0f, width - 1), Mathf.Clamp(Mathf.Round(y), 0f, height - 1));
        }
    }
}
