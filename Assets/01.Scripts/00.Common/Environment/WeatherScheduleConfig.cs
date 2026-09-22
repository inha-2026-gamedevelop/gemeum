using DigitalRuby.WeatherMaker;
using UnityEngine;

namespace Geumeum.Common.Environment
{
    /// <summary> 게임 시각에 따라 전역 날씨 프로필을 전환하는 구간과 확인 주기 </summary>
    [CreateAssetMenu(fileName = "WeatherScheduleConfig", menuName = "그믐/환경/날씨 스케줄")]
    public class WeatherScheduleConfig : ScriptableObject
    {
        [Header("비가 내리는 게임 내 시각 (0~24시)")]
        public float RainStartHour = 12f;
        public float RainEndHour = 14f;

        [Header("전역 날씨 존에 적용할 프로필")]
        public WeatherMakerProfileScript RainProfile;
        public WeatherMakerProfileScript ClearProfile;

        [Header("시각 확인 주기")]
        public float CheckIntervalSeconds = 5f;
    }
}
