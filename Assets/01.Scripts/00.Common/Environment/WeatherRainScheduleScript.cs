using System.Collections;
using DigitalRuby.WeatherMaker;
using UnityEngine;

namespace Geumeum.Common.Environment
{
    // 시간대에 따라 전역 날씨 존의 프로필을 바꾼다. 날씨 판정은 WeatherMakerWeatherZoneScript가
    // 서버에서만 클라이언트로 전파하므로(UpdateProfile 내부에서 NetworkConnection.IsServer 확인),
    // 이 스크립트는 서버 여부를 별도로 검사하지 않아도 클라이언트에서는 자동으로 무시된다.
    [RequireComponent(typeof(WeatherMakerWeatherZoneScript))]
    public class WeatherRainScheduleScript : MonoBehaviour
    {
        [SerializeField] private WeatherScheduleConfig config;
        [SerializeField] private WeatherMakerDayNightCycleManagerScript dayNightCycle;

        private WeatherMakerWeatherZoneScript weatherZone;
        private bool isRaining;

        private void Awake()
        {
            weatherZone = GetComponent<WeatherMakerWeatherZoneScript>();
        }

        private void OnEnable()
        {
            StartCoroutine(ScheduleLoop());
        }

        private IEnumerator ScheduleLoop()
        {
            WaitForSeconds wait = new WaitForSeconds(config.CheckIntervalSeconds);
            float rainStartSeconds = config.RainStartHour * 3600f;
            float rainEndSeconds = config.RainEndHour * 3600f;

            while (true)
            {
                float timeOfDay = dayNightCycle.TimeOfDay;
                bool shouldRain = timeOfDay >= rainStartSeconds && timeOfDay < rainEndSeconds;

                if (shouldRain != isRaining)
                {
                    isRaining = shouldRain;
                    weatherZone.SingleProfile = isRaining ? config.RainProfile : config.ClearProfile;
                }

                yield return wait;
            }
        }
    }
}
