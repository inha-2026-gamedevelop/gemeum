// System
using System;

// Unity
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Project
using Geumeum.Common;

namespace Geumeum.UI
{
    /// <summary>
    /// 설정 창. 위에 가로 탭(도감 · 옵션 · 시스템)을 두고 Q · E로 넘긴다. 옵션 탭은 왼쪽 분류를 골라 오른쪽 줄을 바꾼다.
    /// ESC로 닫는 흐름은 EscapeMenuPanel 쪽을 그대로 쓴다. 이 창은 ESC를 읽지 않는다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsPanel : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 탭 하나 또는 분류 하나. 누르는 버튼과 활성 표시와 켜고 끌 페이지를 묶는다 </summary>
        [Serializable]
        private struct PageEntry
        {
            public Button     Button;
            public TMP_Text   Label;
            [Tooltip("활성일 때만 켜는 밑줄이나 테두리. 비워도 된다")]
            public GameObject ActiveMark;
            public GameObject Page;
        }

        [Header("구성")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Button      _closeButton;

        [Header("상단 탭")]
        [SerializeField] private PageEntry[] _tabs = Array.Empty<PageEntry>();
        [SerializeField] private KeyCode     _previousTabKey = KeyCode.Q;
        [SerializeField] private KeyCode     _nextTabKey     = KeyCode.E;

        [Header("옵션 분류")]
        [SerializeField] private PageEntry[] _categories = Array.Empty<PageEntry>();

        [Header("내용")]
        [Tooltip("탭 페이지와 같은 오브젝트에 붙어 있어야 한다. 어느 탭으로 열지 이것으로 찾는다")]
        [SerializeField] private GuidePage             _guide;
        [SerializeField] private SettingsOptionsBinder _options;
        [SerializeField] private SettingsSystemPage    _system;
        [Tooltip("옵션 탭 오른쪽 설명 칸. 분류를 바꾸면 안내 문구로 되돌린다")]
        [SerializeField] private SettingsHintPanel     _hint;

        [Header("활성 표시")]
        [SerializeField] private Color _activeLabelColor   = new Color(0.941f, 0.886f, 0.784f, 1f);
        [SerializeField] private Color _inactiveLabelColor = new Color(0.55f, 0.52f, 0.48f, 1f);
        [Tooltip("켜진 탭만 크게 쓴다. 색만 바꾸면 어느 탭에 있는지 한눈에 들어오지 않는다")]
        [SerializeField, Min(1f)] private float _activeTabFontSize   = 34f;
        [SerializeField, Min(1f)] private float _inactiveTabFontSize = 22f;

        private bool _isVisible;
        private int  _tabIndex;

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

            BindEntries(_tabs, SelectTab);
            BindEntries(_categories, SelectCategory);

            SetVisible(false);
        }

        private void OnDisable()
        {
            SetVisible(false);
        }

        private void Update()
        {
            if (!_isVisible)
            {
                return;
            }

            if (Input.GetKeyDown(_previousTabKey))
            {
                SelectTab(Wrap(_tabIndex - 1, _tabs.Length));
            }
            else if (Input.GetKeyDown(_nextTabKey))
            {
                SelectTab(Wrap(_tabIndex + 1, _tabs.Length));
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 옵션 탭으로 연다. ESC 메뉴의 설정 입구다 </summary>
        public void Open()
        {
            OpenAt((_options != null) ? _options.gameObject : null);
        }

        /// <summary> 도감 탭으로 연다. ESC 메뉴의 도감 입구다 </summary>
        public void OpenGuide()
        {
            OpenAt((_guide != null) ? _guide.gameObject : null);
        }

        /// <summary>
        /// 고른 페이지의 탭 · 첫 옵션 분류로 연다. 값은 열 때마다 저장본에서 다시 읽는다.
        /// 탭 순서를 인덱스로 박지 않고 페이지로 찾는다 — 프리팹에서 탭을 옮겨도 입구가 엇갈리지 않는다
        /// </summary>
        private void OpenAt(GameObject page)
        {
            if (_options != null)
            {
                _options.Refresh();
            }

            if (_system != null)
            {
                _system.Refresh();
            }

            SelectTab(FindTab(page));
            SelectCategory(0);
            SetVisible(true);
        }

        private int FindTab(GameObject page)
        {
            for (int i = 0; i < _tabs.Length; ++i)
            {
                if ((page != null) && (_tabs[i].Page == page))
                {
                    return i;
                }
            }

            return 0;
        }

        public void Close()
        {
            // 볼륨 · 표시 설정은 끌 때마다 PlayerPrefs에 들어갔다. 디스크 기록은 닫을 때 한 번만 한다
            if (_isVisible)
            {
                ClientPreferences.Save();
                SoundVolume.Flush();
            }

            SetVisible(false);
        }

        private void SelectTab(int index)
        {
            _tabIndex = ApplySelection(_tabs, index);

            for (int i = 0; i < _tabs.Length; ++i)
            {
                if (_tabs[i].Label != null)
                {
                    _tabs[i].Label.fontSize = (i == _tabIndex) ? _activeTabFontSize : _inactiveTabFontSize;
                }
            }
        }

        private void SelectCategory(int index)
        {
            ApplySelection(_categories, index);

            if (_hint != null)
            {
                _hint.ShowIdle();
            }
        }

        /// <summary> 고른 칸의 페이지만 켜고 글자색과 표시를 바꾼다. 실제로 고른 인덱스를 돌려준다 </summary>
        private int ApplySelection(PageEntry[] entries, int index)
        {
            if (entries.Length == 0)
            {
                return 0;
            }

            int selected = Mathf.Clamp(index, 0, entries.Length - 1);
            for (int i = 0; i < entries.Length; ++i)
            {
                bool isActive = (i == selected);
                PageEntry entry = entries[i];

                if (entry.Page != null)
                {
                    entry.Page.SetActive(isActive);
                }

                if (entry.ActiveMark != null)
                {
                    entry.ActiveMark.SetActive(isActive);
                }

                if (entry.Label != null)
                {
                    entry.Label.color = isActive ? _activeLabelColor : _inactiveLabelColor;
                }
            }

            return selected;
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

        private static void BindEntries(PageEntry[] entries, Action<int> select)
        {
            for (int i = 0; i < entries.Length; ++i)
            {
                if (entries[i].Button == null)
                {
                    continue;
                }

                // 반복 변수를 그대로 잡으면 모든 버튼이 마지막 인덱스를 고른다
                int index = i;
                entries[i].Button.onClick.AddListener(() => select(index));
            }
        }

        private static int Wrap(int index, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            return ((index % count) + count) % count;
        }
    }
}
