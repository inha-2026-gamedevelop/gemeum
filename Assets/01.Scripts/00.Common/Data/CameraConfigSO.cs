// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Data
{
    /// <summary> 3인칭 카메라의 거리, 회전, 충돌 수치 </summary>
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

        [Header("락온 거리")]
        [Tooltip("잡귀를 잠갔을 때의 붐 길이(m). 기본 거리와 같게 두는 것이 시작값이다")]
        [SerializeField] private float _lockOnDistanceEnemy = 4.5f;

        [Tooltip("보스 몸통(m). 꼬리 펼침이 프레임에 들어와야 해서 멀다 - 안 보이면 싸울 수 없다")]
        [SerializeField] private float _lockOnDistanceBossBody = 9f;

        [Tooltip("보스 부위(꼬리 한 덩이)(m). 그로기 10초 동안 표식 꼬리를 치기 좋은 거리")]
        [SerializeField] private float _lockOnDistanceBossPart = 7f;

        [Tooltip("락온 대상을 향해 카메라가 도는 속도(도/초). 즉시 돌리면 대상이 빠르게 움직일 때 화면이 튄다")]
        [SerializeField] private float _lockOnTurnDegreesPerSecond = 540f;

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
        [Tooltip("전체 배수. 접근성 옵션이 0~1로 민다 - 0이면 완전히 꺼진다")]
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

        [Tooltip("보스 부위 파괴(꼬리 한 덩이). 가장 센 셰이크 - 25% 게이트를 한 칸 여는 순간이다")]
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

        public float LockOnDistanceEnemy         => _lockOnDistanceEnemy;
        public float LockOnDistanceBossBody      => _lockOnDistanceBossBody;
        public float LockOnDistanceBossPart      => _lockOnDistanceBossPart;
        public float LockOnTurnDegreesPerSecond  => _lockOnTurnDegreesPerSecond;

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

        /// <summary> 피격 강도에 맞는 셰이크 </summary>
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
