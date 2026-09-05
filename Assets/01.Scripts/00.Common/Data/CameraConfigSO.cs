// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Data
{
    /// <summary>
    /// 3인칭 카메라의 거리, 회전, 충돌 수치.
    /// 네트워크를 타지 않는 로컬 리소스다 - 카메라는 완전히 클라이언트 소유이고 판정에 관여하지 않는다.
    /// 씬마다 카메라 오브젝트가 따로 있어도 <b>감은 여기 하나에서 나온다</b>
    /// </summary>
    /// <remarks>
    /// 거리 넷(4.5 · 9.0 · 7.0 · 0.6m)은 카메라와 락온 설계 13장이 <b>화면을 봐야 정해지는 임시값</b>으로
    /// 남긴 것이다. Play 중에 이 에셋을 열어 밀어 보고 정한다
    /// </remarks>
    [CreateAssetMenu(fileName = "CameraConfigSO", menuName = "Geumeum/CameraConfigSO")]
    public class CameraConfigSO : ScriptableObject
    {
        /****************************************
        *                Types
        ****************************************/

        /// <summary> 셰이크 한 종류의 세기와 길이 </summary>
        [Serializable]
        public struct ShakeSpec
        {
            [Tooltip("최대 흔들림 폭(m)")]
            public float Amplitude;

            [Tooltip("이 길이(초) 동안 선형으로 잦아든다")]
            public float Duration;
        }

        /****************************************
        *              Constants
        ****************************************/

        // 마스크는 레이어 이름이 아니라 인덱스가 필요한데 Constants.Layer는 이름만 갖고 있다.
        // 값은 ProjectSettings/TagManager.asset의 순서를 따른다 - 거기서 레이어를 옮기면 같이 고쳐야 한다.
        // 어디까지나 에셋 생성 시 기본값이고, 실제로 도는 값은 인스펙터에 보인다.
        //
        // ⚠️ 2026-09-05 실측 정정 - 처음에 Default + Ground 만 넣었다가 바위를 전부 놓쳤다.
        //    Main 씬 실측: 지형은 Ground(8)가 아니라 Default(0)에 있고 Ground는 콜라이더 0개다.
        //    바위 393개는 전부 PW_Object_* 세 층에 있다. 이름의 Small/Medium/Large는 물리 크기가
        //    아니라 PW의 거리 분류라 크기로 걸러 넣으면 안 된다(Small 평균 11m, Large 평균 1.7m)
        private const int LAYER_DEFAULT   = 0;  // 지형이 여기 있다
        private const int LAYER_GROUND    = 8;  // 지금은 비어 있으나 지형을 옮길 자리로 남겨 둔다
        private const int LAYER_PW_SMALL  = 15; // 바위
        private const int LAYER_PW_MEDIUM = 16; // 바위
        private const int LAYER_PW_LARGE  = 17; // 바위

        private const int DEFAULT_COLLISION_MASK =
              (1 << LAYER_DEFAULT)
            | (1 << LAYER_GROUND)
            | (1 << LAYER_PW_SMALL)
            | (1 << LAYER_PW_MEDIUM)
            | (1 << LAYER_PW_LARGE);

        /****************************************
        *                Fields
        ****************************************/

        [Header("추적")]
        [Tooltip("대상 발밑에서 카메라가 도는 중심까지의 높이(m). 눈높이")]
        [SerializeField] private float _pivotHeight = 1.6f;

        [Tooltip("중심에서 카메라까지의 거리(m). 무녀 범위 힐 6m 안에 셋이 뭉친 화면을 보고 정한다")]
        [SerializeField] private float _distance = 4.5f;

        [Tooltip("오버숄더 측면 오프셋(m). 화면 정중앙이 비어야 그 자리가 조준선이 된다")]
        [SerializeField] private float _sideOffset = 0.6f;

        [Tooltip("대상 추적 스무딩(초). 회전은 스무딩하지 않는다 - 하면 입력 지연으로 느껴진다")]
        [SerializeField] private float _followSmoothTime = 0.08f;

        [Header("회전")]
        [Tooltip("좌우 감도 기본값(도/픽셀). 옵션 UI가 생기면 사람마다 덮어쓴다")]
        [SerializeField] private float _defaultSensitivityX = 0.15f;

        [Tooltip("상하 감도 기본값(도/픽셀)")]
        [SerializeField] private float _defaultSensitivityY = 0.12f;

        [Tooltip("상하 반전 기본값")]
        [SerializeField] private bool _defaultInvertY = false;

        [Tooltip("위를 보는 한계(도). 음수가 위다")]
        [SerializeField] private float _pitchMin = -30f;

        [Tooltip("아래를 보는 한계(도)")]
        [SerializeField] private float _pitchMax = 60f;

        [Header("충돌")]
        [Tooltip("카메라가 뚫으면 안 되는 레이어. 지형(Ground)과 바위(Default)")]
        [SerializeField] private LayerMask _collisionMask = DEFAULT_COLLISION_MASK;

        [Tooltip("벽을 감지하는 구의 반지름(m). 카메라 근평면이 벽을 뚫기 전에 걸려야 한다")]
        [SerializeField] private float _probeRadius = 0.25f;

        [Tooltip("벽에 밀렸을 때 더 가까워지지 않는 하한(m). 여기서는 캐릭터를 디더 페이드로 지운다")]
        [SerializeField] private float _minDistance = 0.8f;

        [Tooltip("당겨질 때 스무딩(초). 늦으면 벽을 뚫고 지나간 화면이 한 순간 보인다")]
        [SerializeField] private float _pullInSmoothTime = 0.05f;

        [Tooltip("풀릴 때 속도(m/s). 즉시 밀면 좁은 길에서 카메라가 튄다")]
        [SerializeField] private float _pushOutSpeed = 6f;

        [Header("렌즈")]
        [Tooltip("고정한다. 달릴 때 넓어지는 연출은 멀미를 만들고 패링 0.32초 판단을 방해한다")]
        [SerializeField] private float _fieldOfView = 60f;

        [Header("셰이크")]
        [Tooltip("전체 배수. 접근성 옵션이 0~1로 민다 — 0이면 완전히 꺼진다")]
        [Range(0f, 1f)]
        [SerializeField] private float _shakeScale = 1f;

        [Tooltip("여러 셰이크가 겹쳐도 넘지 않는 상한(m)")]
        [SerializeField] private float _shakeMaxAmplitude = 0.3f;

        [Tooltip("가벼운 피격")]
        [SerializeField] private ShakeSpec _shakeHitLight = new ShakeSpec { Amplitude = 0.06f, Duration = 0.12f };

        [Tooltip("강한 피격")]
        [SerializeField] private ShakeSpec _shakeHitHeavy = new ShakeSpec { Amplitude = 0.12f, Duration = 0.18f };

        [Tooltip("패링 성공. 결과를 알리는 셰이크라 세도 된다")]
        [SerializeField] private ShakeSpec _shakeParrySuccess = new ShakeSpec { Amplitude = 0.20f, Duration = 0.15f };

        [Tooltip("치명타")]
        [SerializeField] private ShakeSpec _shakeCritical = new ShakeSpec { Amplitude = 0.15f, Duration = 0.15f };

        [Tooltip("보스 그로기 진입. 셋이 동시에 달려드는 신호다")]
        [SerializeField] private ShakeSpec _shakeGroggyEnter = new ShakeSpec { Amplitude = 0.25f, Duration = 0.30f };

        [Tooltip("보스 부위 파괴(꼬리 한 덩이). 가장 센 셰이크 — 25% 게이트를 한 칸 여는 순간이다")]
        [SerializeField] private ShakeSpec _shakePartBreak = new ShakeSpec { Amplitude = 0.30f, Duration = 0.35f };

        /****************************************
        *              Properties
        ****************************************/

        public float PivotHeight      => _pivotHeight;
        public float Distance         => _distance;
        public float SideOffset       => _sideOffset;
        public float FollowSmoothTime => _followSmoothTime;

        public float DefaultSensitivityX => _defaultSensitivityX;
        public float DefaultSensitivityY => _defaultSensitivityY;
        public bool  DefaultInvertY      => _defaultInvertY;
        public float PitchMin            => _pitchMin;
        public float PitchMax            => _pitchMax;

        public LayerMask CollisionMask    => _collisionMask;
        public float     ProbeRadius      => _probeRadius;
        public float     MinDistance      => _minDistance;
        public float     PullInSmoothTime => _pullInSmoothTime;
        public float     PushOutSpeed     => _pushOutSpeed;

        public float FieldOfView => _fieldOfView;

        /// <summary> 셰이크 전체 배수(0~1). 접근성 옵션이 밀고 0이면 화면이 전혀 흔들리지 않는다 </summary>
        public float ShakeScale => Mathf.Clamp01(_shakeScale);

        public float ShakeMaxAmplitude => _shakeMaxAmplitude;

        public ShakeSpec ShakeParrySuccess => _shakeParrySuccess;
        public ShakeSpec ShakeCritical     => _shakeCritical;
        public ShakeSpec ShakeGroggyEnter  => _shakeGroggyEnter;
        public ShakeSpec ShakePartBreak    => _shakePartBreak;

        /// <summary>
        /// 피격 강도에 맞는 셰이크. Critical은 치명타 셰이크를 쓴다 -
        /// HitStrength와 셰이크 종류가 1:1이 아니라 호출부가 아니라 여기서 맞춘다
        /// </summary>
        public ShakeSpec ResolveHitShake(HitStrength strength)
        {
            switch (strength)
            {
                case HitStrength.Heavy:    return _shakeHitHeavy;
                case HitStrength.Critical: return _shakeCritical;
                default:                   return _shakeHitLight;
            }
        }
    }
}
