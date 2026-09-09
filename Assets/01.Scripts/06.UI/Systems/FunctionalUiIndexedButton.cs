// System
using System;

// Unity
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Geumeum.UI
{
    /// <summary> 반복 생성한 기능 버튼의 번호와 문구를 패널에 전달한다 </summary>
    public sealed class FunctionalUiIndexedButton : MonoBehaviour
    {
        [SerializeField] private Button   _button;
        [SerializeField] private TMP_Text _label;
        [SerializeField] private int      _index;

        public int Index => _index;
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
