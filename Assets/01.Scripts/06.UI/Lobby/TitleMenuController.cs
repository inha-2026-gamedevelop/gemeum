// Unity
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Geumeum.UI
{
    /// <summary>
    /// 시작화면 메뉴의 조작과 각 항목의 동작.
    /// 방향키 이동과 Enter 확인은 EventSystem이 처리하고, 여기서는 확인키 G만 더한다.
    /// 각 항목의 동작은 Button의 OnClick에서 연결한다
    /// </summary>
    public class TitleMenuController : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const KeyCode KEY_CONFIRM = KeyCode.G;

        [Header("메뉴 항목")]
        [SerializeField] private Button _quitButton;

        private int _enabledFrame;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_quitButton == null)
            {
                Debug.LogError("[TitleMenuController] 종료 버튼이 연결되지 않았다", this);
                return;
            }

            _quitButton.onClick.AddListener(QuitGame);
        }

        private void OnEnable()
        {
            _enabledFrame = Time.frameCount;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KEY_CONFIRM) == false)
            {
                return;
            }

            // 메뉴를 여는 키가 G였다면 같은 프레임에 첫 항목까지 눌려버린다
            if (Time.frameCount == _enabledFrame)
            {
                return;
            }

            SubmitSelected();
        }

        /****************************************
        *                Methods
        ****************************************/

        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // Enter와 같은 경로로 보내야 항목마다 확인키를 따로 잇지 않아도 된다
        private void SubmitSelected()
        {
            if (EventSystem.current == null)
            {
                return;
            }

            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected == null)
            {
                return;
            }

            ExecuteEvents.Execute(selected, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
        }
    }
}
