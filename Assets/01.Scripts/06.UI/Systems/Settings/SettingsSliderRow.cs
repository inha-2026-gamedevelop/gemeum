// System
using System;

// Unity
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Geumeum.UI
{
    /// <summary>
    /// 설정 한 줄의 슬라이더. 0~1 값을 주고받고 옆 글자에는 백분율 정수를 띄운다.
    /// 슬라이더 범위와 정수 단위는 프리팹에서 정한다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsSliderRow : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        // 0~1 값을 옆 글자에 백분율 정수로 띄울 때 곱한다
        private const float PERCENT = 100f;

        [Header("구성")]
        [SerializeField] private Slider   _slider;
        [SerializeField] private TMP_Text _valueText;

        /// <summary> 사용자가 끌어서 바꿨을 때만. 0~1 </summary>
        public event Action<float> ValueChanged;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if ((_slider == null) || (_valueText == null))
            {
                Debug.LogError("[SettingsSliderRow] 슬라이더나 값 글자 참조가 비어 있다", this);
                return;
            }

            _slider.onValueChanged.AddListener(OnSliderChanged);
        }

        private void OnDestroy()
        {
            if (_slider != null)
            {
                _slider.onValueChanged.RemoveListener(OnSliderChanged);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 저장된 값을 보여 준다. 이벤트를 다시 내지 않는다 </summary>
        public void SetNormalized(float value)
        {
            if (_slider == null)
            {
                return;
            }

            _slider.SetValueWithoutNotify(Mathf.Lerp(_slider.minValue, _slider.maxValue, Mathf.Clamp01(value)));
            RefreshText();
        }

        private void OnSliderChanged(float _)
        {
            RefreshText();
            ValueChanged?.Invoke(Normalized());
        }

        private float Normalized()
        {
            return Mathf.InverseLerp(_slider.minValue, _slider.maxValue, _slider.value);
        }

        private void RefreshText()
        {
            if (_valueText != null)
            {
                _valueText.text = Mathf.RoundToInt(Normalized() * PERCENT).ToString();
            }
        }
    }
}
