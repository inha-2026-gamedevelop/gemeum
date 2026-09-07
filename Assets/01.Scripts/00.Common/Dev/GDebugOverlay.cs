// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> GDebug가 쌓은 줄을 화면 오른쪽 가운데에 그린다 </summary>
    public class GDebugOverlay : PersistentSingleton<GDebugOverlay>
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string OBJECT_NAME = "@GDebugOverlay";

        private const float PANEL_WIDTH  = 380f; // 화면 오른쪽에서 차지하는 폭(px)
        private const float RIGHT_MARGIN = 12f;  // 화면 오른쪽 끝과의 간격(px)
        private const float LINE_HEIGHT  = 20f;  // 한 줄 높이(px)
        private const float PADDING      = 8f;   // 배경과 글자 사이(px)

        private const float HOLD_SECONDS = 6f;   // 이 시간 동안은 또렷하게 보인다
        private const float FADE_SECONDS = 2f;   // 그 뒤 이만큼에 걸쳐 사라진다

        private const int   FONT_SIZE    = 13;

        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("화면 표시를 껐다 켜는 키")]
        [SerializeField] private KeyCode _toggleKey = KeyCode.F8;

        [Tooltip("화면을 비우는 키")]
        [SerializeField] private KeyCode _clearKey = KeyCode.F9;

        private GUIStyle  _lineStyle;
        private Texture2D _background;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnSingletonAwake()
        {
            // 1x1 단색으로 배경을 깐다. 밝은 지형 위에서 흰 글자가 안 읽히는 것을 막는다
            _background = new Texture2D(1, 1);
            _background.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.55f));
            _background.Apply();
        }

        protected override void OnSingletonDestroy()
        {
            if (_background != null)
            {
                Destroy(_background);
                _background = null;
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(_toggleKey))
            {
                GDebug.IsVisible = !GDebug.IsVisible;
            }

            if (Input.GetKeyDown(_clearKey))
            {
                GDebug.Clear();
            }
        }

        private void OnGUI()
        {
            if ((!GDebug.IsVisible) || (GDebug.Count == 0))
            {
                return;
            }

            EnsureStyle();

            int   count       = GDebug.Count;
            float panelHeight = (count * LINE_HEIGHT) + (PADDING * 2f);
            float left        = Screen.width - PANEL_WIDTH - RIGHT_MARGIN;
            float top         = (Screen.height - panelHeight) * 0.5f;   // 오른쪽 가운데

            GUI.DrawTexture(new Rect(left, top, PANEL_WIDTH, panelHeight), _background);

            float now = Time.unscaledTime;

            // 오래된 것이 위, 새 것이 아래다. 채팅창처럼 아래에서 올라온다
            for (int i = 0; i < count; ++i)
            {
                GDebug.Entry entry = GDebug.GetEntry(i);

                float alpha = ResolveAlpha(now - entry.UnscaledTime);
                if (alpha <= 0f)
                {
                    continue;
                }

                Color color = ResolveColor(entry.Kind);
                color.a     = alpha;

                _lineStyle.normal.textColor = color;

                var rect = new Rect(
                    left + PADDING,
                    top + PADDING + (i * LINE_HEIGHT),
                    PANEL_WIDTH - (PADDING * 2f),
                    LINE_HEIGHT);

                GUI.Label(rect, BuildLine(entry), _lineStyle);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 씬에 배치하지 않아도 첫 로그에서 스스로 생긴다 </summary>
        public static void EnsureExists()
        {
            // 플레이 중이 아니면 만들지 않는다. DontDestroyOnLoad가 에디트 모드에서 예외를 낸다.
            // 로그는 그래도 버퍼에 쌓이므로 에디터 스크립트에서 불러도 안전하다
            if ((!Application.isPlaying) || (HasInstance))
            {
                return;
            }

            EnsureCreated(OBJECT_NAME);
        }

        private void EnsureStyle()
        {
            if (_lineStyle != null)
            {
                return;
            }

            // GUIStyle을 OnGUI마다 새로 만들면 프레임마다 할당이 생긴다
            _lineStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = FONT_SIZE,
                alignment = TextAnchor.MiddleLeft,
                richText  = false,
            };
        }

        private static string BuildLine(in GDebug.Entry entry)
        {
            string body = ResolvePrefix(entry.Kind) + entry.Message;

            if (entry.Repeat <= 1)
            {
                return body;
            }

            return body + "  x" + entry.Repeat;
        }

        private static string ResolvePrefix(GLogKind kind)
        {
            switch (kind)
            {
                case GLogKind.Success: return "[성공] ";
                case GLogKind.Fail:    return "[실패] ";
                case GLogKind.Warn:    return "[주의] ";
                case GLogKind.Server:  return "[서버] ";
                default:               return "";
            }
        }

        private static Color ResolveColor(GLogKind kind)
        {
            switch (kind)
            {
                case GLogKind.Success: return new Color(0.45f, 1f, 0.55f);
                case GLogKind.Fail:    return new Color(1f, 0.45f, 0.45f);
                case GLogKind.Warn:    return new Color(1f, 0.85f, 0.35f);
                case GLogKind.Server:  return new Color(0.55f, 0.8f, 1f);
                default:               return Color.white;
            }
        }

        /// <summary> 오래된 줄은 서서히 사라진다. 화면을 계속 덮고 있으면 전투가 안 보인다 </summary>
        private static float ResolveAlpha(float age)
        {
            if (age <= HOLD_SECONDS)
            {
                return 1f;
            }

            return Mathf.Clamp01(1f - ((age - HOLD_SECONDS) / FADE_SECONDS));
        }
    }
}
