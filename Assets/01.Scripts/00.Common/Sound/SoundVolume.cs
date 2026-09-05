// Unity
using UnityEngine;
using UnityEngine.Audio;

// 프로젝트 내부
using Geumeum.Data;

namespace Geumeum.Common
{
    /// <summary>
    /// 사용자 볼륨 설정. 0~1 값을 믹서의 노출 파라미터(dB)로 옮기고 기기에 저장한다.
    /// 개별 AudioSource의 볼륨을 건드리지 않으므로 SFX_Remote의 상대 오프셋이 유지된다.
    /// UI는 Set 계열만 호출하면 되고 저장과 복원은 여기가 한다
    /// </summary>
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

        /// <summary>
        /// 음소거를 켜고 끈다. 슬라이더 값은 건드리지 않으므로 다시 켜면 원래 크기로 돌아온다.
        /// 볼륨을 0으로 내리는 것과는 다른 상태다
        /// </summary>
        public static bool SetMuted(bool muted)
        {
            PlayerPrefs.SetInt(Constants.Sound.PREF_MUTED, (muted) ? 1 : 0);
            return ApplyMaster();
        }

        /// <summary>
        /// 저장된 볼륨 셋을 믹서에 밀어 넣는다. **저장하지 않는다.**
        /// 저장까지 하면 슬라이더를 한 번도 안 건드린 사람에게도 기본값이 박혀,
        /// 나중에 기본값을 조정해도 이미 게임을 켜 본 사람에게는 반영되지 않는다.
        /// 게임 시작 시 SoundBootstrap이 첫 프레임에 한 번 부른다
        /// </summary>
        public static bool Apply()
        {
            bool ok = ApplyMaster();
            ok &= Push(Constants.Sound.PARAM_BGM_VOLUME, Bgm);
            ok &= Push(Constants.Sound.PARAM_SFX_VOLUME, Sfx);

            return ok;
        }

        // 음소거는 마스터 한 곳에서만 적용한다. 셋을 각각 끄면 다시 켤 때 순서에 따라 값이 어긋난다
        private static bool ApplyMaster()
        {
            return Push(Constants.Sound.PARAM_MASTER_VOLUME, (IsMuted) ? 0f : Master);
        }

        /// <summary>
        /// 저장을 디스크에 확정한다.
        /// Set 계열은 값만 써 두므로 설정 창을 닫을 때 한 번 부른다, 부르지 않아도 종료 시 Unity가 저장한다
        /// </summary>
        public static void Flush()
        {
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 0~1을 dB로 바꾼다.
        /// 사람 귀는 로그로 들으므로 선형 값을 그대로 dB에 넣으면 슬라이더 절반에서 거의 안 줄어든 것처럼 들린다
        /// </summary>
        public static float ToDecibel(float value)
        {
            float clamped = Mathf.Clamp(value, 0f, 1f);

            // 0을 그대로 로그에 넣으면 음의 무한대가 된다
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

        // 믹서에만 넣는다. 저장은 Set 계열이 각자 한다
        private static bool Push(string parameter, float value)
        {
            AudioMixer mixer = GetMixer();

            if (mixer == null)
            {
                return false;
            }

            // 믹서 파라미터는 플레이 중에만 바뀐다. 에디터에서 부르면 여기서 false가 된다.
            // 플레이 중인데도 false면 그때는 노출 파라미터 이름이 믹서와 어긋난 것이다
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
