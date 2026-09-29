// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.UI
{
    /// <summary>
    /// 특성 별자리의 가상 깊이. 노드마다 -1(멀리) ~ +1(가까이) 값 하나를 주고
    /// 크기, 밝기, 알파, 선 굵기, 반짝임을 그 값 하나로 함께 움직인다
    ///
    /// 실제 Z를 쓰지 않는다. 캔버스 구조를 그대로 두고 깊이처럼 읽히게만 한다
    /// 값은 모두 인스펙터에서 다듬는다
    /// </summary>
    [Serializable]
    public sealed class TalentConstellationDepth
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float MIN_DEPTH = -1f;
        private const float MAX_DEPTH = 1f;
        private const float TWO_PI    = Mathf.PI * 2f;

        // 사인 해시. 정수 시드를 고르게 흩어지는 소수로 바꾸는 흔한 상수 쌍이다
        private const float HASH_FREQUENCY = 12.9898f;
        private const float HASH_STRETCH   = 43758.5453f;

        [Header("깊이 배치 (-1 멀리 ~ +1 가까이)")]
        [Tooltip("내 가지의 기준 깊이. 내 가지가 앞으로 나와 보이게 한다")]
        [SerializeField] private float _localArmDepth = 0.3f;

        [Tooltip("남의 가지의 기준 깊이")]
        [SerializeField] private float _otherArmDepth = -0.45f;

        [Tooltip("열마다 더하는 깊이. 0열이 줄기, 1열부터 갈래다. 모자란 열은 0이다")]
        [SerializeField] private float[] _columnDepths = { 0f, 0.15f, -0.05f, -0.2f };

        [Tooltip("뿌리에서 한 칸 멀어질 때마다 더하는 깊이")]
        [SerializeField] private float _rowDepthStep = 0.05f;

        [Tooltip("노드마다 흩뿌리는 깊이 폭(±). 규칙적인 값만 쓰면 평면처럼 보인다")]
        [SerializeField] private float _depthJitter = 0.15f;

        [Header("깊이 표현 (멀리 → 가까이)")]
        [SerializeField] private Vector2 _scaleRange      = new Vector2(0.8f, 1.15f);
        [SerializeField] private Vector2 _brightnessRange = new Vector2(0.7f, 1.1f);
        [SerializeField] private Vector2 _alphaRange      = new Vector2(0.45f, 1f);

        [Tooltip("선 알파 배수. 두 끝 노드 깊이의 평균을 쓴다")]
        [SerializeField] private Vector2 _lineAlphaRange = new Vector2(0.35f, 1f);

        [Tooltip("선 굵기 배수. 너무 굵으면 별자리가 아니라 스킬 트리처럼 보인다")]
        [SerializeField] private Vector2 _lineWidthRange = new Vector2(0.7f, 1.3f);

        [Header("시차 (마우스)")]
        [Tooltip("마우스를 끝까지 밀었을 때 별이 움직이는 거리(px). 가까운 별이 더 많이 움직인다")]
        [SerializeField] private Vector2 _parallaxRange = new Vector2(2f, 9f);

        [Tooltip("가운데 그믐의 깊이. 모든 가지가 모이는 자리라 약간 앞에 둔다")]
        [SerializeField] private float _hubDepth = 0.2f;

        [Header("반짝임")]
        [Tooltip("알파가 흔들리는 폭. 잠긴 별은 반짝이지 않는다")]
        [SerializeField] private float _twinkleAmount = 0.15f;

        [Tooltip("초당 반짝임 횟수. 가까운 별이 더 빠르다")]
        [SerializeField] private Vector2 _twinkleSpeedRange = new Vector2(0.25f, 0.7f);

        [Header("고를 수 있는 별의 맥박")]
        [Tooltip("맥박 사이 간격(초). 계속 번쩍이면 퀘스트 버튼처럼 보인다")]
        [SerializeField] private float _pulseInterval = 4f;

        [SerializeField] private float _pulseDuration = 0.6f;

        [Tooltip("맥박 때 커지는 비율")]
        [SerializeField] private float _pulseScale = 0.08f;

        [Header("가리킨 별 - 사용자 쪽으로 다가온다")]
        [SerializeField] private float _hoverScale = 1.18f;

        [Tooltip("다가오고 물러나는 속도. 클수록 빠르다")]
        [SerializeField] private float _hoverSmoothing = 12f;

        [Tooltip("다른 별을 가리키는 동안 나머지 별과 선에 곱하는 알파")]
        [SerializeField] private float _hoverDimOthers = 0.8f;

        /****************************************
        *              Properties
        ****************************************/

        public float HoverScale     => _hoverScale;
        public float HoverSmoothing => _hoverSmoothing;
        public float HoverDimOthers => _hoverDimOthers;
        public float HubDepth       => _hubDepth;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 노드 깊이. 흩뿌림은 노드 ID에서 뽑으므로 세 화면과 매 갱신이 같은 값을 얻는다
        /// </summary>
        public float NodeDepth(bool mine, int column, int row, int nodeId)
        {
            float depth = mine ? _localArmDepth : _otherArmDepth;
            if ((_columnDepths != null) && (column >= 0) && (column < _columnDepths.Length))
            {
                depth += _columnDepths[column];
            }

            depth += row * _rowDepthStep;
            depth += Hash(nodeId) * _depthJitter;
            return Mathf.Clamp(depth, MIN_DEPTH, MAX_DEPTH);
        }

        public float Scale(float depth)
        {
            return Lerp(_scaleRange, depth);
        }

        /// <summary> 상태 색에 깊이를 입힌다. 가까울수록 밝고 진하다 </summary>
        public Color Tint(Color color, float depth)
        {
            float brightness = Lerp(_brightnessRange, depth);
            return new Color(
                Mathf.Clamp01(color.r * brightness),
                Mathf.Clamp01(color.g * brightness),
                Mathf.Clamp01(color.b * brightness),
                color.a * Lerp(_alphaRange, depth));
        }

        public Color LineTint(Color color, float depth)
        {
            color.a *= Lerp(_lineAlphaRange, depth);
            return color;
        }

        public float LineWidthScale(float depth)
        {
            return Lerp(_lineWidthRange, depth);
        }

        /// <summary>
        /// 시차로 밀리는 거리(px). pointer는 화면 가운데 기준 -1 ~ 1이다.
        /// 시선의 반대로 밀려야 가까운 것이 앞에 있는 것처럼 읽힌다
        /// </summary>
        public Vector2 ParallaxOffset(Vector2 pointer, float depth)
        {
            return -pointer * Lerp(_parallaxRange, depth);
        }

        /// <summary> 알파 배수. phase는 별마다 다르게 주어 한꺼번에 깜빡이지 않게 한다 </summary>
        public float Twinkle(float time, float depth, float phase)
        {
            float speed = Lerp(_twinkleSpeedRange, depth);
            float wave = (Mathf.Sin((time * speed * TWO_PI) + (phase * TWO_PI)) + 1f) * 0.5f;
            return 1f - (_twinkleAmount * wave);
        }

        /// <summary> 크기에 더할 비율. 간격마다 한 번 부풀었다 돌아온다 </summary>
        public float Pulse(float time, float phase)
        {
            if ((_pulseInterval <= 0f) || (_pulseDuration <= 0f))
            {
                return 0f;
            }

            float local = Mathf.Repeat(time + (phase * _pulseInterval), _pulseInterval);
            if (local >= _pulseDuration)
            {
                return 0f;
            }

            return Mathf.Sin((local / _pulseDuration) * Mathf.PI) * _pulseScale;
        }

        /// <summary> 정수에서 -1 ~ 1 사이 고정 난수를 뽑는다 </summary>
        public static float Hash(int seed)
        {
            float value = Mathf.Sin(seed * HASH_FREQUENCY) * HASH_STRETCH;
            return ((value - Mathf.Floor(value)) * 2f) - 1f;
        }

        private static float Lerp(Vector2 range, float depth)
        {
            return Mathf.Lerp(range.x, range.y, Mathf.InverseLerp(MIN_DEPTH, MAX_DEPTH, depth));
        }
    }
}
