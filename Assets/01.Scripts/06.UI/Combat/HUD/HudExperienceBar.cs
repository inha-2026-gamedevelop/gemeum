// Unity
using UnityEngine;
using UnityEngine.UI;

// 프로젝트 내부
using Geumeum.Combat;
using Geumeum.Data;
using Geumeum.Systems;

namespace Geumeum.UI
{
    /// <summary>
    /// 화면 맨 아래 경험치 바. 로컬 플레이어의 현재 레벨 안에서 얼마나 찼는지 보인다
    ///
    /// PlayerStatus의 성장 이벤트(GrowthChanged)는 서버에서만 불린다. 클라이언트는 SyncVar로 값만 받으므로
    /// 레벨과 누적 경험치 두 값을 매 프레임 비교하고, 바뀐 때만 바를 다시 그린다
    ///
    /// 오를 때는 체력 바 잔상을 거꾸로 쓴다 - 얻은 만큼 밝은 막대가 먼저 차고, 지연 뒤 채움이 따라 오른다.
    /// 잔상 규칙(EnemyHealthBarTrail)은 줄어드는 쪽을 따라가므로 빈 비율(1 - 진행률)로 넣어 방향을 뒤집는다.
    /// 레벨이 오르면 끝까지 채운 뒤 비우고 새 구간의 진행률로 다시 오른다
    /// </summary>
    public class HudExperienceBar : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float FULL        = 1f;
        private const float EMPTY       = 0f;
        private const float NO_PENDING  = -1f;
        private const string GAIN_NAME  = "Gain";

        [Tooltip("채움 붓 획. Filled(가로) 이미지의 fillAmount로 채운다 - 늘이면 붓 획이 찌그러진다")]
        [SerializeField] private Image _fill;

        [Tooltip("방금 얻은 만큼 먼저 차는 막대 색. 채움을 복제해 바로 뒤에 깐다")]
        [SerializeField] private Color _gainColor = new Color(1f, 0.878f, 0.541f, 0.9f);

        private PlayerStatus _boundStatus;
        private int          _shownLevel      = -1;
        private int          _shownExperience = -1;

        private Image               _gain;
        private EnemyHealthBarTrail _emptyTrail = EnemyHealthBarTrail.Create(FULL);

        // 레벨이 올라 끝까지 채우는 중이면, 다 찬 뒤 새로 올라갈 진행률. 없으면 NO_PENDING
        private float _pendingProgress = NO_PENDING;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _gain = CreateGain(_fill, _gainColor);
        }

        private void OnEnable()
        {
            // 다시 켜지면 그 사이 바뀐 값을 한 번 반영한다
            _shownLevel = -1;
        }

        private void Update()
        {
            PlayerStatus local = PlayerStatus.Local;
            if (local != _boundStatus)
            {
                // 재접속 등으로 로컬 플레이어가 바뀌었다. 이전 값으로 판단하지 않는다
                _boundStatus = local;
                _shownLevel = -1;
            }

            if (_boundStatus != null)
            {
                Refresh(_boundStatus.Level, _boundStatus.Experience);
            }

            Tick();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 현재 레벨 구간에서 채운 비율. 만렙은 가득 찬 채로 둔다
        /// 곡선을 읽지 못하면 0이다 - 틀린 값을 그리느니 비워 둔다
        /// </summary>
        private static float ResolveProgress(int level, int experience)
        {
            if (!GameDB.IsInitialized || (GameDB.LevelCurve == null))
            {
                return 0f;
            }

            LevelCurveSO curve = GameDB.LevelCurve;
            if (level >= curve.MaxLevel)
            {
                return FULL;
            }

            if (!GrowthRules.TryGetCumulativeExperience(curve, level, out int levelStart) ||
                !GrowthRules.TryGetCumulativeExperience(curve, level + 1, out int levelEnd) ||
                (levelEnd <= levelStart))
            {
                return 0f;
            }

            return Mathf.Clamp01((float)(experience - levelStart) / (levelEnd - levelStart));
        }

        private void Refresh(int level, int experience)
        {
            if ((level == _shownLevel) && (experience == _shownExperience))
            {
                return;
            }

            int previousLevel = _shownLevel;
            _shownLevel = level;
            _shownExperience = experience;

            float progress = ResolveProgress(level, experience);

            // 처음 보거나(-1) 레벨이 내려갔으면(재접속 등) 얻은 것이 아니다. 잔상 없이 맞춘다
            if ((previousLevel < 0) || (level < previousLevel))
            {
                Snap(progress);
                return;
            }

            if (level > previousLevel)
            {
                // 끝까지 채운 뒤 비우고 새 구간으로. 여러 레벨이 한 번에 올라도 한 번만 넘긴다
                _pendingProgress = progress;
                SetProgress(FULL);
                return;
            }

            if (_pendingProgress != NO_PENDING)
            {
                // 아직 앞 레벨을 채우는 중이다. 다 찬 뒤 오를 값만 바꾼다
                _pendingProgress = progress;
                return;
            }

            SetProgress(progress);
        }

        private void Tick()
        {
            if (_emptyTrail.HasChip)
            {
                _emptyTrail.Tick(Time.unscaledTime, Time.unscaledDeltaTime, ResolveDrainPerSecond());
                Apply();
                return;
            }

            if (_pendingProgress == NO_PENDING)
            {
                return;
            }

            float next = _pendingProgress;
            _pendingProgress = NO_PENDING;
            Snap(EMPTY);
            SetProgress(next);
        }

        /// <summary> 오르면 밝은 막대가 먼저 가고 채움이 따라간다. 내려가면 둘 다 바로 맞춘다 </summary>
        private void SetProgress(float progress)
        {
            _emptyTrail.SetRatio(FULL - progress, Time.unscaledTime, ResolveDelaySeconds());
            Apply();
        }

        private void Snap(float progress)
        {
            _pendingProgress = NO_PENDING;
            _emptyTrail = EnemyHealthBarTrail.Create(FULL - progress);
            Apply();
        }

        private void Apply()
        {
            if (_fill != null)
            {
                _fill.fillAmount = FULL - _emptyTrail.Chip;
            }

            if (_gain != null)
            {
                _gain.fillAmount = FULL - _emptyTrail.Health;
            }
        }

        // 잡귀 체력 바 잔상과 같은 값을 쓴다. 설정을 못 읽으면 바로 맞춘다
        private static float ResolveDelaySeconds()
        {
            return GameDB.IsInitialized && (GameDB.Presentation != null)
                ? GameDB.Presentation.EnemyHealthBarChipDelaySeconds
                : 0f;
        }

        private static float ResolveDrainPerSecond()
        {
            return GameDB.IsInitialized && (GameDB.Presentation != null)
                ? GameDB.Presentation.EnemyHealthBarChipDrainPerSecond
                : 0f;
        }

        /// <summary> 채움 이미지를 복제해 바로 뒤에 깐다 - 붓 획 모양이 같아야 겹쳐 보이지 않는다 </summary>
        private static Image CreateGain(Image fill, Color color)
        {
            if (fill == null)
            {
                return null;
            }

            Transform fillTransform = fill.transform;
            Image gain = Instantiate(fill, fillTransform.parent);
            gain.name = GAIN_NAME;
            gain.color = color;
            gain.transform.SetSiblingIndex(fillTransform.GetSiblingIndex());
            gain.fillAmount = fill.fillAmount;
            return gain;
        }
    }
}
