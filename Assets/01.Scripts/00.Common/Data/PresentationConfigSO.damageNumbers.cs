// Unity
using UnityEngine;

namespace Geumeum.Data
{
    /// <summary>
    /// 데미지 숫자 가림 검사 수치(COMBAT_STYLE_DESIGN S10 시야 밖·성능). 각자 화면의 표현 값이다
    /// 숫자의 색·크기·수명·상한은 PresentationConfigSO.impact.cs에 있다
    /// </summary>
    public partial class PresentationConfigSO
    {
        /****************************************
        *              Constants
        ****************************************/

        // 가림 레이어 기본값. CameraConfigSO의 카메라 충돌 레이어와 같게 둔다(지형·바위). 적·플레이어는 넣지 않는다
        private const int OCCLUSION_LAYER_DEFAULT   = 0;  // 지형이 여기 있다
        private const int OCCLUSION_LAYER_GROUND    = 8;
        private const int OCCLUSION_LAYER_PW_SMALL  = 15; // 바위
        private const int OCCLUSION_LAYER_PW_MEDIUM = 16; // 바위
        private const int OCCLUSION_LAYER_PW_LARGE  = 17; // 바위

        private const int DEFAULT_DAMAGE_NUMBER_OCCLUSION_MASK =
              (1 << OCCLUSION_LAYER_DEFAULT)
            | (1 << OCCLUSION_LAYER_GROUND)
            | (1 << OCCLUSION_LAYER_PW_SMALL)
            | (1 << OCCLUSION_LAYER_PW_MEDIUM)
            | (1 << OCCLUSION_LAYER_PW_LARGE);

        /****************************************
        *                Fields
        ****************************************/

        [Header("데미지 숫자 가림 (S10)")]
        [Tooltip("카메라와 적중점 사이에 있으면 숫자를 만들지 않는 레이어. 지형·바위만 넣는다 - 적·플레이어·보스를 넣으면 맞은 대상이 스스로를 가린다")]
        [SerializeField] private LayerMask _damageNumberOcclusionMask = DEFAULT_DAMAGE_NUMBER_OCCLUSION_MASK;

        [Tooltip("한 프레임에 하는 가림 검사 최대 횟수. 넘으면 검사 없이 표시한다. 0이면 가림 검사를 하지 않는다")]
        [SerializeField, Min(0)] private int _damageNumberOcclusionChecksPerFrame = 4;

        [Tooltip("같은 대상(부위 포함)의 가림 결과를 다시 쓰는 시간(초)")]
        [SerializeField, Min(0f)] private float _damageNumberOcclusionReuseSeconds = 0.2f;

        [Tooltip("적중점을 카메라 쪽으로 당기는 거리(m). 적중점 바로 뒤·아래의 지형이 가림으로 잡히지 않게 한다")]
        [SerializeField, Min(0f)] private float _damageNumberOcclusionEndInset = 0.3f;

        /****************************************
        *              Properties
        ****************************************/

        public LayerMask DamageNumberOcclusionMask           => _damageNumberOcclusionMask;
        public int       DamageNumberOcclusionChecksPerFrame => Mathf.Max(0, _damageNumberOcclusionChecksPerFrame);
        public float     DamageNumberOcclusionReuseSeconds   => Mathf.Max(0f, _damageNumberOcclusionReuseSeconds);
        public float     DamageNumberOcclusionEndInset       => Mathf.Max(0f, _damageNumberOcclusionEndInset);
    }
}
