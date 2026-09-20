// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.AI;
using Geumeum.Common;

namespace Geumeum.Art
{
    /// <summary>
    /// TargetedDash 템플릿 중 2페이즈 패턴 7(마수 돌진) 예고가 뜬 동안 보스 모델을 숨겨
    /// 순간이동처럼 보이게 한다. 서버가 확정한 cue 시각(StartServerTime~ImpactServerTime)만
    /// 읽어 표현만 한다, 판정에는 관여하지 않는다. 도착 시각(=impact)에 다시 보인다.
    /// 패턴 7 직후 Phase2State가 거대 꼬리치기(패턴6)를 강제로 이어 붙인다
    /// </summary>
    public sealed class BossDashVisibilityView : MonoBehaviour
    {
        /****************************************
        *                Types
        ****************************************/

        private struct HiddenWindow
        {
            public uint   AttackInstanceId;
            public double StartServerTime;
            public double EndServerTime;
        }

        /****************************************
        *              Constants
        ****************************************/

        private const int TARGETED_DASH_PATTERN_ID = 7; // 마수 돌진, Phase2State.TARGETED_DASH_PATTERN_ID와 같은 값

        /****************************************
        *                Fields
        ****************************************/

        private IBossTelegraphSource _telegraphSource;
        private Renderer[]           _renderers;
        private bool                 _isSubscribed;
        private HiddenWindow?        _activeWindow;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _telegraphSource = GetComponentInParent<IBossTelegraphSource>();

            // root 전체를 잡으면 꼬리(PartVisual)·원혼(SoulVisual) 같은 형제 비주얼까지 같이
            // 켜고 꺼버린다 - 이 오브젝트(Model) 아래 본체 스킨 메쉬만 대상으로 한다
            _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            _activeWindow = null;
            SetVisible(true);
        }

        private void Update()
        {
            if (_activeWindow == null)
            {
                return;
            }

            double now = NetworkTime.time;
            HiddenWindow window = _activeWindow.Value;

            if (now >= window.EndServerTime)
            {
                _activeWindow = null;
                SetVisible(true);
                return;
            }

            SetVisible(now < window.StartServerTime);
        }

        /****************************************
        *                Methods
        ****************************************/

        private void TrySubscribe()
        {
            if ((_isSubscribed) || (_telegraphSource == null))
            {
                return;
            }

            _telegraphSource.TelegraphChanged += HandleTelegraphChanged;
            _isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_isSubscribed)
            {
                return;
            }

            _telegraphSource.TelegraphChanged -= HandleTelegraphChanged;
            _isSubscribed = false;
        }

        private void HandleTelegraphChanged(BossTelegraphCue cue)
        {
            if ((cue.PatternId != TARGETED_DASH_PATTERN_ID) || (cue.RepeatIndex != 0))
            {
                return;
            }

            if (!cue.IsActive)
            {
                if ((_activeWindow != null) && (_activeWindow.Value.AttackInstanceId == cue.AttackInstanceId))
                {
                    _activeWindow = null;
                    SetVisible(true);
                }

                return;
            }

            _activeWindow = new HiddenWindow
            {
                AttackInstanceId = cue.AttackInstanceId,
                StartServerTime  = cue.StartServerTime,
                EndServerTime    = cue.ImpactServerTime,
            };
        }

        private void SetVisible(bool visible)
        {
            for (int i = 0; i < _renderers.Length; ++i)
            {
                if (_renderers[i] != null)
                {
                    _renderers[i].enabled = visible;
                }
            }
        }
    }
}
