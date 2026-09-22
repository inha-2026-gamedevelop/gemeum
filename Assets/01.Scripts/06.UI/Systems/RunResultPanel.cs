// Unity
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Network;

namespace Geumeum.UI
{
    /// <summary> 서버가 동결한 팀 결과와 참가자 기여를 그대로 보여 준다 </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class RunResultPanel : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private TMP_Text     _outcomeText;
        [SerializeField] private TMP_Text     _scoreText;
        [SerializeField] private TMP_Text     _summaryText;
        [SerializeField] private TMP_Text     _evaluationText;
        [SerializeField] private TMP_Text[]   _participantTexts;
        [SerializeField] private Button       _closeButton;

        private RunManager _boundRun;
        private ulong      _displayedSessionId;
        private uint       _displayedRunId;
        private uint       _displayedRevision;
        private bool       _hasDisplayedResult;
        private bool       _isVisible;
        private LocalGameplayInputToken _inputToken;

        private void Awake()
        {
            if (HasMissingReference())
            {
                enabled = false;
                return;
            }

            _closeButton.onClick.AddListener(Hide);
            SetVisible(false);
        }

        private void Update()
        {
            if (_isVisible)
            {
                LocalGameplayInputGate.Maintain(_inputToken);
            }

            BindRun(RunManager.LocalInstance);
            if (_boundRun == null)
            {
                return;
            }

            RunResultSnapshot result = _boundRun.Result;
            if (result.Outcome != RunOutcome.None)
            {
                TryDisplay(result);
                return;
            }

            if (_hasDisplayedResult && (_boundRun.RunId.Value != 0u) &&
                (_boundRun.RunId.Value != _displayedRunId))
            {
                ClearDisplayedResult();
            }
        }

        private void OnDisable()
        {
            SetVisible(false);
            BindRun(null);
        }

        private void OnDestroy()
        {
            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(Hide);
            }

