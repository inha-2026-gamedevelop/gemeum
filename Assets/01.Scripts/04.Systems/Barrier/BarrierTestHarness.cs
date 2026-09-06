// System
using System.Collections;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Network;

namespace Geumeum.Systems
{
    /// <summary>
    /// 결계만 따로 테스트할 때 로비 준비 없이 런을 강제 시작한다.
    /// Dev_Barrier 전용 테스트 도구 — 정식 플레이 흐름(LobbyController)에는 관여하지 않는다
    /// </summary>
    public class BarrierTestHarness : MonoBehaviour
    {
#if UNITY_EDITOR
        [SerializeField] private BarrierController _barrierController        = null;
        [SerializeField] private float             _testPhaseDurationSeconds = 10f; // 실제 값(수십 분) 대신 확인용으로 짧게 둔다

        // 끄면 RunManager의 진짜 시계(BarrierPhaseSO의 실제 시간값)로만 phase가 넘어가는지 확인할 수 있다.
        // 켜져 있으면 이 하네스가 RunManager를 무시하고 강제로 넘기므로 실제 의존 관계 검증에는 방해가 된다
        [SerializeField] private bool _autoAdvanceEnabled = true;

        private Coroutine _autoAdvanceRoutine;
#endif

        [ContextMenu("Force Start Run (Test)")]
        private void ForceStartRun()
        {
            if (RunManager.ServerInstance == null)
            {
                Debug.LogWarning("[BarrierTestHarness] RunManager.ServerInstance가 없다 — 서버(호스트)로 실행 중인지 확인", this);
                return;
            }

            bool started = RunManager.ServerInstance.StartRun();
            Debug.Log($"[BarrierTestHarness] StartRun 결과: {started}, phase={RunManager.ServerInstance.Phase}", this);

#if UNITY_EDITOR
            if (started && _autoAdvanceEnabled)
            {
                StartAutoAdvance();
            }
#endif
        }

#if UNITY_EDITOR
        /****************************************
        *           Debug (Editor Only)
        ****************************************/

        // RunManager의 실제 타임라인(10~20분 단위)을 기다리지 않고, N초마다 강제로 다음 phase로 넘긴다.
        // BarrierPhaseSO의 실제 시간값은 건드리지 않는다 — 그러면 RunPhaseTimeline.Validate가 깨진다
        private void OnDisable()
        {
            StopAutoAdvance();
        }

        [ContextMenu("Stop Auto Advance (Test)")]
        private void StopAutoAdvance()
        {
            if (_autoAdvanceRoutine == null)
            {
                return;
            }

            StopCoroutine(_autoAdvanceRoutine);
            _autoAdvanceRoutine = null;
        }

        private void StartAutoAdvance()
        {
            StopAutoAdvance();

            if (_barrierController == null)
            {
                Debug.LogWarning("[BarrierTestHarness] BarrierController 참조가 비어 있어 자동 phase 전환을 시작할 수 없다", this);
                return;
            }

            _autoAdvanceRoutine = StartCoroutine(CoAutoAdvance());
        }

        private IEnumerator CoAutoAdvance()
        {
            WaitForSeconds wait = new WaitForSeconds(_testPhaseDurationSeconds);

            while (true)
            {
                yield return wait;
                _barrierController.DebugForceNextPhase();
            }
        }
#endif
    }
}
