// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Dev;

namespace Geumeum.Network
{
    /// <summary>
    /// 키로 씬 전환을 건다. 개발 확인용이고 서버에서만 동작한다.
    /// 화면 표시는 없다 - 씬과 전환 상태, 같은 동작의 버튼은 브라우저 개발 패널(RunTestPanel)에 있다
    /// </summary>
    public class RunSceneDebugTrigger : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS

        /****************************************
        *                Preload
        ****************************************/

        /// <summary>
        /// 씬에 배치하지 않아도 실행 시작에 스스로 붙는다.
        /// 씬이나 프리팹에 심으면 시작 씬이 바뀔 때 통째로 사라진다
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Preload()
        {
            DevToolHost.Attach<RunSceneDebugTrigger>();
        }

        /****************************************
        *                Fields
        ****************************************/

        [Header("키와 대상 씬")]
        [SerializeField] private KeyCode    _transitionKeyCode = KeyCode.F5;
        [SerializeField] private RunSceneId _targetScene       = RunSceneId.Boss;

        [Header("전체 부활")]
        [SerializeField] private KeyCode _reviveAllKeyCode = KeyCode.F4;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            if (!NetworkServer.active)
            {
                return;
            }

            // 부활은 씬 전환과 달리 RunSceneDirector가 없어도 된다
            if (Input.GetKeyDown(_reviveAllKeyCode))
            {
                Geumeum.Combat.ReviveController.ServerReviveAll();
            }

            if (RunSceneDirector.ServerInstance == null)
            {
                return;
            }

            if (!Input.GetKeyDown(_transitionKeyCode))
            {
                return;
            }

            RunSceneDirector.ServerInstance.ServerBeginTransition(_targetScene);
        }

#endif
    }
}
