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

        // 컴포넌트를 껐다 켜면 OnEnable이 다시 불린다. 핸들이 없으면 이전 루프가 살아 있는 채로
        // 하나가 더 붙어 두 루프가 같은 프로필을 서로 덮어쓴다
        private Coroutine _scheduleRoutine;

        private void Awake()
        {
            weatherZone = GetComponent<WeatherMakerWeatherZoneScript>();
        }

        private void OnEnable()
        {
            StartSchedule();
        }

        private void OnDisable()
        {
            StopSchedule();
        }

        private void StartSchedule()
        {
            StopSchedule();

            if (!HasRequiredReferences())
            {
                return;
            }

            _scheduleRoutine = StartCoroutine(ScheduleLoop());
        }

        private void StopSchedule()
        {
            if (_scheduleRoutine == null)
            {
                return;
            }

            StopCoroutine(_scheduleRoutine);
            _scheduleRoutine = null;
        }

        // 참조가 비면 루프 첫 회에 NullReference로 죽는다. 시작 전에 걸러 무엇이 비었는지 남긴다
        private bool HasRequiredReferences()
        {
            if (config == null)
            {
                Debug.LogError("[WeatherRainSchedule] WeatherScheduleConfig가 비어 있어 일정을 시작하지 않는다", this);
                return false;
            }

            if (dayNightCycle == null)
            {
                Debug.LogError("[WeatherRainSchedule] 낮밤 주기 참조가 비어 있어 일정을 시작하지 않는다", this);
                return false;
            }

            if (weatherZone == null)
            {
                Debug.LogError("[WeatherRainSchedule] WeatherMakerWeatherZoneScript를 찾지 못했다", this);
                return false;
            }

            // 0 이하면 WaitForSeconds가 매 프레임 깨어나 확인 주기가 사라진다
            if (config.CheckIntervalSeconds <= 0f)
            {
                Debug.LogError($"[WeatherRainSchedule] 확인 주기가 0 이하다: {config.CheckIntervalSeconds}", this);
                return false;
            }

            return true;
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
