// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary> 키로 씬 전환을 건다. 개발 확인용이고 서버에서만 동작한다 </summary>
    public class RunSceneDebugTrigger : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD

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

        private const float LABEL_MARGIN = 10f;
        private const float LABEL_WIDTH  = 460f;
        private const float LABEL_HEIGHT = 22f;
        private const float LABEL_TOP    = 32f;

        [Header("키와 대상 씬")]
        [SerializeField] private KeyCode    _transitionKeyCode = KeyCode.F5;
        [SerializeField] private RunSceneId _targetScene       = RunSceneId.Boss;

        [Header("전체 부활")]
        [SerializeField] private KeyCode _reviveAllKeyCode = KeyCode.F4;

        [Header("화면에 상태를 표시한다")]
        [SerializeField] private bool _showLabel = true;

        private GUIStyle _labelStyle;

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

        private void OnGUI()
        {
            if (!_showLabel)
            {
                return;
            }

            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label);
                _labelStyle.normal.textColor = Color.white;
            }

            string text;
            if (!NetworkServer.active)
            {
                text = "씬 전환은 호스트만 건다";
            }
            else if (RunSceneDirector.ServerInstance == null)
            {
                text = "RunSceneDirector가 없다";
            }
            else
            {
                text = "씬 " + RunSceneDirector.ServerInstance.CurrentScene
                    + " / " + RunSceneDirector.ServerInstance.State
                    + "   [" + _transitionKeyCode + "] " + _targetScene + "로 전환"
                    + "   [" + _reviveAllKeyCode + "] 전체 부활";
            }

            GUI.Label(new Rect(LABEL_MARGIN, LABEL_TOP, LABEL_WIDTH, LABEL_HEIGHT), text, _labelStyle);
        }

#endif
    }
}
