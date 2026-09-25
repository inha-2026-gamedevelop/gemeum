// Unity
using UnityEngine;
using UnityEngine.UI;

namespace Geumeum.Art
{
    /// <summary>
    /// 화면 가장자리 오버레이(COMBAT_STYLE_DESIGN S6, S7). URP 후처리가 꺼져 있어 UI 오버레이로 만든다
    /// 캔버스 하나에 세 겹을 둔다 - 극한 회피의 어두운 가장자리, 받아베기 먹 번짐, 타격 밝은 플래시(위로 갈수록 나중에 그린다)
    /// 가운데가 비고 가장자리만 짙은 텍스처를 한 번 절차 생성해 쓴다. 씬을 고치지 않도록 첫 재생 때 코드로 캔버스를 만든다
    /// 전부 단색 UI 오버레이다. 실제 월드 채도를 바꾸지 않는다
    /// </summary>
    public sealed class ImpactScreenFlash
    {
        /****************************************
        *              Constants
        ****************************************/

        // 기존 HUD 캔버스(RunHUD 등)가 0이라 그보다 아래에 둔다. 오버레이라 월드 위에는 그대로 그려진다
        private const int SORTING_ORDER = -20; // 데미지 숫자(-10)와 HUD(0)보다 아래

        // 텍스처 모양. 수치는 밸런스가 아니라 그림 모양이다
        private const int   TEXTURE_SIZE   = 64;
        private const float EDGE_INNER     = 0.55f; // 이 정규화 반경 안쪽은 완전히 비운다
        private const float EDGE_EXPONENT  = 4f;    // 초타원 지수. 클수록 화면 네 변을 따라 고르게 밝다

        // 먹 번짐 텍스처 모양. 가장자리가 고르지 않게 노이즈로 흔든다
        private const int   INK_TEXTURE_SIZE    = 128;
        private const float INK_INNER           = 0.5f;  // 이 정규화 반경 안쪽은 비운다
        private const float INK_OUTER           = 0.85f; // 이 반경부터 완전히 짙다. 텍스처 테두리가 반드시 짙어야 Clamp 바깥이 이어진다
        private const float INK_NOISE_AMPLITUDE = 0.12f; // 번짐 경계 흔들림 폭
        private const float INK_NOISE_FREQUENCY = 5f;    // 텍스처 한 변에 들어가는 노이즈 물결 수

        // 먹이 번지는 정도를 uvRect 확대율로 표현한다. 1보다 작으면 가장자리가 화면 밖으로 밀리고, 크면 안쪽으로 번진다
        private const float INK_ZOOM_MIN = 0.8f;
        private const float INK_ZOOM_MAX = 1.3f;

        /****************************************
        *                Types
        ****************************************/

        private enum Envelope : byte
        {
            Decay = 0, // 켜지는 순간 가장 짙고 길이 동안 잦아든다
            Swell = 1, // 번졌다가 빠진다(먹 번짐)
        }

        /// <summary> 오버레이 한 겹의 재생 상태 </summary>
        private sealed class Layer
        {
            public RawImage Image;
            public Envelope Envelope;
            public bool     Playing;
            public float    Elapsed;
            public float    Duration;
            public float    Alpha;
            public Color    Color = Color.white;
        }

        /****************************************
        *                Fields
        ****************************************/

        private readonly Transform _parent;

        private GameObject _root;
        private Canvas     _canvas;
        private Texture2D  _edgeTexture;
        private Texture2D  _inkTexture;

        private readonly Layer _dark  = new Layer { Envelope = Envelope.Decay };
        private readonly Layer _ink   = new Layer { Envelope = Envelope.Swell };
        private readonly Layer _flash = new Layer { Envelope = Envelope.Decay };

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 먹 번짐이 재생 중인지 </summary>
        public bool IsInkPlaying => _ink.Playing;

        /****************************************
        *              Constructor
        ****************************************/

        public ImpactScreenFlash(Transform parent)
        {
            _parent = parent;
        }

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 밝은 가장자리 플래시를 새로 켠다. 이미 켜져 있으면 지금 보이는 밝기보다 어두워지지 않게 이어 붙인다 </summary>
        public void Play(Color color, float seconds, float alpha)
        {
            PlayDecay(_flash, color, seconds, alpha);
        }

        /// <summary>
        /// 화면 가장자리를 어둡게 한다(극한 회피 S7). 단색 오버레이로 가장자리를 어둡게 할 뿐 월드 채도를 낮추지 않는다
        /// 이미 켜져 있으면 지금 보이는 어두움보다 옅어지지 않게 이어 붙인다
        /// </summary>
        public void PlayDarkEdge(Color color, float seconds, float alpha)
        {
            PlayDecay(_dark, color, seconds, alpha);
        }

