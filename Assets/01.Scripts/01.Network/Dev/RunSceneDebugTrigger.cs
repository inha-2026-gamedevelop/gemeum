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
        *                Fields
        ****************************************/

        private const float LABEL_MARGIN = 10f;
        private const float LABEL_WIDTH  = 460f;
        private const float LABEL_HEIGHT = 22f;
        private const float LABEL_TOP    = 32f;

        [Header("키와 대상 씬")]
        [SerializeField] private KeyCode    _transitionKeyCode = KeyCode.F5;
        [SerializeField] private RunSceneId _targetScene       = RunSceneId.Boss;

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
                    + "   [" + _transitionKeyCode + "] " + _targetScene + "로 전환";
            }

            GUI.Label(new Rect(LABEL_MARGIN, LABEL_TOP, LABEL_WIDTH, LABEL_HEIGHT), text, _labelStyle);
        }

#endif
    }
}
