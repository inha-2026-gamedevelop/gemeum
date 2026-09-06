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

        // 대상을 하나만 두는 이유는 Title, Lobby, Field, BossEntrance, Result 가 아직 빈 껍데기라
        // 그리로 보내면 아무것도 없는 곳에 갇히기 때문이다. 씬이 채워지면 늘린다
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
            // 전환은 서버가 소유한다. 클라이언트가 눌러도 아무 일이 없어야 한다
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
