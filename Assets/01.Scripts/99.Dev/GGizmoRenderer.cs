// Unity
using UnityEngine;
using UnityEngine.Rendering;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Dev
{
    /// <summary>
    /// GGizmo가 쌓아 둔 선과 글자를 게임 화면에 그리고, 켜고 끄는 조작은 브라우저 패널로 보낸다.
    ///
    /// 선과 월드 글자는 월드에 붙는 표시라 게임 화면에 있어야 한다.
    /// 대신 토글·갈래·그래프처럼 읽고 누르는 것은 별도 창(브라우저)으로 뺀다.
    /// OnDrawGizmos는 에디터 Scene 뷰 전용이라 개발 빌드에서 보이지 않으므로,
    /// URP의 카메라 렌더링이 끝난 시점에 GL로 직접 선을 얹는다
    /// </summary>
    public class GGizmoRenderer : PersistentSingleton<GGizmoRenderer>
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 첫 기즈모에서 스스로 생긴다. 씬에 배치하지 않기 위한 것이다.
        /// GGizmo의 그리기 함수는 릴리스에서 호출부만 사라지고 본문은 컴파일되므로,
        /// 이 진입점은 빌드 종류와 상관없이 존재해야 한다
        /// </summary>
        public static void EnsureExists()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS
            // 플레이 중이 아니면 만들지 않는다. DontDestroyOnLoad가 에디트 모드에서 예외를 낸다.
            // 잠겨 있을 때도 만들지 않는다 - 여기는 기즈모를 찍을 때마다 불리는 자리다
            if ((!Application.isPlaying) || (HasInstance) || (!DevAccess.IsUnlocked))
            {
                return;
            }

            EnsureCreated(OBJECT_NAME);
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS

        /****************************************
        *              Constants
        ****************************************/

        private const string OBJECT_NAME = "@GGizmoRenderer";

        private const string PANEL_ID    = "gizmo";
        private const string PANEL_TITLE = "기즈모";

        private const int LABEL_FONT_SIZE = 12;

        private const float LABEL_PADDING = 6f; // 글자 폭 계산 뒤 좌우로 더 주는 여백(px)

        private const float PLOT_MIN_SPAN = 0.0001f; // 값이 안 변할 때 0으로 나누는 것을 막는다

        /****************************************
        *                Preload
        ****************************************/

        /// <summary> 기즈모를 아무도 안 찍어도 패널은 보여야 한다 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Preload()
        {
            DevAccess.RunWhenUnlocked(EnsureExists);
        }

        /****************************************
        *                Fields
        ****************************************/

        private Material _material   = null;
        private GUIStyle _labelStyle = null;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnSingletonAwake()
        {
            // Hidden/Internal-Colored는 정점 색만 쓰는 유니티 내장 셰이더다. GL 즉시 그리기의 표준 재질이다
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                Debug.LogError("[GGizmoRenderer] Hidden/Internal-Colored 셰이더를 찾지 못했다", this);
            }
            else
            {
                _material = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }

            DevPanelHost.Register(PANEL_ID, PANEL_TITLE, Build, Handle);

            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        protected override void OnSingletonDestroy()
        {
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;

            DevPanelHost.Unregister(PANEL_ID);

            if (_material != null)
            {
                Destroy(_material);
                _material = null;
            }
        }

        private void Update()
        {
            // 수명이 끝난 것은 프레임 시작에 버린다. 그리기는 이 뒤에 일어난다
            GGizmo.Sweep();
        }

        private void OnGUI()
        {
            DrawWorldLabels();
        }

        /****************************************
        *              Rendering
        ****************************************/

        /// <summary>
        /// URP에서는 Camera.onPostRender가 오지 않는다.
        /// endCameraRendering에서 카메라의 행렬을 직접 걸고 GL로 선을 얹는다
        /// </summary>
        private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if ((!GGizmo.IsEnabled) || (_material == null))
            {
                return;
            }

            // 프리뷰·반사 카메라까지 그리면 캐릭터 미리보기 화면에 판정선이 묻는다
            if ((camera == null) || (camera.cameraType != CameraType.Game))
            {
                return;
            }

            int count = GGizmo.SegmentCount;
            if (count <= 0)
            {
                return;
            }

            ApplyMaterialState();

            GL.PushMatrix();
            GL.LoadProjectionMatrix(camera.projectionMatrix);
            GL.modelview = camera.worldToCameraMatrix;

            _material.SetPass(0);

            GL.Begin(GL.LINES);

            for (int i = 0; i < count; ++i)
            {
                GGizmo.Segment segment = GGizmo.GetSegment(i);

                if (!GGizmo.IsChannelVisible(segment.Channel))
                {
                    continue;
                }

                GL.Color(segment.Color);
                GL.Vertex(segment.A);
                GL.Vertex(segment.B);
            }

            GL.End();
            GL.PopMatrix();
        }

        private void ApplyMaterialState()
        {
            _material.SetInt("_SrcBlend", (int) BlendMode.SrcAlpha);
            _material.SetInt("_DstBlend", (int) BlendMode.OneMinusSrcAlpha);
            _material.SetInt("_Cull",     (int) CullMode.Off);
            _material.SetInt("_ZWrite",   0);

            // 가려짐을 켜면 지형 뒤의 선이 사라진다. 어느 쪽이 필요한지는 상황마다 달라 패널에서 고른다
            _material.SetInt("_ZTest", (int) (GGizmo.UseDepthTest
                ? CompareFunction.LessEqual
                : CompareFunction.Always));
        }

        private void DrawWorldLabels()
        {
            if ((!GGizmo.IsEnabled) || (!GGizmo.ShowLabels))
            {
                return;
            }

            int count = GGizmo.LabelCount;
            if (count <= 0)
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            EnsureLabelStyle();

            for (int i = 0; i < count; ++i)
            {
                GGizmo.Label label = GGizmo.GetLabel(i);

                if (!GGizmo.IsChannelVisible(label.Channel))
                {
                    continue;
                }

                Vector3 screen = camera.WorldToScreenPoint(label.World);

                // 카메라 뒤에 있는 것은 화면 앞으로 접혀 나와 엉뚱한 자리에 찍힌다
                if (screen.z <= 0f)
                {
                    continue;
                }

                _labelStyle.normal.textColor = label.Color;

                Vector2 size = _labelStyle.CalcSize(new GUIContent(label.Text));

                // 계산한 폭에 여백을 조금 준다. 반올림 때문에 마지막 글자가 잘리는 것을 막는다
                size.x += LABEL_PADDING;

                var rect = new Rect(
                    screen.x - (size.x * 0.5f),
                    Screen.height - screen.y - (size.y * 0.5f),
                    size.x,
                    size.y);

                GUI.Label(rect, label.Text, _labelStyle);
            }
        }

        private void EnsureLabelStyle()
        {
            if (_labelStyle != null)
            {
                return;
            }

            // GUIStyle을 OnGUI마다 새로 만들면 프레임마다 할당이 생긴다
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = LABEL_FONT_SIZE,
                alignment = TextAnchor.MiddleCenter,
                richText  = false,

                // 줄바꿈을 끈다. CalcSize 폭에 딱 맞춰 그리면 1px이 모자라 두 줄로 접히고,
                // 높이는 한 줄만큼이라 아래쪽이 잘려 깨진 글자처럼 보인다
                wordWrap  = false,
            };
        }

        /****************************************
        *                Panel
        ****************************************/

        private void Build(DevPanelBuilder builder)
        {
            builder.Toggle("enabled", "기즈모 전체 표시", GGizmo.IsEnabled);
            builder.Toggle("depth",   "지형에 가려지게",  GGizmo.UseDepthTest);
            builder.Toggle("labels",  "월드 글자 표시",   GGizmo.ShowLabels);

            builder.Header("갈래");

            for (int i = 0; i < GGizmo.CHANNEL_COUNT; ++i)
            {
                var channel = (GGizmoChannel) i;

                builder.Toggle("channel." + i, GGizmo.GetChannelName(channel),
                    GGizmo.IsChannelVisible(channel));
            }

            builder.Label($"선 {GGizmo.SegmentCount}개 · 글자 {GGizmo.LabelCount}개"
                        + (GGizmo.IsOverflowed ? "  (예산 초과)" : string.Empty));

            BuildWatches(builder);
            BuildPlots(builder);
        }

        private static void BuildWatches(DevPanelBuilder builder)
        {
            if (GGizmo.Watches.Count == 0)
            {
                return;
            }

            builder.Header("지금 값");

            for (int i = 0; i < GGizmo.Watches.Count; ++i)
            {
                GGizmo.Watch watch = GGizmo.Watches[i];
                builder.Label($"{watch.Key} : {watch.Value}");
            }
        }

        private static void BuildPlots(DevPanelBuilder builder)
        {
            if (GGizmo.Plots.Count == 0)
            {
                return;
            }

            builder.Header("값 그래프");
            builder.Button("clearplots", "그래프 지우기");

            for (int i = 0; i < GGizmo.Plots.Count; ++i)
            {
                GGizmo.Plot plot = GGizmo.Plots[i];

                int count = plot.Count;
                if (count <= 1)
                {
                    continue;
                }

                var samples = new float[count];

                float min = float.MaxValue;
                float max = float.MinValue;

                for (int sample = 0; sample < count; ++sample)
                {
                    float value = plot.Get(sample);

                    samples[sample] = value;
                    min = Mathf.Min(min, value);
                    max = Mathf.Max(max, value);
                }

                // 값이 안 변하면 위아래 폭이 0이 되어 그리는 쪽에서 0으로 나눈다
                float span = Mathf.Max(max - min, PLOT_MIN_SPAN);

                builder.Plot("plot." + plot.Key,
                    $"{plot.Key}   지금 {samples[count - 1]:0.###}   {min:0.###} ~ {max:0.###}",
                    samples, min, min + span);
            }
        }

        private void Handle(DevCommand command)
        {
            if (command.Control == "enabled")
            {
                GGizmo.IsEnabled = command.IsOn;
                return;
            }

            if (command.Control == "depth")
            {
                GGizmo.UseDepthTest = command.IsOn;
                return;
            }

            if (command.Control == "labels")
            {
                GGizmo.ShowLabels = command.IsOn;
                return;
            }

            if (command.Control == "clearplots")
            {
                GGizmo.ClearPlots();
                return;
            }

            if ((string.IsNullOrEmpty(command.Control)) || (!command.Control.StartsWith("channel.")))
            {
                return;
            }

            if (int.TryParse(command.Control.Substring("channel.".Length), out int index))
            {
                GGizmo.SetChannelVisible((GGizmoChannel) index, command.IsOn);
            }
        }

#endif
    }
}
