// Unity
using UnityEngine;
using UnityEngine.UI;

namespace Geumeum.UI
{
    /// <summary>
    /// 설정 창의 빈 골격. 여닫기와 자리만 잡아 두고 실제 항목은 아직 없다.
    /// 항목을 채울 때 이 파일에 필드를 늘리고 ESC 흐름은 EscapeMenuPanel 쪽을 그대로 쓴다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsPanel : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("구성")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Button      _closeButton;

        private bool _isVisible;

        public bool IsVisible => _isVisible;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(Close);
                _closeButton.onClick.AddListener(Close);
            }

            SetVisible(false);
        }

        private void OnDisable()
        {
            SetVisible(false);
        }

        /****************************************
        *                Methods
        ****************************************/

        public void Open()
        {
            SetVisible(true);
        }

        public void Close()
        {
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            _isVisible = visible;
            if (_canvasGroup == null)
            {
                return;
            }

            _canvasGroup.alpha          = visible ? 1f : 0f;
            _canvasGroup.interactable   = visible;
            _canvasGroup.blocksRaycasts = visible;
        }
    }
}