            BindRun(null);
        }

        private void BindRun(RunManager run)
        {
            if (ReferenceEquals(_boundRun, run))
            {
                return;
            }

            if (!ReferenceEquals(_boundRun, null))
            {
                _boundRun.ResultChanged -= HandleResultChanged;
            }

            _boundRun = run;
            if (ReferenceEquals(_boundRun, null))
            {
                ClearDisplayedResult();
                return;
            }

            _boundRun.ResultChanged += HandleResultChanged;
            if (_boundRun.Result.Outcome != RunOutcome.None)
            {
                TryDisplay(_boundRun.Result);
            }
            else if (_hasDisplayedResult)
            {
                ClearDisplayedResult();
            }
        }

        private void HandleResultChanged(RunResultSnapshot result)
        {
            if (result.Outcome == RunOutcome.None)
            {
                if (_hasDisplayedResult && (result.RunIdValue != _displayedRunId))
                {
                    ClearDisplayedResult();
                }

                return;
            }

            TryDisplay(result);
        }

        private void TryDisplay(in RunResultSnapshot result)
        {
            if (_hasDisplayedResult &&
                (result.SessionIdValue == _displayedSessionId) &&
                (result.RunIdValue == _displayedRunId) &&
                (result.ResultRevision <= _displayedRevision))
            {
                return;
            }

            if (!RunResultViewAdapter.TryCreate(result, out RunResultViewData view))
            {
                ShowUnavailableResult(result);
                return;
            }

            _displayedSessionId = view.SessionIdValue;
            _displayedRunId     = view.RunIdValue;
            _displayedRevision  = view.ResultRevision;
            _hasDisplayedResult = true;

            _outcomeText.text = FunctionalUiText.GetOutcome(view.Outcome);
            if (view.HasAuthoritativeScore)
            {
                _scoreText.text = $"팀 점수 {view.Score:N0}점, " +
                    FunctionalUiText.GetEvaluationGrade(view.EvaluationGrade);
                _evaluationText.text = view.EvaluationMessage;
            }
            else
            {
                _scoreText.text = "점수를 확정하지 못했습니다.";
                _evaluationText.text = string.IsNullOrWhiteSpace(view.EvaluationMessage)
                    ? "점수 설정을 확인해 주세요."
                    : view.EvaluationMessage;
            }

            _summaryText.text =
                ((view.PartyKind == RunPartyKind.BotAssisted) ? "봇 포함 파티\n" :
                    (view.PartyKind == RunPartyKind.HumansOnly) ? "사람 파티\n" : "이전 기록 (편성 정보 없음)\n") +
                $"진행 시간 {FormatTime(view.FieldElapsedSeconds + view.BossElapsedSeconds)}" +
                $"\n잡귀 처치 {view.EnemyKills:N0}, 사망 {view.DeathCount:N0}" +
                $"\n남은 노잣돈 {view.RemainingCurrency:N0}, 남은 체력 합 {view.RemainingHealthSum:0.#}";

            RunParticipantResultSnapshot[] participants = view.Participants;
            for (int i = 0; i < _participantTexts.Length; ++i)
            {
                bool hasParticipant = i < participants.Length;
                _participantTexts[i].gameObject.SetActive(hasParticipant);
                if (hasParticipant)
                {
                    _participantTexts[i].text = DescribeParticipant(participants[i]);
                }
            }

            SetVisible(true);
        }

        private void ShowUnavailableResult(in RunResultSnapshot result)
        {
            _displayedSessionId = result.SessionIdValue;
            _displayedRunId     = result.RunIdValue;
            _displayedRevision  = result.ResultRevision;
            _hasDisplayedResult = true;

            _outcomeText.text    = FunctionalUiText.GetOutcome(result.Outcome);
            _scoreText.text      = "결과를 확정하지 못했습니다.";
            _summaryText.text    = "참가자 기여 정보를 불러올 수 없습니다.";
            _evaluationText.text = string.IsNullOrWhiteSpace(result.EvaluationMessage)
                ? "결과 설정을 확인해 주세요."
                : result.EvaluationMessage;

            for (int i = 0; i < _participantTexts.Length; ++i)
            {
                _participantTexts[i].gameObject.SetActive(false);
            }

            SetVisible(true);
        }

        private void ClearDisplayedResult()
        {
            _displayedSessionId = 0UL;
            _displayedRunId     = 0u;
            _displayedRevision  = 0u;
            _hasDisplayedResult = false;
            SetVisible(false);
        }

        private void Hide()
        {
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            _isVisible = visible;
            if (visible && (_inputToken == null))
            {
                _inputToken = LocalGameplayInputGate.Acquire();
            }
            else if ((!visible) && (_inputToken != null))
            {
                _inputToken.Dispose();
                _inputToken = null;
            }

            _canvasGroup.alpha          = visible ? 1f : 0f;
            _canvasGroup.interactable   = visible;
            _canvasGroup.blocksRaycasts = visible;
        }

        private bool HasMissingReference()
        {
            if ((_canvasGroup != null) && (_outcomeText != null) && (_scoreText != null) &&
                (_summaryText != null) && (_evaluationText != null) &&
                (_participantTexts != null) && (_participantTexts.Length > 0) &&
                (_closeButton != null))
            {
                return false;
            }

            Debug.LogError("[RunResultPanel] 결과 화면 참조가 연결되지 않았다", this);
            return true;
        }

        private static string DescribeParticipant(in RunParticipantResultSnapshot participant)
        {
            string name = participant.HasCharacter
                ? FunctionalUiText.GetCharacterName(participant.Character)
                : "참가자";
            return $"{name}{(participant.ParticipantKind == ParticipantKind.Bot ? " (봇)" : string.Empty)}" +
                $"\n피해 {participant.DamageDealt:0.#}, 타인 치유 {participant.HealingDone:0.#}" +
                $"\n구조 {participant.RescueCount}, 삼켜짐 구조 {participant.SwallowedRescueCount}, " +
                $"사망 {participant.DeathCount}" +
                $"\n패링 {participant.ParryCounterCount}, 정밀 {participant.PrecisionCounterCount}, " +
                $"공명 {participant.ResonanceCounterCount}, 감소 {participant.CounterReductionTotal:0.#}";
        }

        private static string FormatTime(float seconds)
        {
            int totalSeconds = Mathf.Max(0, Mathf.FloorToInt(seconds));
            int minutes = totalSeconds / 60;
            int remainder = totalSeconds % 60;
            return $"{minutes:00}:{remainder:00}";
        }
    }
}
