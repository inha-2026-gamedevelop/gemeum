// Unity
using UnityEngine;

namespace Geumeum.Data
{
    /// <summary>
    /// 잡귀 머리 위 체력 바 수치. 각자 화면의 표현 값이고 판정과 무관하다
    /// 동기화된 체력이 줄면 잠깐 띄우고, 깎인 부분은 밝은 잔상으로 남았다가 천천히 줄어든다
    /// </summary>
    public partial class PresentationConfigSO
    {
        /****************************************
        *              Constants
        ****************************************/

        // 멀리 있는 바를 줄일 때의 하한 범위. 너무 작으면 읽을 수 없고, 1보다 크면 멀수록 커진다
        private const float ENEMY_HEALTH_BAR_MIN_SCALE_FLOOR   = 0.1f;
        private const float ENEMY_HEALTH_BAR_MIN_SCALE_CEILING = 1f;

        /****************************************
        *                Fields
        ****************************************/

        [Header("잡귀 머리 위 체력 바")]
        [Tooltip("동시에 떠 있는 바 최대 수. 넘치면 가장 오래 맞지 않은 바를 거둔다. 게임 시작 때 한 번 읽는다")]
        [SerializeField, Min(1)] private int _enemyHealthBarMaxOnScreen = 12;

        [Tooltip("마지막으로 체력이 준 뒤 바를 온전히 보이는 시간(초)")]
        [SerializeField, Min(0f)] private float _enemyHealthBarVisibleSeconds = 3f;

        [Tooltip("표시 시간이 끝난 뒤 사라지는 데 걸리는 시간(초). 0이면 바로 사라진다")]
        [SerializeField, Min(0f)] private float _enemyHealthBarFadeSeconds = 0.4f;

        [Tooltip("깎인 부분(잔상)이 줄기 시작하기 전 멈춰 있는 시간(초). 맞을 때마다 다시 센다")]
        [SerializeField, Min(0f)] private float _enemyHealthBarChipDelaySeconds = 0.4f;

        [Tooltip("잔상이 줄어드는 속도(초당 전체 비율). 0.6이면 1초에 바 전체의 60%만큼 줄어든다. 0이면 지연 뒤 바로 맞춘다")]
        [SerializeField, Min(0f)] private float _enemyHealthBarChipDrainPerSecond = 0.6f;

        [Tooltip("바 가로 길이(px, 1080p 기준)")]
        [SerializeField, Min(1f)] private float _enemyHealthBarWidth = 96f;

        [Tooltip("바 세로 길이(px, 1080p 기준)")]
        [SerializeField, Min(1f)] private float _enemyHealthBarHeight = 9f;

        [Tooltip("배경 테두리 두께(px, 1080p 기준). 체력·잔상 막대가 이만큼 안쪽에 그려진다")]
        [SerializeField, Min(0f)] private float _enemyHealthBarBorder = 1.5f;

        [Tooltip("몸 콜라이더(없으면 렌더러) 윗면에서 바를 더 띄우는 높이(m)")]
        [SerializeField, Min(0f)] private float _enemyHealthBarHeadMargin = 0.35f;

        [Tooltip("콜라이더·렌더러가 없어 머리 높이를 알 수 없을 때 발밑에서 잰 바 높이(m)")]
        [SerializeField, Min(0f)] private float _enemyHealthBarFallbackHeight = 2f;

        [Tooltip("이 거리(m)보다 멀면 바를 거리에 반비례해 줄인다. 0이면 거리와 무관하게 같은 크기")]
        [SerializeField, Min(0f)] private float _enemyHealthBarScaleReferenceDistance = 8f;

        [Tooltip("멀리 있어도 이 배율보다 작게 줄이지 않는다")]
        [SerializeField, Range(ENEMY_HEALTH_BAR_MIN_SCALE_FLOOR, ENEMY_HEALTH_BAR_MIN_SCALE_CEILING)] private float _enemyHealthBarMinScale = 0.6f;

        [Tooltip("배경색. 어두운 남색")]
        [SerializeField] private Color _enemyHealthBarBackgroundColor = new Color(0.05f, 0.06f, 0.13f, 0.85f);

        [Tooltip("남은 체력 막대 색. 탁한 붉은색")]
        [SerializeField] private Color _enemyHealthBarFillColor = new Color(0.62f, 0.14f, 0.13f, 1f);

        [Tooltip("깎인 부분 잔상 색. 밝은 미색")]
        [SerializeField] private Color _enemyHealthBarChipColor = new Color(0.96f, 0.91f, 0.78f, 1f);

        /****************************************
        *              Properties
        ****************************************/

        public int   EnemyHealthBarMaxOnScreen            => Mathf.Max(1, _enemyHealthBarMaxOnScreen);
        public float EnemyHealthBarVisibleSeconds         => Mathf.Max(0f, _enemyHealthBarVisibleSeconds);
        public float EnemyHealthBarFadeSeconds            => Mathf.Max(0f, _enemyHealthBarFadeSeconds);
        public float EnemyHealthBarChipDelaySeconds       => Mathf.Max(0f, _enemyHealthBarChipDelaySeconds);
        public float EnemyHealthBarChipDrainPerSecond     => Mathf.Max(0f, _enemyHealthBarChipDrainPerSecond);
        public float EnemyHealthBarWidth                  => Mathf.Max(1f, _enemyHealthBarWidth);
        public float EnemyHealthBarHeight                 => Mathf.Max(1f, _enemyHealthBarHeight);
        public float EnemyHealthBarBorder                 => Mathf.Max(0f, _enemyHealthBarBorder);
        public float EnemyHealthBarHeadMargin             => Mathf.Max(0f, _enemyHealthBarHeadMargin);
        public float EnemyHealthBarFallbackHeight         => Mathf.Max(0f, _enemyHealthBarFallbackHeight);
        public float EnemyHealthBarScaleReferenceDistance => Mathf.Max(0f, _enemyHealthBarScaleReferenceDistance);
        public float EnemyHealthBarMinScale               => Mathf.Clamp(_enemyHealthBarMinScale,
            ENEMY_HEALTH_BAR_MIN_SCALE_FLOOR, ENEMY_HEALTH_BAR_MIN_SCALE_CEILING);
        public Color EnemyHealthBarBackgroundColor        => _enemyHealthBarBackgroundColor;
        public Color EnemyHealthBarFillColor              => _enemyHealthBarFillColor;
        public Color EnemyHealthBarChipColor              => _enemyHealthBarChipColor;
    }
}
