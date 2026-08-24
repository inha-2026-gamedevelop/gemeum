using DigitalRuby.WeatherMaker;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Geumeum.Common.Environment
{
    // 테스트용 시간대 단축키. 개발 빌드/에디터에서만 동작한다.
    // Weather Maker는 시간 진행을 서버 권위로만 처리하므로(클라이언트는 RPC로 받음),
    // 호스트가 아닌 클라이언트에서 눌러도 무시된다.
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
            if (Keyboard.current == null || !IsServerAuthoritative())
            {
                return;
            }

            HandleFastForward();
            HandleTimeJump();
        }

        private bool IsServerAuthoritative()
        {
            // 실제 Mirror 세션에 연결되어 있지 않으면(솔로 에디터 테스트) 그냥 허용하고,
            // 연결되어 있으면 서버(호스트)에서만 허용해 클라이언트가 시간을 임의로 바꾸지 못하게 한다.
            if (WeatherMakerScript.Instance == null)
            {
                return true;
            }

            var connection = WeatherMakerScript.Instance.NetworkConnection;
            return !connection.IsConnected || connection.IsServer;
        }

        private void HandleFastForward()
        {
            // Speed/NightSpeed 배율 대신 TimeOfDay를 직접 증가시킨다. 원본 Speed는 서버 권위일 때만
            // 자동 적용되지만(WeatherMakerDayNightCycleManagerScript.Update), 이 값은 그 제약과 무관하게
            // 항상 즉시 반영되므로 호스트를 띄우지 않은 솔로 테스트에서도 동작한다.
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
