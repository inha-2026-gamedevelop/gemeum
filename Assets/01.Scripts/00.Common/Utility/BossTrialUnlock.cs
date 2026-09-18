// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 보스 트라이 해금 상태. 런을 한 번이라도 시작했으면 열리고, 실행 인자로는 무조건 열린다.
    /// 이 프로젝트에서 유일하게 남는 로컬 값이라 bool 하나로 끝낸다
    /// </summary>
    public static class BossTrialUnlock
    {
        private const string PREFS_KEY    = "geumeum.bossTrial.unlocked";
        private const string COMMAND_LINE = "-bosstrial";

        /// <summary> 전시와 심사에서는 인자로 바로 연다. 그 외에는 런을 한 번 시작한 기록을 본다 </summary>
        public static bool IsUnlocked => (HasCommandLineOverride() || (PlayerPrefs.GetInt(PREFS_KEY, 0) != 0));

        public static bool HasCommandLineOverride()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; ++i)
            {
                if (string.Equals(args[i], COMMAND_LINE, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary> 런이 시작되면 해금한다. 이미 열려 있으면 다시 쓰지 않는다 </summary>
        public static void MarkRunStarted()
        {
            if (PlayerPrefs.GetInt(PREFS_KEY, 0) != 0)
            {
                return;
            }

            PlayerPrefs.SetInt(PREFS_KEY, 1);
            PlayerPrefs.Save();
        }
    }
}
