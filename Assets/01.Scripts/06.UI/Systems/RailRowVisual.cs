// Unity
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Geumeum.UI
{
    /// <summary> 레일 한 줄의 활성 표시를 맡는다. 박스와 아이콘과 글자와 밑줄을 함께 바꾼다 </summary>
    [DisallowMultipleComponent]
    public sealed class RailRowVisual : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("구성")]
        [SerializeField] private RectTransform _box;
        [SerializeField] private Image         _boxImage;
        [SerializeField] private Image         _iconImage;
        [SerializeField] private TMP_Text      _label;
        [SerializeField] private GameObject    _line;

        [Header("박스 스프라이트")]
        [SerializeField] private Sprite _activeBox;
        [SerializeField] private Sprite _normalBox;

        [Header("박스 크기")]
        [Tooltip("활성 테두리는 모서리 장식이 바깥으로 뻗어 있어 같은 크기로 두면 안쪽 네모가 작아 보인다")]
        [SerializeField] private float _activeSize = 70f;
        [SerializeField] private float _normalSize = 60f;

        [Header("색")]
        [SerializeField] private Color _activeColor      = new Color(0.741f, 0.216f, 0.169f, 1f);
        [SerializeField] private Color _normalBoxColor   = new Color(0.86f, 0.80f, 0.70f, 1f);
        [SerializeField] private Color _normalLabelColor = new Color(0.941f, 0.886f, 0.784f, 1f);

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이 줄이 열려 있는 페이지인지 알려 준다 </summary>
        public void SetActiveRow(bool isActive)
        {
            // 문구도 같은 탭 버튼의 클릭 영역이다
            if (_label != null) { _label.raycastTarget = true; }
            if (_boxImage != null)
            {
                _boxImage.sprite = isActive ? _activeBox : _normalBox;
                _boxImage.color  = isActive ? _activeColor : _normalBoxColor;
            }

            if (_box != null)
            {
                float size = isActive ? _activeSize : _normalSize;
                _box.sizeDelta = new Vector2(size, size);
            }

            if (_iconImage != null)
            {
                _iconImage.color = isActive ? _activeColor : _normalBoxColor;
            }

            if (_label != null)
            {
                _label.color = isActive ? _activeColor : _normalLabelColor;
            }

            if (_line != null)
            {
                _line.SetActive(isActive);
            }
        }
    }
}