        /// <summary> 먹 번짐(받아베기 S6). 가장자리에서 안쪽으로 번졌다 빠진다. 재생 중이면 새로 시작하지 않는다 </summary>
        public void PlayInk(Color color, float seconds, float alpha)
        {
            if (!IsPlayable(seconds, alpha) || _ink.Playing)
            {
                return;
            }

            if (!EnsureCreated() || !EnsureInkTexture())
            {
                return;
            }

            _ink.Color    = color;
            _ink.Alpha    = Mathf.Clamp01(alpha);
            _ink.Duration = seconds;
            _ink.Elapsed  = 0f;
            _ink.Playing  = true;

            Show(_ink);
        }

        /// <summary> 실제 시간으로 흐른다. 일시정지 메뉴 위에 오버레이가 얼어붙어 남지 않게 한다 </summary>
        public void Tick(float unscaledDeltaTime)
        {
            if (!_dark.Playing && !_ink.Playing && !_flash.Playing)
            {
                return;
            }

            float delta = Mathf.Max(0f, unscaledDeltaTime);
            TickLayer(_dark, delta);
            TickLayer(_ink, delta);
            TickLayer(_flash, delta);

            RefreshCanvas();
        }

        /// <summary> 어두운 가장자리만 끈다. 다른 겹은 그대로 둔다 </summary>
        public void StopDarkEdge()
        {
            StopLayer(_dark);
            RefreshCanvas();
        }

        /// <summary> 모든 겹을 끈다 </summary>
        public void Stop()
        {
            StopLayer(_dark);
            StopLayer(_ink);
            StopLayer(_flash);

            RefreshCanvas();
        }

        /// <summary> 만든 캔버스와 텍스처를 치운다 </summary>
        public void Dispose()
        {
            _dark.Playing  = false;
            _ink.Playing   = false;
            _flash.Playing = false;

            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
            }

            if (_edgeTexture != null)
            {
                Object.Destroy(_edgeTexture);
                _edgeTexture = null;
            }

            if (_inkTexture != null)
            {
                Object.Destroy(_inkTexture);
                _inkTexture = null;
            }

            _canvas       = null;
            _dark.Image   = null;
            _ink.Image    = null;
            _flash.Image  = null;
        }

        /****************************************
        *                Methods
        ****************************************/

        private static bool IsPlayable(float seconds, float alpha)
        {
            return (seconds > 0f) && (alpha > 0f) && !float.IsNaN(seconds) && !float.IsNaN(alpha);
        }

        private void PlayDecay(Layer layer, Color color, float seconds, float alpha)
        {
            if (!IsPlayable(seconds, alpha))
            {
                return;
            }

            if (!EnsureCreated())
            {
                return;
            }

            float visible = layer.Playing ? (layer.Alpha * Weight(layer)) : 0f;

            layer.Color    = color;
            layer.Alpha    = Mathf.Clamp01(Mathf.Max(alpha, visible));
            layer.Duration = seconds;
            layer.Elapsed  = 0f;
            layer.Playing  = true;

            Show(layer);
        }

        private void Show(Layer layer)
        {
            layer.Image.enabled = true;
            _canvas.enabled     = true;
            Apply(layer);
        }

        private void TickLayer(Layer layer, float delta)
        {
            if (!layer.Playing)
            {
                return;
            }

            layer.Elapsed += delta;
            if (layer.Elapsed >= layer.Duration)
            {
                StopLayer(layer);
                return;
            }

            Apply(layer);
        }

        private static void StopLayer(Layer layer)
        {
            layer.Playing = false;

            if (layer.Image != null)
            {
                layer.Image.enabled = false;
            }
        }

        private void RefreshCanvas()
        {
            if (_canvas == null)
            {
                return;
            }

            _canvas.enabled = _dark.Playing || _ink.Playing || _flash.Playing;
        }

        private static float Weight(Layer layer)
        {
            return (layer.Envelope == Envelope.Swell)
                ? ImpactRules.InkWeight(layer.Elapsed, layer.Duration)
                : ImpactRules.FlashWeight(layer.Elapsed, layer.Duration);
        }

        private static void Apply(Layer layer)
        {
            float weight = Weight(layer);

            Color color = layer.Color;
            color.a           = layer.Alpha * weight;
            layer.Image.color = color;

            if (layer.Envelope == Envelope.Swell)
            {
                // 짙어지는 만큼 가장자리가 안쪽으로 번진다
                float zoom = Mathf.Lerp(INK_ZOOM_MIN, INK_ZOOM_MAX, weight);
                float min  = 0.5f - (zoom * 0.5f);
                layer.Image.uvRect = new Rect(min, min, zoom, zoom);
            }
        }

