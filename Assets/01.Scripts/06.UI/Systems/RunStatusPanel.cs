// Unity
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Network;

namespace Geumeum.UI
{
    /// <summary> 보스 입구 카운트다운과 런 정지 사유를 로컬 사용자에게 보여 준다 </summary>
    [DefaultExecutionOrder(1000)]
    public class RunStatusPanel : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("패널")]
        [SerializeField] private CanvasGroup _canvasGroup;

        [Header("상태 문구")]
        [SerializeField] private TMP_Text _countdownText;
        [SerializeField] private TMP_Text _pauseText;

        [Header("호스트 동작")]
        [SerializeField] private Button _abandonButton;

        private LocalGameplayInputToken _inputToken;
        private RunManager              _boundRun;
        private bool                    _requestSent;
        private bool                    _requestRejected;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (HasMissingReference())
            {
                enabled = false;
                return;
            }

            _abandonButton.onClick.AddListener(RequestAbandon);
            SetPanelVisible(false, false);
        }

        private void Update()
        {
            Refresh();
        }

        private void OnDisable()
        {
            BindRun(null);
            ReleaseInput();
        }

        private void OnDestroy()
        {
            if (_abandonButton != null)
            {
                _abandonButton.onClick.RemoveListener(RequestAbandon);
            }

            BindRun(null);
            ReleaseInput();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Refresh()
        {
            RunManager run = RunManager.LocalInstance;
            BindRun(run);
            if (run == null)
            {
                _requestSent = false;
                _requestRejected = false;
                SetPanelVisible(false, false);
                SetInputOwned(false);
                return;
            }

            bool showCountdown = (run.EntranceState == BossEntranceState.CountingDown);
            _countdownText.gameObject.SetActive(showCountdown);
            if (showCountdown)
            {
                int seconds = Mathf.CeilToInt(run.BossEntranceCountdownRemainingSeconds);
                _countdownText.text = $"보스 입구 개방까지 {seconds}초";
            }

            bool participantMissing = ((run.PauseReasons & RunPauseReason.ParticipantMissing) != 0);
            RunSceneDirector director = RunSceneDirector.LocalInstance;
            bool transitionFailed = ((director != null) &&
                                     (director.State == RunTransitionState.Failed));
            bool showPause = (participantMissing || transitionFailed);

            _pauseText.gameObject.SetActive(showPause);
            if (showPause)
            {
                _pauseText.text = BuildPauseMessage(
                    participantMissing, transitionFailed, director, run.CanLocalAbandonParticipantWait);
                if (_requestRejected)
                {
                    _pauseText.text += "\n요청을 처리하지 못했습니다. 다시 시도해 주세요.";
                }
            }

            bool canAbandon = (showPause && run.CanLocalAbandonParticipantWait);
            if (!canAbandon)
            {
                _requestSent = false;
                _requestRejected = false;
            }

            _abandonButton.gameObject.SetActive(canAbandon);
            _abandonButton.interactable = (canAbandon && (!_requestSent));

            SetPanelVisible(showCountdown || showPause, canAbandon);
            SetInputOwned(canAbandon);
        }

        private static string BuildPauseMessage(bool participantMissing, bool transitionFailed,
            RunSceneDirector director, bool canAbandon)
        {
            string message;
            if (transitionFailed)
            {
                string failure = ((director == null) || string.IsNullOrWhiteSpace(director.FailureMessage))
                    ? "씬 전환을 완료하지 못했습니다."
                    : director.FailureMessage;
                message = failure;
            }
            else if (participantMissing)
            {
                message = "참가자의 재접속을 기다리는 중입니다.";
            }
            else
            {
                message = string.Empty;
            }

            return canAbandon
                ? $"{message}\n더 기다릴 수 없다면 런을 종료할 수 있습니다."
                : $"{message}\n호스트가 런 종료 여부를 결정하고 있습니다.";
        }

        private void RequestAbandon()
        {
            if (_requestSent)
            {
                return;
            }

            RunManager run = RunManager.LocalInstance;
            if ((run == null) || (!run.CanLocalAbandonParticipantWait))
            {
                return;
            }

            _requestRejected = false;
            if (!run.RequestAbandonParticipantWait())
            {
                _requestRejected = true;
                _requestSent = false;
                return;
            }

            _requestSent = true;
            _abandonButton.interactable = false;
        }

        private void BindRun(RunManager run)
        {
            if (ReferenceEquals(_boundRun, run))
            {
                return;
            }

            if (!ReferenceEquals(_boundRun, null))
            {
                _boundRun.AbandonParticipantWaitRequestCompleted -=
                    HandleAbandonRequestCompleted;
            }

            _boundRun = run;
            _requestSent = false;
            _requestRejected = false;
            if (!ReferenceEquals(_boundRun, null))
            {
                _boundRun.AbandonParticipantWaitRequestCompleted +=
                    HandleAbandonRequestCompleted;
            }
        }

        private void HandleAbandonRequestCompleted(bool accepted)
        {
            if (accepted)
            {
                _requestRejected = false;
                return;
            }

            _requestSent = false;
            _requestRejected = true;
        }

        private void SetPanelVisible(bool visible, bool receivesInput)
        {
            _canvasGroup.alpha          = visible ? 1f : 0f;
            _canvasGroup.interactable   = (visible && receivesInput);
            _canvasGroup.blocksRaycasts = (visible && receivesInput);
        }

        private void SetInputOwned(bool shouldOwn)
        {
            if (shouldOwn && (_inputToken == null))
            {
                _inputToken = LocalGameplayInputGate.Acquire();
            }

            if (shouldOwn)
            {
                LocalGameplayInputGate.Maintain(_inputToken);
                return;
            }

            ReleaseInput();
        }

        private void ReleaseInput()
        {
            if (_inputToken == null)
            {
                return;
            }

            _inputToken.Dispose();
            _inputToken = null;
        }

        private bool HasMissingReference()
        {
            if ((_canvasGroup != null) && (_countdownText != null) &&
                (_pauseText != null) && (_abandonButton != null))
            {
                return false;
            }

            Debug.LogError("[RunStatusPanel] CanvasGroup, 문구 또는 포기 버튼이 연결되지 않았다", this);
            return true;
        }
    }
}
