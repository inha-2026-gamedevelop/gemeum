// System
using System;

// Unity
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Geumeum.UI
{
    /// <summary>
    /// 설정 한 줄의 ◀ 값 ▶ 선택기. 값 목록은 모르고 몇 칸 움직였는지만 알린다.
    /// 무엇을 고를 수 있는지와 저장은 SettingsOptionsBinder가 맡는다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsStepperRow : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("구성")]
        [SerializeField] private TMP_Text _valueText;
        [SerializeField] private Button   _previousButton;
        [SerializeField] private Button   _nextButton;

        /// <summary> 왼쪽이면 -1, 오른쪽이면 +1 </summary>
        public event Action<int> Stepped;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if ((_valueText == null) || (_previousButton == null) || (_nextButton == null))
            {
                Debug.LogError("[SettingsStepperRow] 값 글자나 화살표 버튼 참조가 비어 있다", this);
                return;
            }

            _previousButton.onClick.AddListener(StepPrevious);
            _nextButton.onClick.AddListener(StepNext);
        }

        private void OnDestroy()
        {
            if (_previousButton != null)
            {
                _previousButton.onClick.RemoveListener(StepPrevious);
            }

            if (_nextButton != null)
            {
                _nextButton.onClick.RemoveListener(StepNext);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void SetValueText(string text)
        {
            if (_valueText != null)
            {
                _valueText.text = text;
            }
        }

        /// <summary> 고를 값이 하나뿐일 때처럼 움직일 수 없으면 화살표를 막는다 </summary>
        public void SetInteractable(bool isInteractable)
        {
            if (_previousButton != null)
            {
                _previousButton.interactable = isInteractable;
            }

            if (_nextButton != null)
            {
                _nextButton.interactable = isInteractable;
            }
        }

        private void StepPrevious()
        {
            Stepped?.Invoke(-1);
        }

        private void StepNext()
        {
            Stepped?.Invoke(1);
        }
    }
}
