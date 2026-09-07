// Unity
using UnityEngine;
using UnityEngine.Audio;

// 프로젝트 내부
using Geumeum.Data;

namespace Geumeum.Common
{
    /// <summary> 사용자 볼륨 설정. 0~1 값을 믹서의 노출 파라미터(dB)로 옮기고 기기에 저장한다 </summary>
    public static class SoundVolume
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 전체 볼륨 (0~1). 음소거는 0이다 </summary>
        public static float Master
        {
            get { return Get(Constants.Sound.PREF_MASTER_VOLUME, Constants.Sound.DEFAULT_MASTER_VOLUME); }
        }

        /// <summary> 배경음 볼륨 (0~1) </summary>
        public static float Bgm
        {
            get { return Get(Constants.Sound.PREF_BGM_VOLUME, Constants.Sound.DEFAULT_BGM_VOLUME); }
        }

        /// <summary> 효과음 볼륨 (0~1). SFX 그룹 전체에 걸리고 Local, Remote의 차이는 그대로 남는다 </summary>
        public static float Sfx
        {
            get { return Get(Constants.Sound.PREF_SFX_VOLUME, Constants.Sound.DEFAULT_SFX_VOLUME); }
        }

        /// <summary> 음소거 상태. 슬라이더 값은 그대로 두고 전체만 끈다 </summary>
        public static bool IsMuted
        {
            get { return (PlayerPrefs.GetInt(Constants.Sound.PREF_MUTED, 0) != 0); }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 전체 볼륨을 바꾼다 (0~1). 설정 UI 슬라이더가 호출한다 </summary>
        public static bool SetMaster(float value)
        {
            PlayerPrefs.SetFloat(Constants.Sound.PREF_MASTER_VOLUME, Mathf.Clamp01(value));
            return ApplyMaster();
        }

        /// <summary> 배경음 볼륨을 바꾼다 (0~1) </summary>
        public static bool SetBgm(float value)
        {
            PlayerPrefs.SetFloat(Constants.Sound.PREF_BGM_VOLUME, Mathf.Clamp01(value));
            return Push(Constants.Sound.PARAM_BGM_VOLUME, Bgm);
        }

        /// <summary> 효과음 볼륨을 바꾼다 (0~1) </summary>
        public static bool SetSfx(float value)
        {
            PlayerPrefs.SetFloat(Constants.Sound.PREF_SFX_VOLUME, Mathf.Clamp01(value));
            return Push(Constants.Sound.PARAM_SFX_VOLUME, Sfx);
        }

        /// <summary> 음소거를 켜고 끈다. 슬라이더 값은 건드리지 않는다 </summary>
        public static bool SetMuted(bool muted)
        {
            PlayerPrefs.SetInt(Constants.Sound.PREF_MUTED, (muted) ? 1 : 0);
            return ApplyMaster();
        }

        /// <summary> 저장된 볼륨 셋을 믹서에 밀어 넣는다. 저장하지 않는다 </summary>
        public static bool Apply()
        {
            bool ok = ApplyMaster();
            ok &= Push(Constants.Sound.PARAM_BGM_VOLUME, Bgm);
            ok &= Push(Constants.Sound.PARAM_SFX_VOLUME, Sfx);

            return ok;
        }

        private static bool ApplyMaster()
        {
            return Push(Constants.Sound.PARAM_MASTER_VOLUME, (IsMuted) ? 0f : Master);
        }

        /// <summary> 저장을 디스크에 확정한다 </summary>
        public static void Flush()
        {
            PlayerPrefs.Save();
        }

        /// <summary> 0~1을 dB로 바꾼다 </summary>
        public static float ToDecibel(float value)
        {
            float clamped = Mathf.Clamp(value, 0f, 1f);

            if (clamped <= Constants.Sound.MIN_VOLUME)
            {
                return Constants.Sound.MIN_VOLUME_DB;
            }

            return Mathf.Clamp(Mathf.Log10(clamped) * 20f, Constants.Sound.MIN_VOLUME_DB, Constants.Sound.MAX_VOLUME_DB);
        }

        /// <summary> dB를 0~1로 되돌린다 </summary>
        public static float ToLinear(float decibel)
        {
            if (decibel <= Constants.Sound.MIN_VOLUME_DB)
            {
                return 0f;
            }

            return Mathf.Clamp01(Mathf.Pow(10f, decibel / 20f));
        }

        private static float Get(string key, float defaultValue)
        {
            return Mathf.Clamp01(PlayerPrefs.GetFloat(key, defaultValue));
        }

        private static bool Push(string parameter, float value)
        {
            AudioMixer mixer = GetMixer();

            if (mixer == null)
            {
                return false;
            }

            if (!mixer.SetFloat(parameter, ToDecibel(Mathf.Clamp01(value))))
            {
                if (Application.isPlaying)
                {
                    Debug.LogError($"[SoundVolume] 믹서에 노출되지 않은 파라미터다: {parameter}");
                }

                return false;
            }

            return true;
        }

        private static AudioMixer GetMixer()
        {
            if (!GameDB.IsInitialized)
            {
                Debug.LogWarning("[SoundVolume] GameDB 초기화 전에 볼륨을 바꾸려 했다");
                return null;
            }

            return GameDB.Sound.Mixer;
        }
    }
}
