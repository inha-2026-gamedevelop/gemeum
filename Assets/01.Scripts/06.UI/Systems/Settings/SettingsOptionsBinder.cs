// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// Project
using Geumeum.Common;

namespace Geumeum.UI
{
    /// <summary>
    /// 옵션 탭의 행들을 실제 설정에 잇는다. 전부 이 기기에서만 쓰는 값이라 서버를 거치지 않는다.
    /// 행은 값을 모르고 여기가 목록 · 현재 값 · 저장을 맡는다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsOptionsBinder : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string LABEL_ON  = "켜기";
        private const string LABEL_OFF = "끄기";

        private const string LABEL_MINIMAP_TOGGLE = "토글";
        private const string LABEL_MINIMAP_HOLD   = "누르는 동안";

        private const string LABEL_FRAME_UNLIMITED = "제한 없음";
        // 수직 동기화가 켜져 있으면 상한 대신 모니터 주사율(부족하면 그 절반)에 묶인다. 막힌 줄에 그 이유를 보인다
        private const string LABEL_FRAME_BY_VSYNC  = "모니터 주사율";

        private static readonly FullScreenMode[] G_ScreenModes =
        {
            FullScreenMode.ExclusiveFullScreen,
            FullScreenMode.FullScreenWindow,
            FullScreenMode.Windowed,
        };

        private static readonly string[] G_ScreenModeLabels =
        {
            "전체 화면",
            "테두리 없는 창",
            "창 모드",
        };

        [Header("게임플레이")]
        [SerializeField] private SettingsStepperRow _enemyHpBarRow;
        [SerializeField] private SettingsStepperRow _minimapModeRow;

        [Header("그래픽")]
        [SerializeField] private SettingsStepperRow _screenModeRow;
        [SerializeField] private SettingsStepperRow _resolutionRow;
        [SerializeField] private SettingsStepperRow _vSyncRow;
        [SerializeField] private SettingsStepperRow _frameCapRow;

        [Tooltip("최대 프레임 선택지. 0은 제한 없음이다")]
        [SerializeField] private int[] _frameCapOptions = { 30, 60, 120, 144, 0 };

        [Header("오디오")]
        [SerializeField] private SettingsSliderRow  _masterRow;
        [SerializeField] private SettingsSliderRow  _bgmRow;
        [SerializeField] private SettingsSliderRow  _sfxRow;
        [SerializeField] private SettingsStepperRow _muteRow;

        // 주사율만 다른 항목은 하나로 합친다. 창을 열 때마다 새로 만든다 — 모니터가 바뀌었을 수 있다
        private readonly List<Vector2Int> _resolutions = new List<Vector2Int>();

        private int _screenModeIndex;
        private int _resolutionIndex;
        private int _frameCapIndex;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            Subscribe(_enemyHpBarRow, StepEnemyHpBar);
            Subscribe(_minimapModeRow, StepMinimapMode);
            Subscribe(_screenModeRow, StepScreenMode);
            Subscribe(_resolutionRow, StepResolution);
            Subscribe(_vSyncRow, StepVSync);
            Subscribe(_frameCapRow, StepFrameCap);
            Subscribe(_muteRow, StepMute);

            Subscribe(_masterRow, OnMasterChanged);
            Subscribe(_bgmRow, OnBgmChanged);
            Subscribe(_sfxRow, OnSfxChanged);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 저장된 값과 지금 화면 상태를 다시 읽어 모든 줄에 띄운다. 창을 열 때 부른다 </summary>
        public void Refresh()
        {
            _screenModeIndex = Array.IndexOf(G_ScreenModes, Screen.fullScreenMode);
            if (_screenModeIndex < 0)
            {
                // macOS 전용 최대화 창 같은 값은 목록에 없다. 가장 가까운 창 모드로 보인다
                _screenModeIndex = G_ScreenModes.Length - 1;
            }

            CollectResolutions();
            _frameCapIndex = FindFrameCapIndex(ClientPreferences.FrameCap);

            RefreshGameplay();
            RefreshGraphics();
            RefreshAudio();
        }

        private void RefreshGameplay()
        {
            SetText(_enemyHpBarRow, OnOff(ClientPreferences.ShowEnemyHpBar));
            SetText(_minimapModeRow, (ClientPreferences.MinimapOpenMode == MinimapOpenMode.Hold)
                ? LABEL_MINIMAP_HOLD
                : LABEL_MINIMAP_TOGGLE);
        }

        private void RefreshGraphics()
        {
            // 창을 열기 전(Refresh 전)에는 해상도 목록이 비어 있다
            if (_resolutions.Count == 0)
            {
                return;
            }

            SetText(_screenModeRow, G_ScreenModeLabels[_screenModeIndex]);

            Vector2Int resolution = _resolutions[_resolutionIndex];
            SetText(_resolutionRow, resolution.x + " x " + resolution.y);
            if (_resolutionRow != null)
            {
                _resolutionRow.SetInteractable(_resolutions.Count > 1);
            }

            bool isVSync = ClientPreferences.VSync;
            SetText(_vSyncRow, OnOff(isVSync));

            int cap = (_frameCapOptions.Length > 0) ? _frameCapOptions[_frameCapIndex] : 0;
            string capLabel = (cap > 0) ? cap.ToString() : LABEL_FRAME_UNLIMITED;
            SetText(_frameCapRow, isVSync ? LABEL_FRAME_BY_VSYNC : capLabel);
            if (_frameCapRow != null)
            {
                // 수직 동기화가 켜져 있으면 Unity가 상한을 무시한다. 눌러도 달라지지 않는 줄은 막아 둔다
                _frameCapRow.SetInteractable((!isVSync) && (_frameCapOptions.Length > 1));
            }
        }

