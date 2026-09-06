namespace Geumeum.Common
{
    public static partial class Constants
    {
        /// <summary> 사운드 계약값. 크로스페이드 길이, 볼륨 같은 연출 수치는 SoundCatalogSO에 있다 </summary>
        public static class Sound
        {
            public const int SFX_SOURCE_COUNT = 16; // 동시에 울릴 수 있는 효과음 수, 넘치면 가장 오래된 소스가 재설정된다

            // GeumeumMixer.mixer에 노출된 파라미터 이름. 오타는 SetFloat이 false를 돌려주는 것으로만 드러나므로 여기서 한 번만 적는다
            public const string PARAM_MASTER_VOLUME = "MasterVolume";
            public const string PARAM_BGM_VOLUME    = "BgmVolume";
            public const string PARAM_SFX_VOLUME    = "SfxVolume";

            // 볼륨 저장 키. 런 상태와 달리 이건 기기에 남는다
            public const string PREF_MASTER_VOLUME = "Sound.Master";
            public const string PREF_BGM_VOLUME    = "Sound.Bgm";
            public const string PREF_SFX_VOLUME    = "Sound.Sfx";

            // 음소거는 볼륨 0과 다른 상태다. 끄기 전 값을 잃지 않아야 다시 켤 수 있다
            public const string PREF_MUTED = "Sound.Muted";

            public const float DEFAULT_MASTER_VOLUME = 1f;
            public const float DEFAULT_BGM_VOLUME    = 0.7f; // BGM이 효과음을 덮지 않게 처음부터 낮춰 둔다
            public const float DEFAULT_SFX_VOLUME    = 1f;

            // 믹서 볼륨은 dB다. 0을 그대로 로그로 바꾸면 음의 무한대가 되므로 하한을 둔다
            public const float MIN_VOLUME_DB = -80f;
            public const float MAX_VOLUME_DB = 0f;
            public const float MIN_VOLUME    = 0.0001f;
        }
    }
}
