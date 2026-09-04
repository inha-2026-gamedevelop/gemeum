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
        }
    }
}