        private void RefreshAudio()
        {
            if (_masterRow != null)
            {
                _masterRow.SetNormalized(SoundVolume.Master);
            }

            if (_bgmRow != null)
            {
                _bgmRow.SetNormalized(SoundVolume.Bgm);
            }

            if (_sfxRow != null)
            {
                _sfxRow.SetNormalized(SoundVolume.Sfx);
            }

            SetText(_muteRow, OnOff(SoundVolume.IsMuted));
        }

        /****************************************
        *            Row Callbacks
        ****************************************/

        private void StepEnemyHpBar(int _)
        {
            ClientPreferences.SetShowEnemyHpBar(!ClientPreferences.ShowEnemyHpBar);
            RefreshGameplay();
        }

        private void StepMinimapMode(int _)
        {
            MinimapOpenMode next = (ClientPreferences.MinimapOpenMode == MinimapOpenMode.Hold)
                ? MinimapOpenMode.Toggle
                : MinimapOpenMode.Hold;
            ClientPreferences.SetMinimapOpenMode(next);
            RefreshGameplay();
        }

        private void StepScreenMode(int direction)
        {
            _screenModeIndex = Wrap(_screenModeIndex + direction, G_ScreenModes.Length);
            ApplyResolution();
        }

        private void StepResolution(int direction)
        {
            _resolutionIndex = Wrap(_resolutionIndex + direction, _resolutions.Count);
            ApplyResolution();
        }

        private void StepVSync(int _)
        {
            ClientPreferences.SetVSync(!ClientPreferences.VSync);
            RefreshGraphics();
        }

        private void StepFrameCap(int direction)
        {
            if (_frameCapOptions.Length == 0)
            {
                return;
            }

            _frameCapIndex = Wrap(_frameCapIndex + direction, _frameCapOptions.Length);
            ClientPreferences.SetFrameCap(_frameCapOptions[_frameCapIndex]);
            RefreshGraphics();
        }

        private void StepMute(int _)
        {
            SoundVolume.SetMuted(!SoundVolume.IsMuted);
            RefreshAudio();
        }

        private void OnMasterChanged(float value)
        {
            SoundVolume.SetMaster(value);
        }

        private void OnBgmChanged(float value)
        {
            SoundVolume.SetBgm(value);
        }

        private void OnSfxChanged(float value)
        {
            SoundVolume.SetSfx(value);
        }

        /****************************************
        *                Helpers
        ****************************************/

        /// <summary>
        /// 화면 모드와 해상도는 한 번에 건다. Unity가 다음 프레임에 반영하므로 Screen 값을
        /// 바로 다시 읽지 않고 고른 인덱스를 그대로 띄운다. 저장은 Unity가 스스로 한다
        /// </summary>
        private void ApplyResolution()
        {
            if (_resolutions.Count == 0)
            {
                return;
            }

            Vector2Int resolution = _resolutions[_resolutionIndex];
            Screen.SetResolution(resolution.x, resolution.y, G_ScreenModes[_screenModeIndex]);
            RefreshGraphics();
        }

        private void CollectResolutions()
        {
            _resolutions.Clear();

            Resolution[] available = Screen.resolutions;
            for (int i = 0; i < available.Length; ++i)
            {
                Vector2Int size = new Vector2Int(available[i].width, available[i].height);
                if (!_resolutions.Contains(size))
                {
                    _resolutions.Add(size);
                }
            }

            // 창 모드에서 손으로 늘린 크기처럼 목록에 없는 현재 크기도 고를 수 있게 남긴다
            Vector2Int current = new Vector2Int(Screen.width, Screen.height);
            _resolutionIndex = _resolutions.IndexOf(current);
            if (_resolutionIndex < 0)
            {
                _resolutions.Add(current);
                _resolutionIndex = _resolutions.Count - 1;
            }
        }

        private int FindFrameCapIndex(int cap)
        {
            int index = Array.IndexOf(_frameCapOptions, cap);
            if (index >= 0)
            {
                return index;
            }

            // 목록을 바꿔 저장값이 사라졌으면 제한 없음(0)이나 첫 칸으로 보인다
            index = Array.IndexOf(_frameCapOptions, 0);
            return Mathf.Max(0, index);
        }

        private static int Wrap(int index, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            return ((index % count) + count) % count;
        }

        private static string OnOff(bool isOn)
        {
            return isOn ? LABEL_ON : LABEL_OFF;
        }

        private static void SetText(SettingsStepperRow row, string text)
        {
            if (row != null)
            {
                row.SetValueText(text);
            }
        }

        private static void Subscribe(SettingsStepperRow row, Action<int> handler)
        {
            if (row == null)
            {
                Debug.LogError("[SettingsOptionsBinder] 선택기 줄 참조가 비어 있다");
                return;
            }

            row.Stepped += handler;
        }

        private static void Subscribe(SettingsSliderRow row, Action<float> handler)
        {
            if (row == null)
            {
                Debug.LogError("[SettingsOptionsBinder] 슬라이더 줄 참조가 비어 있다");
                return;
            }

            row.ValueChanged += handler;
        }
    }
}
