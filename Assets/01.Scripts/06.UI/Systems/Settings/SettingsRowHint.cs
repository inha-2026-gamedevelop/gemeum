// Unity
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Geumeum.UI
{
    /// <summary>
    /// 설정 한 줄에 마우스를 올리면 줄에 등불 번짐을 켜고, 설명 칸이 있으면 이 줄의 설명을 띄운다.
    /// 설명 문구는 줄마다 프리팹에 적는다 - 값 목록과 저장은 SettingsOptionsBinder 몫이라 여기서는 모른다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsRowHint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("구성")]
        [Tooltip("줄 이름 글자. 설명 칸 제목으로도 쓴다")]
        [SerializeField] private TMP_Text   _label;
        [Tooltip("마우스가 올라와 있는 동안만 켜는 번짐과 위아래 선")]
        [SerializeField] private GameObject _hoverMark;
        [Tooltip("비워 두면 번짐만 켠다. 시스템 탭처럼 설명 칸이 없는 줄이 그렇다")]
        [SerializeField] private SettingsHintPanel _panel;

        [Header("설명")]
        [SerializeField, TextArea(2, 5)] private string _description;

        [Header("글자색")]
        [SerializeField] private Color _hoverLabelColor = new Color(0.953f, 0.918f, 0.839f, 1f);

        private Color _restLabelColor;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if ((_label == null) || (_hoverMark == null))
            {
                Debug.LogError("[SettingsRowHint] 줄 이름이나 번짐 참조가 비어 있다", this);
                enabled = false;
                return;
            }

            _restLabelColor = _label.color;
            SetHovered(false);
        }

        // 탭이나 분류를 바꿔 줄이 꺼지면 빠져나가는 신호가 오지 않는다. 다시 켜졌을 때 번짐이 남지 않게 한다
        private void OnDisable()
        {
            SetHovered(false);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 복제해 만든 줄의 이름과 설명을 바꾼다. 색은 Awake에서 잡으므로 이름 글자색은 건드리지 않는다 </summary>
        public void SetContent(string label, string description)
        {
            if (_label != null)
            {
                _label.text = label;
            }

            _description = description;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            SetHovered(true);

            if (_panel != null)
            {
                _panel.Show(_label.text, _description);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            SetHovered(false);
        }

        private void SetHovered(bool isHovered)
        {
            // Awake에서 참조가 비어 꺼진 경우에도 OnDisable은 불린다
            if ((_label == null) || (_hoverMark == null))
            {
                return;
            }

            _hoverMark.SetActive(isHovered);
            _label.color = isHovered ? _hoverLabelColor : _restLabelColor;
        }
    }
}
