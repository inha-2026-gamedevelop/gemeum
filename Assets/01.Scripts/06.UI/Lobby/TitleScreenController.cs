// Unity
using UnityEngine;

namespace Geumeum.UI
{
    /// <summary>
    /// 시작화면의 안내 문구와 메뉴 사이 전환.
    /// 아무 키나 누르면 안내를 감추고 메뉴를 띄운다
    /// </summary>
    public class TitleScreenController : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("패널")]
        [SerializeField] private GameObject _pressAnyKeyPanel;
        [SerializeField] private GameObject _menuPanel;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_pressAnyKeyPanel == null || _menuPanel == null)
            {
                Debug.LogError("[TitleScreenController] 패널이 연결되지 않았다", this);
                enabled = false;
                return;
            }

            ShowPressAnyKey();
        }

        private void Update()
        {
            if (Input.anyKeyDown == false)
            {
                return;
            }

            ShowMenu();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void ShowPressAnyKey()
        {
            _pressAnyKeyPanel.SetActive(true);
            _menuPanel.SetActive(false);
        }

        // 전환은 한 번뿐이라 이후에는 매 프레임 입력을 볼 이유가 없다
        private void ShowMenu()
        {
            _pressAnyKeyPanel.SetActive(false);
            _menuPanel.SetActive(true);

            enabled = false;
        }
    }
}
