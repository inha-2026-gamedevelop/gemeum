// Third party
using NUnit.Framework;

// Unity
using UnityEngine;

namespace Geumeum.UI.Editor
{
    /// <summary>
    /// 수직 동기화 · 프레임 상한 기본값과 저장값 적용 규칙
    /// 기본값만 쓰던 사용자는 수직 동기화가 꺼지고, 직접 고른 저장값은 시작할 때 덮어쓰지 않는다
    /// </summary>
    public sealed class ClientPreferencesFrameTests
    {
        // ClientPreferences의 PlayerPrefs 키. 바뀌면 기존 사용자 설정도 사라지므로 여기서 함께 붙잡는다
        private const string KEY_VSYNC     = "Graphics.VSync";
        private const string KEY_FRAME_CAP = "Graphics.FrameCap";

        private bool _hadVSync;
        private int  _savedVSync;
        private bool _hadFrameCap;
        private int  _savedFrameCap;
        private int  _savedVSyncCount;
        private int  _savedTargetFrameRate;

        [SetUp]
        public void SetUp()
        {
            // 에디터의 실제 설정을 건드리므로 원래 값을 남겨 두고 끝나면 되돌린다
            _hadVSync             = PlayerPrefs.HasKey(KEY_VSYNC);
            _savedVSync           = PlayerPrefs.GetInt(KEY_VSYNC, 0);
            _hadFrameCap          = PlayerPrefs.HasKey(KEY_FRAME_CAP);
            _savedFrameCap        = PlayerPrefs.GetInt(KEY_FRAME_CAP, 0);
            _savedVSyncCount      = QualitySettings.vSyncCount;
            _savedTargetFrameRate = Application.targetFrameRate;

            PlayerPrefs.DeleteKey(KEY_VSYNC);
            PlayerPrefs.DeleteKey(KEY_FRAME_CAP);
        }

        [TearDown]
        public void TearDown()
        {
            Restore(KEY_VSYNC, _hadVSync, _savedVSync);
            Restore(KEY_FRAME_CAP, _hadFrameCap, _savedFrameCap);
            QualitySettings.vSyncCount  = _savedVSyncCount;
            Application.targetFrameRate = _savedTargetFrameRate;
        }

        [Test]
        public void DefaultIsVSyncOffAndUnlimited()
        {
            QualitySettings.vSyncCount  = 1;
            Application.targetFrameRate = 30;

            ClientPreferences.ApplyFrameSettings();

            Assert.IsFalse(ClientPreferences.VSync);
            Assert.AreEqual(0, ClientPreferences.FrameCap);
            Assert.AreEqual(0, QualitySettings.vSyncCount);
            Assert.AreEqual(-1, Application.targetFrameRate);
        }

        [Test]
        public void ApplyDoesNotWriteKeys()
        {
            ClientPreferences.ApplyFrameSettings();

            // 시작할 때마다 기본값을 저장해 버리면 다음 기본값 변경을 따라가지 못한다
            Assert.IsFalse(PlayerPrefs.HasKey(KEY_VSYNC));
            Assert.IsFalse(PlayerPrefs.HasKey(KEY_FRAME_CAP));
        }

        [Test]
        public void ExplicitVSyncOnIsKept()
        {
            PlayerPrefs.SetInt(KEY_VSYNC, 1);

            ClientPreferences.ApplyFrameSettings();

            Assert.IsTrue(ClientPreferences.VSync);
            Assert.AreEqual(1, QualitySettings.vSyncCount);
            Assert.AreEqual(1, PlayerPrefs.GetInt(KEY_VSYNC, 0));
        }

        [Test]
        public void ExplicitFrameCapIsKept()
        {
            PlayerPrefs.SetInt(KEY_FRAME_CAP, 144);

            ClientPreferences.ApplyFrameSettings();

            Assert.AreEqual(0, QualitySettings.vSyncCount);
            Assert.AreEqual(144, Application.targetFrameRate);
        }

        [Test]
        public void ToggleSavesAndAppliesAtOnce()
        {
            ClientPreferences.SetVSync(true);
            Assert.AreEqual(1, QualitySettings.vSyncCount);

            ClientPreferences.SetVSync(false);
            ClientPreferences.SetFrameCap(60);
            Assert.AreEqual(0, QualitySettings.vSyncCount);
            Assert.AreEqual(60, Application.targetFrameRate);
            Assert.AreEqual(0, PlayerPrefs.GetInt(KEY_VSYNC, -1));

            ClientPreferences.SetFrameCap(0);
            Assert.AreEqual(-1, Application.targetFrameRate);
        }

        private static void Restore(string key, bool hadKey, int value)
        {
            if (hadKey)
            {
                PlayerPrefs.SetInt(key, value);
            }
            else
            {
                PlayerPrefs.DeleteKey(key);
            }
        }
    }
}
