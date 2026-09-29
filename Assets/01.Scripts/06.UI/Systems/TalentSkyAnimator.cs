// System
using System;

// Unity
using UnityEngine;
using UnityEngine.UI;

namespace Geumeum.UI
{
    /// <summary>
    /// 특성 별하늘 연출. 은하수 띠를 천천히 흘리고 별 몇 개를 반짝인다.
    /// 마우스를 따라 층마다 다른 거리만큼 밀어 깊이를 만들고, 별자리 앞에 먼지를 띄운다
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

        // 특성 노드 버튼 이름 머리. FunctionalManagementPanel이 복제하면서 붙인다
        private const string NODE_NAME_PREFIX = "TalentNode";

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

        [Header("시차 (마우스)")]
        [Tooltip("마우스를 따라가는 속도. 클수록 빠르다")]
        [SerializeField] private float _pointerSmoothing = 4f;

        [Tooltip("은하수 층마다 밀리는 거리(px). 은하수 층과 같은 순서다. 먼 층일수록 작게 둔다")]
        [SerializeField] private float[] _nebulaParallax = { 4f, 9f };

        [SerializeField] private float _twinkleParallax = 6f;

        [Tooltip("입력이 없어도 화면이 아주 조금 떠다니는 폭. 시차 입력(-1 ~ 1)에 더한다")]
        [SerializeField] private float _idleDriftAmount = 0.12f;

        [Tooltip("떠다니는 속도(라디안/초). 가로, 세로를 다르게 두어 원을 그리지 않게 한다")]
        [SerializeField] private Vector2 _idleDriftSpeed = new Vector2(0.07f, 0.05f);

        [Header("앞쪽 먼지")]
        [Tooltip("별자리 앞을 떠다니는 먼지 수. 적어야 공간감만 남고 화면이 부산스럽지 않다")]
        [SerializeField] private int _dustCount = 28;

        [Tooltip("비워 두면 반짝이는 별 그림을 쓴다")]
        [SerializeField] private Sprite _dustSprite;

        [SerializeField] private Vector2 _dustSizeRange  = new Vector2(1.5f, 4f);
        [SerializeField] private Vector2 _dustAlphaRange = new Vector2(0.12f, 0.4f);

        [Tooltip("먼지가 흐르는 속도 범위(px/초)")]
        [SerializeField] private Vector2 _dustSpeedRange = new Vector2(4f, 12f);

        [Tooltip("먼지가 흐르는 방향 범위(도). 90이 위다. 대체로 떠오르되 옆으로 조금씩 흐른다")]
        [SerializeField] private Vector2 _dustAngleRange = new Vector2(60f, 120f);

        [Tooltip("먼지가 밀리는 거리(px). 가장 가까운 층이라 배경보다 훨씬 크게 둔다")]
        [SerializeField] private float _dustParallax = 22f;

        private static readonly Color[] G_TwinkleTints =
        {
            new Color(1f, 1f, 1f),
            new Color(0.82f, 0.88f, 1f),
            new Color(1f, 0.9f, 0.86f),
        };

        private Image[] _twinkles      = new Image[0];
        private float[] _twinkleSpeeds = new float[0];
        private float[] _twinklePhases = new float[0];

        // 시차로 밀기 전 자리. 프리팹이 정한 자리를 기준으로 민다
        private Vector2[] _nebulaOrigins = new Vector2[0];
        private Vector2   _twinkleOrigin;

        private RectTransform   _dustRoot;
        private RectTransform[] _dusts          = new RectTransform[0];
        private Vector2[]       _dustStarts     = new Vector2[0];
        private Vector2[]       _dustVelocities = new Vector2[0];

