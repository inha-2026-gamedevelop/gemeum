// Unity
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Geumeum.UI
{
    /// <summary>
    /// 먹 버튼의 글자를 버튼 상태에 맞춰 흐리게 한다
    ///
    /// Button의 색 반응은 테두리 그림 하나에만 걸린다. 글자가 그대로 밝으면 누를 수 없는 버튼이
    /// 누를 수 있는 것처럼 읽힌다. Button은 interactable 변화를 알리지 않으므로 값만 비교하고
    /// 바뀐 때만 글자 알파를 바꾼다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InkButtonLabel : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private Button   _button;
        [SerializeField] private TMP_Text _label;

        [Tooltip("누를 수 없을 때 글자 알파 배수")]
        [SerializeField] private float _disabledAlpha = 0.4f;

        private bool _isShownInteractable;
        private bool _hasShown;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            _hasShown = false;
        }

        private void LateUpdate()
        {
            if ((_button == null) || (_label == null))
            {
                return;
            }

            bool interactable = _button.IsInteractable();
            if (_hasShown && (interactable == _isShownInteractable))
            {
                return;
            }

            _hasShown = true;
            _isShownInteractable = interactable;
            _label.alpha = interactable ? 1f : _disabledAlpha;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 스타일 도구가 붙일 때 대상을 물린다 </summary>
        public void Bind(Button button, TMP_Text label)
        {
            _button = button;
            _label = label;
        }
    }
}
