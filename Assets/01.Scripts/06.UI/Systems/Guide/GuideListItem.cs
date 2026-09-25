// System
using System;

// Unity
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Geumeum.UI
{
    /// <summary>
    /// 도감의 분류 버튼이나 항목 한 줄. 목록은 카탈로그 길이만큼 복제해 쓰므로 누르면 무엇을 할지는 만든 쪽이 넘긴다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GuideListItem : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("구성")]
        [SerializeField] private Button     _button;
        [SerializeField] private TMP_Text   _label;
        [Tooltip("선택된 줄에만 켜는 테두리나 강조")]
        [SerializeField] private GameObject _selectedMark;

        [Header("글자색")]
        [SerializeField] private Color _selectedColor   = new Color(0.941f, 0.886f, 0.784f, 1f);
        [SerializeField] private Color _unselectedColor = new Color(0.72f, 0.68f, 0.62f, 1f);

        private Action _onClick;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if ((_button == null) || (_label == null))
            {
                Debug.LogError("[GuideListItem] 버튼이나 글자 참조가 비어 있다", this);
                return;
            }

            _button.onClick.AddListener(HandleClick);
        }

        private void OnDestroy()
        {
            if (_button != null)
            {
                _button.onClick.RemoveListener(HandleClick);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 글자와 누를 때 할 일을 바꿔 끼운다. 줄을 재사용할 때마다 부른다 </summary>
        public void Bind(string label, Action onClick)
        {
            if (_label != null)
            {
                _label.text = label;
            }

            _onClick = onClick;
        }

        public void SetSelected(bool isSelected)
        {
            if (_selectedMark != null)
            {
                _selectedMark.SetActive(isSelected);
            }

            if (_label != null)
            {
                _label.color = isSelected ? _selectedColor : _unselectedColor;
            }
        }

        private void HandleClick()
        {
            _onClick?.Invoke();
        }
    }
}
