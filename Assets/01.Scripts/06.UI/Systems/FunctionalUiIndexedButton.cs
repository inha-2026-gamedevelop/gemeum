// System
using System;

// Unity
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Geumeum.UI
{
    /// <summary> 반복 생성한 기능 버튼의 번호와 문구를 패널에 전달한다 </summary>
    public sealed class FunctionalUiIndexedButton : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private Button   _button;
        [SerializeField] private TMP_Text _label;
        [SerializeField] private int      _index;

        public int Index => _index;
        public void SetIndex(int index) { _index = index; }
        public TMP_Text Label => _label;

        public bool Interactable
        {
            get => (_button != null) && _button.interactable;
            set
            {
                if (_button != null)
                {
                    _button.interactable = value;
                }
            }
        }

        public event Action<int> Clicked;

        /// <summary>
        /// 포인터나 키보드 선택이 이 버튼에 머무는지. 두 번째 인자가 들어옴/나감이다
        ///
        /// Button.interactable이 false여도 포인터 이벤트는 그대로 전달되므로
        /// 아직 못 고르는 잠김 항목도 설명을 띄울 수 있다. 반대로 키보드 선택은
        /// 비활성 Selectable에 가지 않으므로 select 경로는 고를 수 있는 항목만 온다
        /// </summary>
        public event Action<int, bool> HoverChanged;

        /// <summary>
        /// 이 칸을 끌기 시작했다. 배낭에서 양도 상자로 장비를 옮길 때만 쓴다
        /// 무엇을 끌 수 있는지는 패널이 정한다 - 버튼은 번호와 포인터만 넘긴다
        /// </summary>
        public event Action<int, PointerEventData> DragBegan;

        public event Action<int, PointerEventData> Dragged;

        public event Action<int, PointerEventData> DragEnded;

        public void OnBeginDrag(PointerEventData eventData) { DragBegan?.Invoke(_index, eventData); }
        public void OnDrag(PointerEventData eventData)      { Dragged?.Invoke(_index, eventData); }
        public void OnEndDrag(PointerEventData eventData)   { DragEnded?.Invoke(_index, eventData); }

        public void OnPointerEnter(PointerEventData eventData) { RaiseHover(true); }
        public void OnPointerExit(PointerEventData eventData) { RaiseHover(false); }
        public void OnSelect(BaseEventData eventData) { RaiseHover(true); }
        public void OnDeselect(BaseEventData eventData) { RaiseHover(false); }

        private void RaiseHover(bool entered)
        {
            HoverChanged?.Invoke(_index, entered);
        }

        public void SetLabel(string value)
        {
            if (_label != null)
            {
                _label.text = value;
            }
        }

        public void SetInteractable(bool interactable)
        {
            Interactable = interactable;
        }

        private void Awake()
        {
            if (_button == null)
            {
                _button = GetComponent<Button>();
            }

            if (_button != null)
            {
                _button.onClick.AddListener(HandleClick);
            }
        }

        private void OnDestroy()
        {
            if (_button != null)
            {
                _button.onClick.RemoveListener(HandleClick);
            }
        }

        private void HandleClick()
        {
            Clicked?.Invoke(_index);
        }
    }
}
