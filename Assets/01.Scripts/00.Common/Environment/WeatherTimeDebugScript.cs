using DigitalRuby.WeatherMaker;
using UnityEngine;

namespace Geumeum.Common.Environment
{
    [RequireComponent(typeof(WeatherMakerDayNightCycleManagerScript))]
    public class WeatherTimeDebugScript : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD

        // 시간대 즉시 이동이 맞추는 시각
        private const float SECONDS_PER_HOUR = 3600f;
        private const float MORNING_HOUR     = 6f;
        private const float NOON_HOUR        = 12f;
        private const float EVENING_HOUR     = 18f;
        private const float NIGHT_HOUR       = 22f;

        [Header("배속 (누르고 있는 동안 적용)")]
        [SerializeField] private KeyCode _fastForwardKeyCode = KeyCode.Tab;
        [SerializeField] private float fastForwardMultiplier = 30f;

        [Header("시간대 즉시 이동")]
        [SerializeField] private KeyCode _morningKeyCode = KeyCode.Alpha1;
        [SerializeField] private KeyCode _noonKeyCode    = KeyCode.Alpha2;
        [SerializeField] private KeyCode _eveningKeyCode = KeyCode.Alpha3;
        [SerializeField] private KeyCode _nightKeyCode   = KeyCode.Alpha4;

        private WeatherMakerDayNightCycleManagerScript dayNightCycle;
        private float baseSpeed;

        private void Awake()
        {
            dayNightCycle = GetComponent<WeatherMakerDayNightCycleManagerScript>();
            baseSpeed = (dayNightCycle.Speed > 0f ? dayNightCycle.Speed : dayNightCycle.NightSpeed);
        }

        private void Update()
        {
            HandleFastForward();
            HandleTimeJump();
        }

        private void HandleFastForward()
        {
            if (!Input.GetKey(_fastForwardKeyCode))
            {
                return;
            }

            const float secondsPerDay = 86400f;
            float next = dayNightCycle.TimeOfDay + (baseSpeed * fastForwardMultiplier * Time.unscaledDeltaTime);
            dayNightCycle.TimeOfDay = next % secondsPerDay;
        }

        private void HandleTimeJump()
        {
            if (Input.GetKeyDown(_morningKeyCode))
            {
                dayNightCycle.TimeOfDay = MORNING_HOUR * SECONDS_PER_HOUR;
            }
            else if (Input.GetKeyDown(_noonKeyCode))
            {
                dayNightCycle.TimeOfDay = NOON_HOUR * SECONDS_PER_HOUR;
            }
            else if (Input.GetKeyDown(_eveningKeyCode))
            {
                dayNightCycle.TimeOfDay = EVENING_HOUR * SECONDS_PER_HOUR;
            }
            else if (Input.GetKeyDown(_nightKeyCode))
            {
                dayNightCycle.TimeOfDay = NIGHT_HOUR * SECONDS_PER_HOUR;
            }
        }

#endif
    }
}
