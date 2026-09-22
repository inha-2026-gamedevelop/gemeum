// System
using System;
using System.Globalization;
using System.IO;

// Unity
using UnityEngine;

namespace Geumeum.Dev
{
    /// <summary>
    /// 플레이테스트를 돌릴 때 손이 자주 가는 것들
    ///
    /// 게임 값이 아니라 실행 환경을 만지는 것만 둔다 -
    /// 런과 전투를 만지는 것은 테스트 실행 패널, 판정 모양은 콜라이더 패널이다
    /// </summary>
    public class PlaytestPanel : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS

        /****************************************
        *                Preload
        ****************************************/

        /// <summary> 씬에 배치하지 않아도 실행 시작에 스스로 붙는다 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Preload()
        {
            DevToolHost.Attach<PlaytestPanel>();
        }

        /****************************************
        *              Constants
        ****************************************/

        private const string PANEL_ID    = "playtest";
        private const string PANEL_TITLE = "플레이테스트";

        private const string SHOT_FOLDER = "Screenshots";

        private const float SPEED_MIN = 0.05f;
        private const float SPEED_MAX = 3f;

        // 슬라이더로 정확히 맞추기 어려운 값은 버튼으로 둔다
        private static readonly float[] G_SpeedSteps = { 0.25f, 0.5f, 1f, 2f };

        private static readonly string[] G_SpeedNames = { "0.25배", "0.5배", "1배", "2배" };

        /****************************************
        *                Fields
        ****************************************/

        private string _message = string.Empty;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            DevPanelHost.Register(PANEL_ID, PANEL_TITLE, Build, Handle);
        }

        private void OnDestroy()
        {
            DevPanelHost.Unregister(PANEL_ID);

            // 도구를 끄면 배속은 원래대로 돌려 둔다. 느린 채로 남으면 다음 테스트를 오해한다
            Time.timeScale = 1f;
        }

        /****************************************
        *                Panel
        ****************************************/

        private void Build(DevPanelBuilder builder)
        {
            builder.Label($"해상도 {Screen.width}x{Screen.height}   프레임 상한 "
                        + ((Application.targetFrameRate <= 0) ? "없음" : Application.targetFrameRate.ToString()));

            builder.Header("시간");
            builder.Label($"지금 {Time.timeScale.ToString("0.##", CultureInfo.InvariantCulture)}배"
                        + ((Time.timeScale <= 0f) ? "  (멈춤)" : string.Empty));

            builder.Choice("speedstep", "자주 쓰는 배속", DescribeSpeedStep(), G_SpeedNames);
            builder.Slider("speed", "배속 __ 배", Time.timeScale, SPEED_MIN, SPEED_MAX);
            builder.Button("pause", (Time.timeScale <= 0f) ? "다시 재생" : "일시정지");

            builder.Header("마우스");
            builder.Label((Cursor.lockState == CursorLockMode.Locked)
                ? "커서가 게임에 잡혀 있다"
                : "커서가 풀려 있다");

            builder.Button("cursor", (Cursor.lockState == CursorLockMode.Locked) ? "커서 풀기" : "커서 다시 잡기");

            builder.Header("기록");
            builder.Button("shot", "스크린샷 저장");
            builder.Label("저장 위치: " + ResolveShotFolder());

            if (!string.IsNullOrEmpty(_message))
            {
                builder.Label(_message);
            }
        }

        private void Handle(DevCommand command)
        {
            switch (command.Control)
            {
                case "speedstep":
                    ApplySpeedStep(command);
                    return;

                case "speed":
                    if (command.TryGetFloat(out float speed))
                    {
                        SetSpeed(Mathf.Clamp(speed, SPEED_MIN, SPEED_MAX));
                    }

                    return;

                case "pause":
                    SetSpeed((Time.timeScale <= 0f) ? 1f : 0f);
                    return;

                case "cursor":
                    ToggleCursor();
                    return;

                case "shot":
                    SaveScreenshot();
                    return;
            }
        }

        /****************************************
        *                Actions
        ****************************************/

        private void ApplySpeedStep(in DevCommand command)
        {
            if (!command.TryGetInt(out int index))
            {
                return;
            }

            if ((index < 0) || (index >= G_SpeedSteps.Length))
            {
                return;
            }

            SetSpeed(G_SpeedSteps[index]);
        }

        /// <summary>
        /// 배속은 물리와 애니메이션에 함께 걸린다. 히트스톱과 슬로우모션을 손으로 재현할 때 쓴다
        /// 호스트에서 바꾸면 서버 시계를 따르는 모두에게, 클라이언트에서 바꾸면 그 화면에만 걸린다
        /// </summary>
        private void SetSpeed(float speed)
        {
            Time.timeScale = speed;

            _message = (speed <= 0f)
                ? "멈췄다. 다시 재생을 누르면 1배로 돌아온다"
                : $"배속을 {speed.ToString("0.##", CultureInfo.InvariantCulture)}배로 뒀다";
        }

        private void ToggleCursor()
        {
            bool isLocked = (Cursor.lockState == CursorLockMode.Locked);

            Cursor.lockState = (isLocked) ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible   = isLocked;

            _message = (isLocked)
                ? "커서를 풀었다. 게임 창을 다시 누르면 조작이 돌아온다"
                : "커서를 다시 잡았다";
        }

        /// <summary> 버그를 말로 설명하는 것보다 그림 한 장이 빠르다 </summary>
        private void SaveScreenshot()
        {
            string folder = ResolveShotFolder();

            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception exception)
            {
                _message = "폴더를 만들지 못했다: " + exception.Message;
                Debug.LogWarning($"[PlaytestPanel] 스크린샷 폴더 실패: {exception.Message}", this);
                return;
            }

            string name = "gemeum_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
            string path = Path.Combine(folder, name);

            // 실제 파일은 이 프레임이 끝난 뒤에 쓰인다. 바로 열어 보면 아직 없다
            ScreenCapture.CaptureScreenshot(path);

            _message = "저장 요청: " + name;
        }

        /****************************************
        *                Methods
        ****************************************/

        private static string ResolveShotFolder()
        {
            return Path.Combine(Application.persistentDataPath, SHOT_FOLDER);
        }

        private static string DescribeSpeedStep()
        {
            for (int i = 0; i < G_SpeedSteps.Length; ++i)
            {
                if (Mathf.Approximately(Time.timeScale, G_SpeedSteps[i]))
                {
                    return G_SpeedNames[i];
                }
            }

            return string.Empty;
        }

#endif
    }
}
