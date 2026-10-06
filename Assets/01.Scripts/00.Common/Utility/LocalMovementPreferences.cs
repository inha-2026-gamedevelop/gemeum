// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 이 기기에서만 쓰는 이동 입력 설정. 서버에는 해석을 마친 달리기 의도(SprintHeld)만 간다.
    /// 전투(PlayerController)가 읽고 설정 창이 바꾸므로 UI가 아니라 공용 계층에 둔다
    /// </summary>
    public static class LocalMovementPreferences
    {
        /****************************************
        *                Fields
        ****************************************/

        // PlayerPrefs 키. 바꾸면 기존 사용자의 설정이 초기화된다
        private const string PREF_DEFAULT_RUN = "Input.DefaultRun";

        // 키가 없는 사용자(설정을 한 번도 바꾸지 않은 기존 사용자 포함)는 기본 달리기로 시작한다.
        // 시작할 때 기본값을 저장하지 않으므로 나중에 기본값을 바꾸면 그대로 따라간다
        private const int DEFAULT_RUN = 1;

        /// <summary> 설정이 바뀌었을 때 </summary>
        public static event Action Changed;

        // 로컬 플레이어가 매 프레임 읽으므로 처음 한 번만 PlayerPrefs에서 꺼내 둔다
        private static bool _isLoaded;
        private static bool _defaultRun;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 켜져 있으면 평소에 달리고 달리기 키를 누르는 동안 걷는다 </summary>
        public static bool DefaultRun
        {
            get
            {
                EnsureLoaded();
                return _defaultRun;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> PlayerPrefs에 넣기만 한다. 디스크 확정은 설정 창을 닫을 때 한 번 한다 </summary>
        public static void SetDefaultRun(bool isOn)
        {
            EnsureLoaded();
            _defaultRun = isOn;
            PlayerPrefs.SetInt(PREF_DEFAULT_RUN, isOn ? 1 : 0);
            Changed?.Invoke();
        }

        /// <summary>
        /// 기본 달리기 설정과 키 입력을 실제 달리기 의도로 바꾼다.
        /// 기본 달리기면 키를 누르는 동안 걷고, 끄면 키를 누르는 동안만 달린다
        /// </summary>
        public static bool ResolveRunIntent(bool defaultRun, bool swapKeyHeld)
        {
            return (defaultRun != swapKeyHeld);
        }

        /// <summary> 캐시를 버리고 다음 읽기에서 PlayerPrefs를 다시 읽는다. 저장값을 직접 바꾼 테스트가 부른다 </summary>
        public static void Reload()
        {
            _isLoaded = false;
        }

        // 도메인 리로드를 끈 에디터에서 지난 플레이의 구독자와 캐시가 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            Changed   = null;
            _isLoaded = false;
        }

        private static void EnsureLoaded()
        {
            if (_isLoaded)
            {
                return;
            }

            _isLoaded   = true;
            _defaultRun = (PlayerPrefs.GetInt(PREF_DEFAULT_RUN, DEFAULT_RUN) != 0);
        }
    }
}
