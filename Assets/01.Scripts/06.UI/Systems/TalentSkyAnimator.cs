// System
using System;

// Unity
using UnityEngine;
using UnityEngine.UI;

namespace Geumeum.UI
{
    /// <summary>
    /// 특성 별하늘 연출. 은하수 띠를 천천히 흘리고 별 몇 개를 반짝인다
    ///
    /// 특성 탭이 켜져 있을 때만 돈다(탭을 끄면 이 오브젝트도 꺼진다).
    /// 관리 창은 시간이 멈춘 상태에서도 열리므로 unscaled 시간을 쓴다.
    /// 매 프레임 색이 바뀌므로 이 오브젝트는 따로 Canvas를 두어 노드 쪽 재구성을 막는다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TalentSkyAnimator : MonoBehaviour
    {
        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        private struct NebulaLayer
        {
            public RawImage Image;

            [Tooltip("초당 흐르는 양(텍스처 폭 비율). 음수면 반대로 흐른다")]
            public float Speed;
        }

        /****************************************
        *                Fields
        ****************************************/

        private const float FULL_TURN          = Mathf.PI * 2f;
        private const float MIN_TWINKLE_PERIOD = 0.1f; // 주기가 0이면 속도가 무한대가 된다

        [Header("은하수")]
        [Tooltip("가로로 이어지는 은하수 층. 층마다 속도를 달리하면 깊이가 생긴다")]
        [SerializeField] private NebulaLayer[] _nebulaLayers = new NebulaLayer[0];

        [Header("반짝이는 별")]
        [SerializeField] private RectTransform _twinkleRoot;
        [SerializeField] private Sprite        _twinkleSprite;
        [SerializeField] private int           _twinkleCount = 70;

        [Tooltip("별 크기 범위(px)")]
        [SerializeField] private Vector2 _twinkleSizeRange = new Vector2(5f, 14f);

        [Tooltip("한 번 반짝이는 데 걸리는 시간 범위(초)")]
        [SerializeField] private Vector2 _twinklePeriodRange = new Vector2(1.8f, 5f);

        [Tooltip("가장 어두울 때와 가장 밝을 때의 불투명도")]
        [SerializeField] private Vector2 _twinkleAlphaRange = new Vector2(0.1f, 0.95f);

        [Tooltip("같은 값이면 매번 같은 자리에 별이 뜬다")]
        [SerializeField] private int _seed = 20260925;

        private static readonly Color[] G_TwinkleTints =
        {
            new Color(1f, 1f, 1f),
            new Color(0.82f, 0.88f, 1f),
            new Color(1f, 0.9f, 0.86f),
        };

        private Image[] _twinkles      = new Image[0];
        private float[] _twinkleSpeeds = new float[0];
        private float[] _twinklePhases = new float[0];

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            CreateTwinkles();
        }

        private void Update()
        {
            float deltaTime = Time.unscaledDeltaTime;
            for (int i = 0; i < _nebulaLayers.Length; ++i)
            {
                RawImage image = _nebulaLayers[i].Image;
                if (image == null)
                {
                    continue;
                }

                Rect uv = image.uvRect;
                uv.x = Mathf.Repeat(uv.x + (_nebulaLayers[i].Speed * deltaTime), 1f);
                image.uvRect = uv;
            }

            float time = Time.unscaledTime;
            for (int i = 0; i < _twinkles.Length; ++i)
            {
                // 사인을 세제곱해 대부분 어둡다가 잠깐 빛나게 한다. 고르게 깜빡이면 별보다 전구 같다
                float wave = 0.5f + (0.5f * Mathf.Sin((time * _twinkleSpeeds[i]) + _twinklePhases[i]));
                float glint = wave * wave * wave;

                Color color = _twinkles[i].color;
                color.a = Mathf.Lerp(_twinkleAlphaRange.x, _twinkleAlphaRange.y, glint);
                _twinkles[i].color = color;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 반짝이는 별을 한 번만 만든다. 자리는 앵커 비율로 두어 화면 크기와 무관하게 흩어진다
        /// </summary>
        private void CreateTwinkles()
        {
            if ((_twinkleRoot == null) || (_twinkleSprite == null) || (_twinkleCount <= 0))
            {
                return;
            }

            var random = new System.Random(_seed);
            _twinkles = new Image[_twinkleCount];
            _twinkleSpeeds = new float[_twinkleCount];
            _twinklePhases = new float[_twinkleCount];

            for (int i = 0; i < _twinkleCount; ++i)
            {
                GameObject star = new GameObject("Twinkle" + i, typeof(RectTransform), typeof(Image));
                var rect = (RectTransform)star.transform;
                rect.SetParent(_twinkleRoot, false);

                Vector2 anchor = new Vector2((float)random.NextDouble(), (float)random.NextDouble());
                rect.anchorMin = anchor;
                rect.anchorMax = anchor;
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = Vector2.one * Mathf.Lerp(_twinkleSizeRange.x, _twinkleSizeRange.y,
                    (float)(random.NextDouble() * random.NextDouble()));

                Image image = star.GetComponent<Image>();
                image.sprite = _twinkleSprite;
                image.raycastTarget = false;
                image.color = G_TwinkleTints[random.Next(G_TwinkleTints.Length)];

                float period = Mathf.Lerp(_twinklePeriodRange.x, _twinklePeriodRange.y,
                    (float)random.NextDouble());
                _twinkleSpeeds[i] = FULL_TURN / Mathf.Max(MIN_TWINKLE_PERIOD, period);
                _twinklePhases[i] = (float)(random.NextDouble() * FULL_TURN);
                _twinkles[i] = image;
            }
        }
    }
}
