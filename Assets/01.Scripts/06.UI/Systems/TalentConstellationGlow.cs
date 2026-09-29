// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.UI
{
    /// <summary>
    /// 특성 별자리의 빛 번짐. 별은 코어 하나가 아니라 안쪽·바깥쪽 헤일로를 겹쳐 실제 광원처럼 보이게 하고,
    /// 가운데 그믐은 여러 겹으로 나눠 아주 느리게 숨쉰다
    ///
    /// URP Bloom을 쓰지 않는다. 같은 카메라의 글자와 버튼까지 번지기 때문이다
    /// 값은 모두 인스펙터에서 다듬는다
    /// </summary>
    [Serializable]
    public sealed class TalentConstellationGlow
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("고른 별 - 살아 있는 별")]
        [Tooltip("안쪽 헤일로 크기. 별 크기의 몇 배인지")]
        [SerializeField] private float _allocatedInnerScale = 1.5f;
        [SerializeField] private float _allocatedInnerAlpha = 0.3f;

        [Tooltip("바깥쪽 헤일로 크기. 별 크기의 몇 배인지")]
        [SerializeField] private float _allocatedOuterScale = 2.6f;
        [SerializeField] private float _allocatedOuterAlpha = 0.08f;

        [Header("고를 수 있는 별 - 깨어나는 별")]
        [SerializeField] private float _openInnerScale = 1.8f;
        [SerializeField] private float _openInnerAlpha = 0.12f;

        [Header("가운데 그믐")]
        [Tooltip("빛 크기(px). 0이면 프리팹 크기를 그대로 쓴다")]
        [SerializeField] private float _hubGlowSize = 64f;

        [Tooltip("달 크기(px). 0이면 프리팹 크기를 그대로 쓴다")]
        [SerializeField] private float _hubMoonSize = 14f;

        [Tooltip("그믐 뒤에 까는 아주 옅은 헤일로 크기(px)")]
        [SerializeField] private float _hubHaloSize = 170f;
        [SerializeField] private float _hubHaloAlpha = 0.07f;

        [Tooltip("숨에 맞춰 헤일로 밝기가 줄어드는 폭. 0.2면 가장 어두울 때 0.6배다")]
        [SerializeField] private float _hubHaloBreathAlpha = 0.2f;

        [Tooltip("숨쉴 때 커지고 작아지는 비율(±)")]
        [SerializeField] private float _hubBreathAmount = 0.04f;

        [Tooltip("한 번 숨쉬는 데 걸리는 시간(초)")]
        [SerializeField] private float _hubBreathPeriod = 6f;

        /****************************************
        *              Properties
        ****************************************/

        public float HubGlowSize  => _hubGlowSize;
        public float HubMoonSize  => _hubMoonSize;
        public float HubHaloSize  => _hubHaloSize;
        public float HubHaloAlpha => _hubHaloAlpha;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 고른 별과 고를 수 있는 별만 헤일로를 가진다. 잠긴 별은 빛이 없다 </summary>
        public bool TryGetInner(bool allocated, bool open, out float scale, out float alpha)
        {
            scale = allocated ? _allocatedInnerScale : _openInnerScale;
            alpha = allocated ? _allocatedInnerAlpha : open ? _openInnerAlpha : 0f;
            return alpha > 0f;
        }

        public bool TryGetOuter(bool allocated, out float scale, out float alpha)
        {
            scale = _allocatedOuterScale;
            alpha = allocated ? _allocatedOuterAlpha : 0f;
            return alpha > 0f;
        }

        /// <summary> 숨 한 번의 위치(-1 ~ 1) </summary>
        public float Breath(float time)
        {
            if (_hubBreathPeriod <= 0f)
            {
                return 0f;
            }

            return Mathf.Sin((time / _hubBreathPeriod) * Mathf.PI * 2f);
        }

        public float BreathScale(float time)
        {
            return 1f + (Breath(time) * _hubBreathAmount);
        }

        /// <summary> 그믐 헤일로 알파 배수. 숨을 들이쉴 때 가장 밝다(1) </summary>
        public float HaloBreathAlpha(float time)
        {
            return 1f - (_hubHaloBreathAlpha * (1f - Breath(time)));
        }
    }
}
