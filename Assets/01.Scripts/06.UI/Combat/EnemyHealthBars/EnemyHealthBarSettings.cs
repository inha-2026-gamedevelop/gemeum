// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.UI
{
    /// <summary>
    /// 잡귀 머리 위 체력 바 켜기/끄기. 설정 창의 "적 체력 바" 토글(ClientPreferences.ShowEnemyHpBar)을 그대로 따른다
    /// 저장과 기본값은 ClientPreferences가 맡는다. 여기는 체력 바 층이 쓰기 편한 값과 변경 알림만 준다
    /// </summary>
    public static class EnemyHealthBarSettings
    {
        /****************************************
        *                Fields
        ****************************************/

        private static bool         _isListening = false;
        private static bool         _lastKnown   = true;
        private static Action<bool> _enabledChanged;

        /****************************************
        *              Events
        ****************************************/

        /// <summary> 설정이 실제로 바뀌었을 때. 인자는 새 값 </summary>
        public static event Action<bool> EnabledChanged
        {
            add
            {
                EnsureListening();
                _enabledChanged += value;
            }
            remove
            {
                _enabledChanged -= value;
            }
        }

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 맞은 잡귀 머리 위에 체력 바를 보일지 </summary>
        public static bool Enabled
        {
            get { return ClientPreferences.ShowEnemyHpBar; }
            set
            {
                ClientPreferences.SetShowEnemyHpBar(value);
                ClientPreferences.Save();
            }
        }

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 도메인 리로드 OFF 대비 정적 상태 초기화. ClientPreferences도 같은 때 구독자를 비운다 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            _isListening    = false;
            _lastKnown      = true;
            _enabledChanged = null;
        }

        /****************************************
        *                Methods
        ****************************************/

        private static void EnsureListening()
        {
            if (_isListening)
            {
                return;
            }

            _isListening = true;
            _lastKnown   = ClientPreferences.ShowEnemyHpBar;
            ClientPreferences.Changed += HandlePreferencesChanged;
        }

        // ClientPreferences.Changed는 표시 설정 전체에 대해 울린다. 체력 바 값이 바뀐 때만 넘긴다
        private static void HandlePreferencesChanged()
        {
            bool current = ClientPreferences.ShowEnemyHpBar;
            if (current == _lastKnown)
            {
                return;
            }

            _lastKnown = current;
            _enabledChanged?.Invoke(current);
        }
    }
}