        private bool EnsureCreated()
        {
            if (_canvas != null)
            {
                return true;
            }

            if (_parent == null)
            {
                return false;
            }

            _edgeTexture = CreateEdgeTexture();

            _root = new GameObject("Impact Screen Flash");
            _root.transform.SetParent(_parent, false);

            _canvas              = _root.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SORTING_ORDER;
            _canvas.enabled      = false;

            // 형제 순서가 그리기 순서다. 밝은 플래시가 가장 위에 온다
            _dark.Image  = CreateLayerImage("Dark Edge", _edgeTexture);
            _ink.Image   = CreateLayerImage("Ink", null);
            _flash.Image = CreateLayerImage("Edge", _edgeTexture);

            return true;
        }

        private bool EnsureInkTexture()
        {
            if (_inkTexture != null)
            {
                return true;
            }

            if (_ink.Image == null)
            {
                return false;
            }

            _inkTexture        = CreateInkTexture();
            _ink.Image.texture = _inkTexture;
            return true;
        }

        private RawImage CreateLayerImage(string name, Texture texture)
        {
            GameObject imageObject = new GameObject(name, typeof(RectTransform));
            imageObject.transform.SetParent(_root.transform, false);

            RectTransform rect = (RectTransform)imageObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            RawImage image = imageObject.AddComponent<RawImage>();
            image.texture       = texture;
            image.raycastTarget = false;
            image.color         = Color.clear;
            image.enabled       = false;
            return image;
        }

        private static Texture2D CreateEdgeTexture()
        {
            Texture2D texture = new Texture2D(TEXTURE_SIZE, TEXTURE_SIZE, TextureFormat.RGBA32, false)
            {
                name       = "ImpactScreenFlashEdge",
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags  = HideFlags.HideAndDontSave,
            };

            Color32[] pixels = new Color32[TEXTURE_SIZE * TEXTURE_SIZE];
            float     half   = (TEXTURE_SIZE - 1) * 0.5f;

            for (int y = 0; y < TEXTURE_SIZE; ++y)
            {
                for (int x = 0; x < TEXTURE_SIZE; ++x)
                {
                    float u = Mathf.Abs((x - half) / half);
                    float v = Mathf.Abs((y - half) / half);

                    float distance = Mathf.Pow(Mathf.Pow(u, EDGE_EXPONENT) + Mathf.Pow(v, EDGE_EXPONENT), 1f / EDGE_EXPONENT);
                    float edge     = Mathf.Clamp01((distance - EDGE_INNER) / (1f - EDGE_INNER));
                    float alpha    = Mathf.SmoothStep(0f, 1f, edge);

                    pixels[(y * TEXTURE_SIZE) + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary> 가장자리가 먹처럼 고르지 않게 번진 텍스처. 테두리는 항상 짙어 uvRect를 넓혀도 바깥이 이어진다 </summary>
        private static Texture2D CreateInkTexture()
        {
            Texture2D texture = new Texture2D(INK_TEXTURE_SIZE, INK_TEXTURE_SIZE, TextureFormat.RGBA32, false)
            {
                name       = "ImpactScreenFlashInk",
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags  = HideFlags.HideAndDontSave,
            };

            Color32[] pixels = new Color32[INK_TEXTURE_SIZE * INK_TEXTURE_SIZE];
            float     half   = (INK_TEXTURE_SIZE - 1) * 0.5f;
            float     step   = INK_NOISE_FREQUENCY / INK_TEXTURE_SIZE;

            for (int y = 0; y < INK_TEXTURE_SIZE; ++y)
            {
                for (int x = 0; x < INK_TEXTURE_SIZE; ++x)
                {
                    float u = Mathf.Abs((x - half) / half);
                    float v = Mathf.Abs((y - half) / half);

                    float distance = Mathf.Pow(Mathf.Pow(u, EDGE_EXPONENT) + Mathf.Pow(v, EDGE_EXPONENT), 1f / EDGE_EXPONENT);
                    float noise    = Mathf.PerlinNoise(x * step, y * step) - 0.5f;
                    float ink      = Mathf.Clamp01(((distance + (noise * INK_NOISE_AMPLITUDE)) - INK_INNER) / (INK_OUTER - INK_INNER));
                    float alpha    = Mathf.SmoothStep(0f, 1f, ink);

                    pixels[(y * INK_TEXTURE_SIZE) + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
