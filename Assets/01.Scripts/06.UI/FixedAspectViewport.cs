// System
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Geumeum.UI
{
    /// <summary> 창 크기와 무관하게 게임/화면 UI를 16:9 안에 배치한다. 로컬 표현 전용 </summary>
    public sealed class FixedAspectViewport : MonoBehaviour
    {
        public const float ASPECT = 16f / 9f;
        private static FixedAspectViewport _instance;
        private readonly List<RectTransform> _contents = new List<RectTransform>();
        private readonly List<Canvas> _canvases = new List<Canvas>();
        private readonly RectTransform[] _bars = new RectTransform[4];
        private int _width;
        private int _height;
        private Canvas _barCanvas;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { return; }
            var root = new GameObject("FixedAspectViewport");
            DontDestroyOnLoad(root);
            _instance = root.AddComponent<FixedAspectViewport>();
        }

        public static Rect CalculateRect(int width, int height)
        {
            if (width <= 0 || height <= 0) { return new Rect(0f, 0f, 1f, 1f); }
            float ratio = (float)width / height;
            if (ratio > ASPECT)
            {
                float fraction = ASPECT / ratio;
                return new Rect((1f - fraction) * 0.5f, 0f, fraction, 1f);
            }
            float vertical = ratio / ASPECT;
            return new Rect(0f, (1f - vertical) * 0.5f, 1f, vertical);
        }

        /// <summary> 런타임에 생성하는 루트 Canvas는 자식 구성을 마친 뒤 호출한다 </summary>
        public static void Register(Canvas canvas)
        {
            if (_instance == null || canvas == null || !canvas.isRootCanvas ||
                canvas.renderMode != RenderMode.ScreenSpaceOverlay || canvas.targetDisplay != 0 ||
                canvas == _instance._barCanvas || _instance._canvases.Contains(canvas)) { return; }

            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
            {
                // 기존 기준 해상도에서의 스케일은 유지하고, 여백 방향으로만 캔버스를 확장한다
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            }
            var content = new GameObject("AspectContent", typeof(RectTransform), typeof(RectMask2D));
            content.layer = canvas.gameObject.layer;
            RectTransform rect = (RectTransform)content.transform;
            rect.SetParent(canvas.transform, false);
            int childCount = canvas.transform.childCount - 1;
            for (int i = 0; i < childCount; ++i)
            {
                canvas.transform.GetChild(0).SetParent(rect, false);
            }
            SetRect(rect, CalculateRect(Screen.width, Screen.height));
            _instance._canvases.Add(canvas);
            _instance._contents.Add(rect);
        }

        private void Awake()
        {
            var overlay = new GameObject("BlackBars", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            overlay.transform.SetParent(transform, false);
            _barCanvas = overlay.GetComponent<Canvas>();
            _barCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _barCanvas.sortingOrder = short.MaxValue;
            for (int i = 0; i < _bars.Length; ++i)
            {
                var bar = new GameObject("Bar" + i, typeof(RectTransform), typeof(Image));
                bar.transform.SetParent(overlay.transform, false);
                bar.GetComponent<Image>().color = Color.black;
                _bars[i] = (RectTransform)bar.transform;
            }
            SceneManager.sceneLoaded += OnSceneLoaded;
            RenderPipelineManager.beginContextRendering += BeforeRender;
            RefreshLayout();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            RenderPipelineManager.beginContextRendering -= BeforeRender;
            if (_instance == this) { _instance = null; }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // 씬 전환 시에만 탐색한다. 매 프레임 오브젝트 검색은 하지 않는다
            foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                Register(canvas);
            }
            RefreshLayout();
        }

        private void Update()
        {
            // OS 창 리사이즈에는 Unity 이벤트가 없으므로 크기만 감시한다. 정지 중에도 동작한다
            if (_width != Screen.width || _height != Screen.height) { RefreshLayout(); }
        }

        private void OnGUI()
        {
            // 개발용 IMGUI 진단도 게임 영역 밖으로 새지 않도록 마지막에 여백을 덮는다
            if (Event.current.type != EventType.Repaint) { return; }
            GUI.depth = -10000;
            Color previous = GUI.color;
            GUI.color = Color.black;
            Rect rect = CalculateRect(Screen.width, Screen.height);
            float side = rect.x * Screen.width;
            float top = rect.y * Screen.height;
            GUI.DrawTexture(new Rect(0f, 0f, side, Screen.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(Screen.width - side, 0f, side, Screen.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, top), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, Screen.height - top, Screen.width, top), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void BeforeRender(ScriptableRenderContext context, List<Camera> cameras)
        {
            Rect rect = CalculateRect(Screen.width, Screen.height);
            foreach (Camera camera in cameras)
            {
                // 미니맵/반사/캡처 RenderTexture와 에디터 SceneView에는 적용하지 않는다
                if (camera.cameraType == CameraType.Game && camera.targetTexture == null && camera.targetDisplay == 0)
                {
                    camera.rect = rect;
                }
            }
        }

        private void RefreshLayout()
        {
            _width = Screen.width;
            _height = Screen.height;
            Rect rect = CalculateRect(_width, _height);
            for (int i = _contents.Count - 1; i >= 0; --i)
            {
                if (_contents[i] == null)
                {
                    _contents.RemoveAt(i);
                    _canvases.RemoveAt(i);
                    continue;
                }
                SetRect(_contents[i], rect);
            }
            SetRect(_bars[0], new Rect(0f, 0f, rect.xMin, 1f));
            SetRect(_bars[1], new Rect(rect.xMax, 0f, 1f - rect.xMax, 1f));
            SetRect(_bars[2], new Rect(0f, 0f, 1f, rect.yMin));
            SetRect(_bars[3], new Rect(0f, rect.yMax, 1f, 1f - rect.yMax));
        }

        private static void SetRect(RectTransform target, Rect rect)
        {
            target.anchorMin = rect.min;
            target.anchorMax = rect.max;
            target.offsetMin = Vector2.zero;
            target.offsetMax = Vector2.zero;
        }
    }
}