        private Vector2 _smoothedMouse;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary>
        /// 시차 입력. 화면 가운데 기준 -1 ~ 1인 마우스 자리에 느린 떠다님을 더한 값이다.
        /// 별자리 노드도 같은 값으로 밀어야 하늘과 별이 한 공간에 있는 것처럼 움직인다
        /// </summary>
        public static Vector2 Pointer { get; private set; }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            CacheOrigins();
            CreateTwinkles();
            CreateDust();
        }

        private void OnDisable()
        {
            // 다시 열 때 이전 마우스 자리에서 미끄러져 오지 않게 한다
            _smoothedMouse = ReadMouse();
        }

        private void Update()
        {
            float deltaTime = Time.unscaledDeltaTime;
            float time = Time.unscaledTime;
            UpdatePointer(time, deltaTime);

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

                float parallax = (i < _nebulaParallax.Length) ? _nebulaParallax[i] : 0f;
                image.rectTransform.anchoredPosition = _nebulaOrigins[i] - (Pointer * parallax);
            }

            if (_twinkleRoot != null)
            {
                _twinkleRoot.anchoredPosition = _twinkleOrigin - (Pointer * _twinkleParallax);
            }

            MoveDust(time);

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

        private void CacheOrigins()
        {
            _nebulaOrigins = new Vector2[_nebulaLayers.Length];
            for (int i = 0; i < _nebulaLayers.Length; ++i)
            {
                RawImage image = _nebulaLayers[i].Image;
                _nebulaOrigins[i] = (image == null) ? Vector2.zero : image.rectTransform.anchoredPosition;
            }

            _twinkleOrigin = (_twinkleRoot == null) ? Vector2.zero : _twinkleRoot.anchoredPosition;
            _smoothedMouse = ReadMouse();
        }

        private void UpdatePointer(float time, float deltaTime)
        {
            float blend = 1f - Mathf.Exp(-_pointerSmoothing * deltaTime);
            _smoothedMouse = Vector2.Lerp(_smoothedMouse, ReadMouse(), blend);

            Vector2 drift = new Vector2(
                Mathf.Sin(time * _idleDriftSpeed.x),
                Mathf.Sin(time * _idleDriftSpeed.y)) * _idleDriftAmount;
            Pointer = _smoothedMouse + drift;
        }

        /// <summary>
        /// 화면 가운데 기준 -1 ~ 1. 마우스가 창 밖이면 가운데로 돌아간다 -
        /// 가장자리에 묶으면 창 밖으로 나가 있는 동안 하늘이 한쪽 끝에 쏠린 채로 남는다
        /// </summary>
        private static Vector2 ReadMouse()
        {
            if ((Screen.width <= 0) || (Screen.height <= 0))
            {
                return Vector2.zero;
            }

            Vector3 mouse = Input.mousePosition;
            Vector2 pointer = new Vector2(
                ((mouse.x / Screen.width) * 2f) - 1f,
                ((mouse.y / Screen.height) * 2f) - 1f);
            bool outside = (Mathf.Abs(pointer.x) > 1f) || (Mathf.Abs(pointer.y) > 1f);
            return outside ? Vector2.zero : pointer;
        }

        /// <summary>
        /// 먼지를 별자리 노드 바로 앞에 한 번만 만든다. 가지 이름과 노드 설명은 먼지보다 앞에 남는다
        ///
        /// 별하늘 Canvas 안에 두면 노드 뒤에 깔리므로 특성 페이지에 따로 둔다.
        /// 매 프레임 움직이므로 자체 Canvas를 달아 노드 쪽 재구성을 막는다
        /// </summary>
        private void CreateDust()
        {
            Sprite sprite = (_dustSprite != null) ? _dustSprite : _twinkleSprite;
            Transform page = transform.parent;
            if ((sprite == null) || (page == null) || (_dustCount <= 0))
            {
                return;
            }

            GameObject root = new GameObject("TalentDust", typeof(RectTransform), typeof(Canvas));
            _dustRoot = (RectTransform)root.transform;
            _dustRoot.SetParent(page, false);
            _dustRoot.anchorMin = Vector2.zero;
            _dustRoot.anchorMax = Vector2.one;
            _dustRoot.offsetMin = Vector2.zero;
            _dustRoot.offsetMax = Vector2.zero;
            _dustRoot.SetSiblingIndex(FindDustSiblingIndex(page));

            var random = new System.Random(_seed + 1);
            _dusts = new RectTransform[_dustCount];
            _dustStarts = new Vector2[_dustCount];
            _dustVelocities = new Vector2[_dustCount];

            for (int i = 0; i < _dustCount; ++i)
            {
                GameObject dust = new GameObject("Dust" + i, typeof(RectTransform), typeof(Image));
                var rect = (RectTransform)dust.transform;
                rect.SetParent(_dustRoot, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.zero;
                rect.sizeDelta = Vector2.one * Mathf.Lerp(_dustSizeRange.x, _dustSizeRange.y,
                    (float)random.NextDouble());

                Image image = dust.GetComponent<Image>();
                image.sprite = sprite;
                image.raycastTarget = false;
                Color color = G_TwinkleTints[random.Next(G_TwinkleTints.Length)];
                color.a = Mathf.Lerp(_dustAlphaRange.x, _dustAlphaRange.y, (float)random.NextDouble());
                image.color = color;

                // 시작 자리는 페이지 비율로 두고, 크기를 모를 때도 흩어지게 한다
                _dustStarts[i] = new Vector2((float)random.NextDouble(), (float)random.NextDouble());

                float angle = Mathf.Lerp(_dustAngleRange.x, _dustAngleRange.y,
                    (float)random.NextDouble()) * Mathf.Deg2Rad;
                float speed = Mathf.Lerp(_dustSpeedRange.x, _dustSpeedRange.y, (float)random.NextDouble());
                _dustVelocities[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
                _dusts[i] = rect;
            }
        }

        /// <summary> 마지막 노드 바로 뒤. 노드가 없으면 맨 앞이다 </summary>
        private static int FindDustSiblingIndex(Transform page)
        {
            int index = page.childCount - 1;
            for (int i = page.childCount - 1; i >= 0; --i)
            {
                if (page.GetChild(i).name.StartsWith(NODE_NAME_PREFIX, StringComparison.Ordinal))
                {
                    return i + 1;
                }
            }

            return index;
        }

        /// <summary> 흐른 거리를 페이지 크기로 감아 끝없이 떠오르게 한다 </summary>
        private void MoveDust(float time)
        {
            if (_dustRoot == null)
            {
                return;
            }

            Rect bounds = _dustRoot.rect;
            if ((bounds.width <= 0f) || (bounds.height <= 0f))
            {
                return;
            }

            Vector2 parallax = Pointer * _dustParallax;
            for (int i = 0; i < _dusts.Length; ++i)
            {
                Vector2 start = new Vector2(_dustStarts[i].x * bounds.width, _dustStarts[i].y * bounds.height);
                Vector2 travel = start + (_dustVelocities[i] * time);
                _dusts[i].anchoredPosition = new Vector2(
                    Mathf.Repeat(travel.x, bounds.width),
                    Mathf.Repeat(travel.y, bounds.height)) - parallax;
            }
        }

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
