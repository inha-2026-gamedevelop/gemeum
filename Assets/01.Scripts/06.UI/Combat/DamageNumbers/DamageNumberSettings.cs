// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Data;

namespace Geumeum.UI
{
    /// <summary>
    /// 데미지 숫자 켜기/끄기 사용자 설정(COMBAT_STYLE_DESIGN S10·9장). 이 기기에만 저장하고 판정·계정과 무관하다
    /// 설정 UI는 Enabled를 읽고 쓰기만 하면 된다. 끄면 떠 있던 숫자도 바로 거둔다
    /// </summary>
    public static class DamageNumberSettings
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string PREF_ENABLED = "UI.DamageNumbers.Enabled";

        /****************************************
        *                Fields
        ****************************************/

        private static bool _isLoaded = false;
        private static bool _enabled  = true;

        /****************************************
        *              Events
        ****************************************/

        /// <summary> 설정이 실제로 바뀌었을 때. 인자는 새 값 </summary>
        public static event Action<bool> EnabledChanged;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 내가 준 피해 숫자를 보일지. 저장값이 없으면 PresentationConfigSO의 기본값 </summary>
        public static bool Enabled
        {
            get
            {
                EnsureLoaded();
                return _enabled;
            }
            set
            {
                EnsureLoaded();

                PlayerPrefs.SetInt(PREF_ENABLED, (value) ? 1 : 0);
                PlayerPrefs.Save();

                if (_enabled == value)
                {
                    return;
                }

                _enabled = value;
                EnabledChanged?.Invoke(value);
            }
        }

        /// <summary> 저장된 값이 있는지. 없으면 기본값을 따르고 있다 </summary>
        public static bool HasSavedValue => PlayerPrefs.HasKey(PREF_ENABLED);

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 저장값을 지우고 기본값으로 돌린다 </summary>
        public static void ResetToDefault()
        {
            PlayerPrefs.DeleteKey(PREF_ENABLED);
            PlayerPrefs.Save();

            bool previous = _enabled;
            _isLoaded = false;

            if (Enabled != previous)
            {
                EnabledChanged?.Invoke(_enabled);
            }
        }

        /// <summary> 도메인 리로드 OFF 대비 정적 상태 초기화 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            _isLoaded      = false;
            _enabled       = true;
            EnabledChanged = null;
        }

        /****************************************
        *                Methods
        ****************************************/

        private static void EnsureLoaded()
        {
            if (_isLoaded)
            {
                return;
            }

            bool defaultValue = GetDefault();

            // GameDB가 아직 없으면 기본값이 확정되지 않았으므로 캐시하지 않고 다음에 다시 읽는다
            if (PlayerPrefs.HasKey(PREF_ENABLED))
            {
                _enabled  = (PlayerPrefs.GetInt(PREF_ENABLED, 1) != 0);
                _isLoaded = true;
                return;
            }

            _enabled  = defaultValue;
            _isLoaded = GameDB.IsInitialized;
        }

        private static bool GetDefault()
        {
            if ((!GameDB.IsInitialized) || (GameDB.Presentation == null))
            {
                return true;
            }

            return GameDB.Presentation.DamageNumbersEnabledByDefault;
        }
    }
}
