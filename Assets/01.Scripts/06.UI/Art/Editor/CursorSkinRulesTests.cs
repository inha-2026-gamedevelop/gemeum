// Third party
using NUnit.Framework;

// Unity
using UnityEngine;

namespace Geumeum.UI.Editor
{
    /// <summary> 커서 모양 선택 규칙. 실제 커서 표시와 UI 위 판정은 Play 모드에서 본다 </summary>
    public sealed class CursorSkinRulesTests
    {
        [TestCase(true,  true,  true,  CursorSkinState.Busy)]
        [TestCase(false, true,  true,  CursorSkinState.Pressed)]
        [TestCase(false, false, true,  CursorSkinState.Hover)]
        [TestCase(false, false, false, CursorSkinState.Default)]
        public void StatePriorityIsBusyPressedHoverDefault(bool busy, bool pressed, bool hover, CursorSkinState expected)
        {
            Assert.AreEqual(expected, CursorSkinRules.SelectState(busy, pressed, hover));
        }

        [TestCase(720,  CursorSkinSize.Small)]
        [TestCase(899,  CursorSkinSize.Small)]
        [TestCase(900,  CursorSkinSize.Medium)]
        [TestCase(1080, CursorSkinSize.Medium)]
        [TestCase(1600, CursorSkinSize.Large)]
        [TestCase(2160, CursorSkinSize.Large)]
        public void SizeFollowsScreenHeight(int height, CursorSkinSize expected)
        {
            Assert.AreEqual(expected, CursorSkinRules.SelectSize(height, 900, 1600));
        }

        [Test]
        public void HotspotScalesWithTextureAndStaysInside()
        {
            Assert.AreEqual(new Vector2(2f, 1f), CursorSkinRules.HotspotPixels(new Vector2(0.032f, 0.022f), 64, 64));
            Assert.AreEqual(new Vector2(16f, 16f), CursorSkinRules.HotspotPixels(new Vector2(0.5f, 0.5f), 32, 32));
            Assert.AreEqual(new Vector2(31f, 31f), CursorSkinRules.HotspotPixels(new Vector2(1f, 1f), 32, 32));
            Assert.AreEqual(Vector2.zero, CursorSkinRules.HotspotPixels(new Vector2(-1f, float.NaN), 32, 32));
            Assert.AreEqual(Vector2.zero, CursorSkinRules.HotspotPixels(new Vector2(0.5f, 0.5f), 0, 32));
        }
    }
}
