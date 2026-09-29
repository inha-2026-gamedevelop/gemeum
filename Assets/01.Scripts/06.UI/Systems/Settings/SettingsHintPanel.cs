// Unity
using TMPro;
using UnityEngine;

namespace Geumeum.UI
{
    /// <summary>
    /// 옵션 탭 오른쪽 설명 칸. 마우스를 올린 줄의 이름과 설명을 띄운다.
    /// 분류를 바꾸면 안내 문구로 돌아간다 - 다른 분류의 설명이 남아 있으면 지금 보이는 줄의 설명으로 읽힌다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsHintPanel : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("구성")]
        [Tooltip("제목과 그 밑 띠. 안내 문구만 띄울 때는 끈다")]
        [SerializeField] private GameObject _heading;
        [SerializeField] private TMP_Text   _titleText;
        [SerializeField] private TMP_Text   _bodyText;

        [Header("안내")]
        [Tooltip("아무 줄도 가리키지 않을 때 띄우는 문구")]
        [SerializeField, TextArea(2, 4)] private string _idleText = "항목에 마우스를 올리면 설명이 나옵니다.";

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if ((_heading == null) || (_titleText == null) || (_bodyText == null))
            {
                Debug.LogError("[SettingsHintPanel] 제목이나 본문 참조가 비어 있다", this);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void Show(string title, string body)
        {
            bool hasTitle = !string.IsNullOrEmpty(title);
            if (_heading != null)
            {
                _heading.SetActive(hasTitle);
            }

            if (_titleText != null)
            {
                _titleText.text = hasTitle ? title : string.Empty;
            }

            if (_bodyText != null)
            {
                _bodyText.text = FunctionalUiText.KeepWordsTogether(body);
            }
        }

        public void ShowIdle()
        {
            Show(string.Empty, _idleText);
        }
    }
}
