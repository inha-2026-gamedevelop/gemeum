// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.UI
{
    /// <summary> 큰 지도를 여는 방식. PlayerPrefs에 정수로 저장하므로 값을 재배열하지 않는다 </summary>
    public enum MinimapOpenMode
    {
        Toggle = 0,   // 한 번 눌러 펼치고 다시 눌러 접는다
        Hold   = 1,   // 누르고 있는 동안만 펼친다
    }

    /// <summary>
    /// 이 기기에서만 쓰는 표시 · 그래픽 설정. 게임 결과와 무관하므로 서버를 거치지 않고 PlayerPrefs에 둔다.
    /// 볼륨은 SoundVolume이, 화면 모드와 해상도는 Unity가 직접 저장하므로 여기 두지 않는다
    /// </summary>
    public static class ClientPreferences
    {
        /****************************************
        *                Fields
        ****************************************/

        // PlayerPrefs 키. 바꾸면 기존 사용자의 설정이 초기화된다
        private const string PREF_SHOW_ENEMY_HP_BAR = "Display.EnemyHpBar";
        private const string PREF_MINIMAP_OPEN_MODE = "Display.MinimapOpenMode";
        private const string PREF_VSYNC             = "Graphics.VSync";
        private const string PREF_FRAME_CAP         = "Graphics.FrameCap";

        // 기본값. 프레임 상한 0은 제한 없음이다
        private const int DEFAULT_SHOW_ENEMY_HP_BAR = 1;
        private const int DEFAULT_VSYNC             = 1;
        private const int DEFAULT_FRAME_CAP         = 0;

        /// <summary> 표시 설정이 바뀌었을 때. 이미 떠 있는 화면이 즉시 따라가도록 듣는다 </summary>
        public static event Action Changed;

        // 표시 설정은 자주 읽혀서 처음 한 번만 PlayerPrefs에서 꺼내 둔다
        private static bool            _isDisplayLoaded;
        private static bool            _showEnemyHpBar;
        private static MinimapOpenMode _minimapOpenMode;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 내가 때린 잡귀 머리 위에 체력 바를 띄울지 </summary>
        public static bool ShowEnemyHpBar
        {
            get
            {
                EnsureDisplayLoaded();
                return _showEnemyHpBar;
            }
        }

        /// <summary> 지도 쪽이 매 프레임 읽으므로 PlayerPrefs를 매번 뒤지지 않고 들고 있는 값을 준다 </summary>
        public static MinimapOpenMode MinimapOpenMode
        {
            get
            {
                EnsureDisplayLoaded();
                return _minimapOpenMode;
            }
        }

        public static bool VSync
        {
            get { return (PlayerPrefs.GetInt(PREF_VSYNC, DEFAULT_VSYNC) != 0); }
        }

        /// <summary> 초당 최대 프레임. 0이면 제한하지 않는다. 수직 동기화가 켜져 있으면 무시된다 </summary>
        public static int FrameCap
        {
            get { return Mathf.Max(0, PlayerPrefs.GetInt(PREF_FRAME_CAP, DEFAULT_FRAME_CAP)); }
        }

        /****************************************
        *                Methods
        ****************************************/

        public static void SetShowEnemyHpBar(bool isShown)
        {
            EnsureDisplayLoaded();
            _showEnemyHpBar = isShown;
            PlayerPrefs.SetInt(PREF_SHOW_ENEMY_HP_BAR, isShown ? 1 : 0);
            Changed?.Invoke();
        }

        public static void SetMinimapOpenMode(MinimapOpenMode mode)
        {
            EnsureDisplayLoaded();
            _minimapOpenMode = mode;
            PlayerPrefs.SetInt(PREF_MINIMAP_OPEN_MODE, (int)mode);
            Changed?.Invoke();
        }

        public static void SetVSync(bool isOn)
        {
            PlayerPrefs.SetInt(PREF_VSYNC, isOn ? 1 : 0);
            ApplyFrameSettings();
        }

        public static void SetFrameCap(int framesPerSecond)
        {
            PlayerPrefs.SetInt(PREF_FRAME_CAP, Mathf.Max(0, framesPerSecond));
            ApplyFrameSettings();
        }

        /// <summary> 저장을 디스크에 확정한다. 설정 창을 닫을 때 한 번 부른다 </summary>
        public static void Save()
        {
            PlayerPrefs.Save();
        }

        // 도메인 리로드를 끈 에디터에서 지난 플레이의 구독자와 캐시가 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            Changed          = null;
            _isDisplayLoaded = false;
        }

        // Unity는 수직 동기화와 프레임 상한을 기억하지 않는다. 첫 씬 전에 저장값을 다시 건다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyOnStartup()
        {
            ApplyFrameSettings();
        }

        private static void EnsureDisplayLoaded()
        {
            if (_isDisplayLoaded)
            {
                return;
            }

            _isDisplayLoaded = true;
            _showEnemyHpBar  = (PlayerPrefs.GetInt(PREF_SHOW_ENEMY_HP_BAR, DEFAULT_SHOW_ENEMY_HP_BAR) != 0);

            int stored = PlayerPrefs.GetInt(PREF_MINIMAP_OPEN_MODE, (int)MinimapOpenMode.Toggle);
            _minimapOpenMode = Enum.IsDefined(typeof(MinimapOpenMode), stored)
                ? (MinimapOpenMode)stored
                : MinimapOpenMode.Toggle;
        }

        private static void ApplyFrameSettings()
        {
            QualitySettings.vSyncCount = VSync ? 1 : 0;

            // -1이 플랫폼 기본(제한 없음)이다
            int cap = FrameCap;
            Application.targetFrameRate = (cap > 0) ? cap : -1;
        }
    }
}
