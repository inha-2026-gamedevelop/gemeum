using DigitalRuby.WeatherMaker;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Geumeum.Common.Environment
{
    [RequireComponent(typeof(WeatherMakerDayNightCycleManagerScript))]
    public class WeatherTimeDebugScript : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD

        [Header("배속 (누르고 있는 동안 적용)")]
        [SerializeField] private Key fastForwardKey = Key.Tab;
        [SerializeField] private float fastForwardMultiplier = 30f;

        [Header("시간대 즉시 이동")]
        [SerializeField] private Key morningKey = Key.Digit1;
        [SerializeField] private Key noonKey = Key.Digit2;
        [SerializeField] private Key eveningKey = Key.Digit3;
        [SerializeField] private Key nightKey = Key.Digit4;

        private WeatherMakerDayNightCycleManagerScript dayNightCycle;
        private float baseSpeed;

        private void Awake()
        {
            dayNightCycle = GetComponent<WeatherMakerDayNightCycleManagerScript>();
            baseSpeed = (dayNightCycle.Speed > 0f ? dayNightCycle.Speed : dayNightCycle.NightSpeed);
        }

        private void Update()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            HandleFastForward();
            HandleTimeJump();
        }

        private void HandleFastForward()
        {
            if (!Keyboard.current[fastForwardKey].isPressed)
            {
                return;
            }

            const float secondsPerDay = 86400f;
            float next = dayNightCycle.TimeOfDay + (baseSpeed * fastForwardMultiplier * Time.unscaledDeltaTime);
            dayNightCycle.TimeOfDay = next % secondsPerDay;
        }

        private void HandleTimeJump()
        {
            if (Keyboard.current[morningKey].wasPressedThisFrame) dayNightCycle.TimeOfDay = 6f * 3600f;
            else if (Keyboard.current[noonKey].wasPressedThisFrame) dayNightCycle.TimeOfDay = 12f * 3600f;
            else if (Keyboard.current[eveningKey].wasPressedThisFrame) dayNightCycle.TimeOfDay = 18f * 3600f;
            else if (Keyboard.current[nightKey].wasPressedThisFrame) dayNightCycle.TimeOfDay = 22f * 3600f;
        }

#endif
    }
}
