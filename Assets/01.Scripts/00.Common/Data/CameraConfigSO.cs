// System
using System;
using System.Collections.Generic;

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

        [Tooltip("관리 창이 열렸을 때 추가로 미는 측면 오프셋(m). 창이 덮는 쪽 반대로 캐릭터를 보낸다. "
            + "FOV 60 · 거리 4.5m에서 화면 가로가 약 9.2m라 1m가 화면 폭의 약 11%다")]
        [SerializeField] private float _menuSideOffset = 1.5f;

        [Tooltip("대상 추적 스무딩(초). 회전은 스무딩하지 않는다 - 하면 입력 지연으로 느껴진다")]
        [SerializeField] private float _followSmoothTime = 0.08f;

        [Header("회전")]
        [Tooltip("런을 시작할 때의 내려다보는 각도(도). 씬에 놓인 카메라 각도를 쓰면 씬을 만지는 사람에 따라 게임 시작 각도가 따라 바뀐다")]
        [SerializeField] private float _defaultPitch = 22f;

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
        public float MenuSideOffset   => _menuSideOffset;
        public float FollowSmoothTime => _followSmoothTime;

        public float DefaultPitch        => _defaultPitch;
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS

        /****************************************
        *              Dev Tuning
        ****************************************/

        /// <summary>
        /// 조정 툴이 다루는 값 하나. 라벨을 여기에 두는 이유는 설명 문구가 필드에서 멀어지면
        /// 값을 고칠 때 문구가 같이 안 고쳐져 금방 거짓말이 되기 때문이다
        /// </summary>
        public readonly struct Tunable
        {
            public string Group  { get; }
            public string Id     { get; }   // JSON 키. 라벨을 고쳐도 저장한 값이 살아남는다
            public string Before { get; }   // 값 앞에 오는 말
            public string After  { get; }   // 값 뒤에 오는 말
            public float  Min    { get; }
            public float  Max    { get; }

            public Func<CameraConfigSO, float>   Get { get; }
            public Action<CameraConfigSO, float> Set { get; }

            public Tunable(string group, string id, string before, string after, float min, float max,
                Func<CameraConfigSO, float> get, Action<CameraConfigSO, float> set)
            {
                Group  = group;
                Id     = id;
                Before = before;
                After  = after;
                Min    = min;
                Max    = max;
                Get    = get;
                Set    = set;
            }
        }

        private static Tunable[] _tunables = null;

        /// <summary> 조정 가능한 값 목록. 툴은 필드 이름을 몰라도 된다 </summary>
        public static IReadOnlyList<Tunable> Tunables
        {
            get
            {
                if (_tunables == null)
                {
                    _tunables = BuildTunables();
                }

                return _tunables;
            }
        }

        private static Tunable[] BuildTunables()
        {
            return new[]
            {
                new Tunable("추적", "pivot-height", "카메라가 도는 중심을 발밑에서", "m 올린다 (눈높이)", 0.5f, 2.5f,
                    c => c._pivotHeight, (c, v) => c._pivotHeight = v),
                new Tunable("추적", "distance", "그 중심에서", "m 떨어져 따라간다", 1.5f, 12.0f,
                    c => c._distance, (c, v) => c._distance = v),
                new Tunable("추적", "side-offset", "화면 중앙을 비우려고 옆으로", "m 민다", -2.0f, 2.0f,
                    c => c._sideOffset, (c, v) => c._sideOffset = v),
                new Tunable("추적", "menu-side-offset", "관리 창이 열리면 옆으로", "m 더 민다", 0.0f, 4.0f,
                    c => c._menuSideOffset, (c, v) => c._menuSideOffset = v),
                new Tunable("추적", "follow-smooth", "대상이 움직이면", "초에 걸쳐 부드럽게 쫓아간다", 0.0f, 0.5f,
                    c => c._followSmoothTime, (c, v) => c._followSmoothTime = v),
                new Tunable("회전", "default-pitch", "런을 시작할 때 카메라는", "도 내려다본다", -30f, 60f,
                    c => c._defaultPitch, (c, v) => c._defaultPitch = v),
                new Tunable("회전", "sens-x", "마우스를 좌우로 1픽셀 움직이면", "도 돈다", 0.01f, 0.6f,
                    c => c._defaultSensitivityX, (c, v) => c._defaultSensitivityX = v),
                new Tunable("회전", "sens-y", "상하로 1픽셀 움직이면", "도 돈다", 0.01f, 0.6f,
                    c => c._defaultSensitivityY, (c, v) => c._defaultSensitivityY = v),
                new Tunable("회전", "pitch-min", "위로는", "도까지만 올려다본다", -89.0f, 0.0f,
                    c => c._pitchMin, (c, v) => c._pitchMin = v),
                new Tunable("회전", "pitch-max", "아래로는", "도까지만 내려다본다", 0.0f, 89.0f,
                    c => c._pitchMax, (c, v) => c._pitchMax = v),
                new Tunable("락온", "lock-enemy", "잡귀를 잠그면", "m 거리에서 본다", 2.0f, 15.0f,
                    c => c._lockOnDistanceEnemy, (c, v) => c._lockOnDistanceEnemy = v),
                new Tunable("락온", "lock-boss-body", "보스 몸통을 잠그면", "m 거리에서 본다", 3.0f, 25.0f,
                    c => c._lockOnDistanceBossBody, (c, v) => c._lockOnDistanceBossBody = v),
                new Tunable("락온", "lock-boss-part", "보스 꼬리를 잠그면", "m 거리에서 본다", 3.0f, 25.0f,
                    c => c._lockOnDistanceBossPart, (c, v) => c._lockOnDistanceBossPart = v),
                new Tunable("락온", "lock-turn", "락온 대상을 향해 초당", "도씩 돌아간다", 90.0f, 1440.0f,
                    c => c._lockOnTurnDegreesPerSecond, (c, v) => c._lockOnTurnDegreesPerSecond = v),
                new Tunable("충돌", "probe-radius", "벽을 감지하는 구의 반지름은", "m다", 0.05f, 1.0f,
                    c => c._probeRadius, (c, v) => c._probeRadius = v),
                new Tunable("충돌", "min-distance", "벽에 밀려도", "m보다 가까워지지 않는다", 0.2f, 3.0f,
                    c => c._minDistance, (c, v) => c._minDistance = v),
                new Tunable("충돌", "pull-in", "벽에 걸리면", "초에 걸쳐 당겨진다", 0.0f, 0.4f,
                    c => c._pullInSmoothTime, (c, v) => c._pullInSmoothTime = v),
                new Tunable("충돌", "push-out", "벽에서 풀리면 초당", "m씩 밀려난다", 0.5f, 20.0f,
                    c => c._pushOutSpeed, (c, v) => c._pushOutSpeed = v),
                new Tunable("렌즈", "fov", "화각은", "도로 고정한다", 30.0f, 100.0f,
                    c => c._fieldOfView, (c, v) => c._fieldOfView = v),
                new Tunable("셰이크", "shake-scale", "모든 흔들림에", "배를 곱한다 (0이면 완전히 끈다)", 0.0f, 1.0f,
                    c => c._shakeScale, (c, v) => c._shakeScale = v),
                new Tunable("셰이크", "shake-max", "여러 흔들림이 겹쳐도", "m를 넘지 않는다", 0.0f, 1.0f,
                    c => c._shakeMaxAmplitude, (c, v) => c._shakeMaxAmplitude = v),

                new Tunable("셰이크", "hit-light-amp", "가벼운 피격이 나면", "m 흔들리고", 0f, 0.6f,
                    c => c._shakeHitLight.Amplitude, (c, v) => c._shakeHitLight.Amplitude = v),
                new Tunable("셰이크", "hit-light-dur", "그 흔들림은", "초에 걸쳐 잦아든다", 0.02f, 1f,
                    c => c._shakeHitLight.Duration, (c, v) => c._shakeHitLight.Duration = v),
                new Tunable("셰이크", "hit-heavy-amp", "강한 피격이 나면", "m 흔들리고", 0f, 0.6f,
                    c => c._shakeHitHeavy.Amplitude, (c, v) => c._shakeHitHeavy.Amplitude = v),
                new Tunable("셰이크", "hit-heavy-dur", "그 흔들림은", "초에 걸쳐 잦아든다", 0.02f, 1f,
                    c => c._shakeHitHeavy.Duration, (c, v) => c._shakeHitHeavy.Duration = v),
                new Tunable("셰이크", "parry-amp", "패링 성공이 나면", "m 흔들리고", 0f, 0.6f,
                    c => c._shakeParrySuccess.Amplitude, (c, v) => c._shakeParrySuccess.Amplitude = v),
                new Tunable("셰이크", "parry-dur", "그 흔들림은", "초에 걸쳐 잦아든다", 0.02f, 1f,
                    c => c._shakeParrySuccess.Duration, (c, v) => c._shakeParrySuccess.Duration = v),
                new Tunable("셰이크", "critical-amp", "치명타이 나면", "m 흔들리고", 0f, 0.6f,
                    c => c._shakeCritical.Amplitude, (c, v) => c._shakeCritical.Amplitude = v),
                new Tunable("셰이크", "critical-dur", "그 흔들림은", "초에 걸쳐 잦아든다", 0.02f, 1f,
                    c => c._shakeCritical.Duration, (c, v) => c._shakeCritical.Duration = v),
                new Tunable("셰이크", "groggy-amp", "보스 그로기 진입이 나면", "m 흔들리고", 0f, 0.6f,
                    c => c._shakeGroggyEnter.Amplitude, (c, v) => c._shakeGroggyEnter.Amplitude = v),
                new Tunable("셰이크", "groggy-dur", "그 흔들림은", "초에 걸쳐 잦아든다", 0.02f, 1f,
                    c => c._shakeGroggyEnter.Duration, (c, v) => c._shakeGroggyEnter.Duration = v),
                new Tunable("셰이크", "part-break-amp", "보스 부위 파괴이 나면", "m 흔들리고", 0f, 0.6f,
                    c => c._shakePartBreak.Amplitude, (c, v) => c._shakePartBreak.Amplitude = v),
                new Tunable("셰이크", "part-break-dur", "그 흔들림은", "초에 걸쳐 잦아든다", 0.02f, 1f,
                    c => c._shakePartBreak.Duration, (c, v) => c._shakePartBreak.Duration = v),
            };
        }

#endif
    }
}
