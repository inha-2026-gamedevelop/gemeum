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
    /// </summary>
    public class HudExperienceBar : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float FULL = 1f;

        [Tooltip("채움 붓 획. Filled(가로) 이미지의 fillAmount로 채운다 - 늘이면 붓 획이 찌그러진다")]
        [SerializeField] private Image _fill;

        private PlayerStatus _boundStatus;
        private int          _shownLevel      = -1;
        private int          _shownExperience = -1;

        /****************************************
        *              Unity Event
        ****************************************/

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

            if (_boundStatus == null)
            {
                return;
            }

            int level = _boundStatus.Level;
            int experience = _boundStatus.Experience;
            if ((level == _shownLevel) && (experience == _shownExperience))
            {
                return;
            }

            _shownLevel = level;
            _shownExperience = experience;
            SetProgress(ResolveProgress(level, experience));
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

        private void SetProgress(float progress)
        {
            if (_fill == null)
            {
                return;
            }

            _fill.fillAmount = progress;
        }
    }
}
