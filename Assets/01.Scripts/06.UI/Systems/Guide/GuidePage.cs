// System
using System.Collections.Generic;

// Unity
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Geumeum.UI
{
    /// <summary>
    /// 도감 탭. 왼쪽 분류를 고르면 가운데에 소제목과 항목을 늘어놓고, 항목을 고르면 오른쪽에 제목과 본문을 띄운다.
    /// 줄은 카탈로그 길이만큼 템플릿을 복제하고, 분류를 바꿀 때는 만든 줄을 다시 쓴다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GuidePage : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("내용")]
        [SerializeField] private GuideCatalogSO _catalog;

        [Header("분류")]
        [SerializeField] private Transform     _categoryRoot;
        [Tooltip("꺼 둔 원본. 분류 수만큼 복제한다")]
        [SerializeField] private GuideListItem _categoryTemplate;

        [Header("항목 목록")]
        [SerializeField] private ScrollRect    _listScroll;
        [SerializeField] private Transform     _listRoot;
        [Tooltip("꺼 둔 원본. 소제목 수만큼 복제한다")]
        [SerializeField] private TMP_Text      _headerTemplate;
        [Tooltip("꺼 둔 원본. 항목 수만큼 복제한다")]
        [SerializeField] private GuideListItem _entryTemplate;

        [Header("설명")]
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _bodyText;

        private readonly List<GuideListItem>        _categoryItems = new List<GuideListItem>();
        private readonly List<TMP_Text>             _headers       = new List<TMP_Text>();
        private readonly List<GuideListItem>        _entryItems    = new List<GuideListItem>();
        private readonly List<GuideCatalogSO.Entry> _shownEntries  = new List<GuideCatalogSO.Entry>();

        private int _categoryIndex = -1;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if ((_catalog == null) || (_categoryRoot == null) || (_categoryTemplate == null) ||
                (_listRoot == null) || (_headerTemplate == null) || (_entryTemplate == null) ||
                (_titleText == null) || (_bodyText == null))
            {
                Debug.LogError("[GuidePage] 카탈로그나 템플릿, 설명 글자 참조가 비어 있다", this);
                enabled = false;
                return;
            }

            _categoryTemplate.gameObject.SetActive(false);
            _headerTemplate.gameObject.SetActive(false);
            _entryTemplate.gameObject.SetActive(false);

            BuildCategories();
        }

        private void OnEnable()
        {
            // 처음 열 때만 첫 분류를 고른다. 다시 열면 보던 항목을 그대로 둔다
            if ((_categoryIndex < 0) && enabled && (_catalog != null) && (_catalog.Categories.Count > 0))
            {
                SelectCategory(0);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void BuildCategories()
        {
            IReadOnlyList<GuideCatalogSO.Category> categories = _catalog.Categories;
            for (int i = 0; i < categories.Count; ++i)
            {
                GuideListItem item = Instantiate(_categoryTemplate, _categoryRoot);
                item.name = "Category_" + i;
                item.gameObject.SetActive(true);

                int index = i;
                item.Bind(categories[i].Name, () => SelectCategory(index));
                _categoryItems.Add(item);
            }
        }

        private void SelectCategory(int index)
        {
            IReadOnlyList<GuideCatalogSO.Category> categories = _catalog.Categories;
            if ((index < 0) || (index >= categories.Count))
            {
                return;
            }

            _categoryIndex = index;
            for (int i = 0; i < _categoryItems.Count; ++i)
            {
                _categoryItems[i].SetSelected(i == index);
            }

            BuildList(categories[index]);

            if (_listScroll != null)
            {
                _listScroll.verticalNormalizedPosition = 1f;
            }

            SelectEntry(0);
        }

        /// <summary> 소제목과 항목을 순서대로 늘어놓는다. 모자란 줄만 새로 만들고 남는 줄은 끈다 </summary>
        private void BuildList(GuideCatalogSO.Category category)
        {
            _shownEntries.Clear();

            int headerCount = 0;
            int sibling     = 0;

            for (int s = 0; s < category.Sections.Length; ++s)
            {
                GuideCatalogSO.Section section = category.Sections[s];
                if (!string.IsNullOrEmpty(section.Heading))
                {
                    TMP_Text header = GetHeader(headerCount++);
                    header.text = section.Heading;
                    header.transform.SetSiblingIndex(sibling++);
                }

                for (int e = 0; e < section.Entries.Length; ++e)
                {
                    int entryIndex = _shownEntries.Count;
                    _shownEntries.Add(section.Entries[e]);

                    GuideListItem item = GetEntryItem(entryIndex);
                    item.Bind(section.Entries[e].Title, () => SelectEntry(entryIndex));
                    item.transform.SetSiblingIndex(sibling++);
                }
            }

            for (int i = headerCount; i < _headers.Count; ++i)
            {
                _headers[i].gameObject.SetActive(false);
            }

            for (int i = _shownEntries.Count; i < _entryItems.Count; ++i)
            {
                _entryItems[i].gameObject.SetActive(false);
            }
        }

        private void SelectEntry(int index)
        {
            for (int i = 0; i < _shownEntries.Count; ++i)
            {
                _entryItems[i].SetSelected(i == index);
            }

            bool hasEntry = (index >= 0) && (index < _shownEntries.Count);
            _titleText.text = hasEntry ? _shownEntries[index].Title : string.Empty;
            _bodyText.text  = hasEntry
                ? FunctionalUiText.KeepWordsTogether(_shownEntries[index].Body)
                : string.Empty;
        }

        private TMP_Text GetHeader(int index)
        {
            if (index >= _headers.Count)
            {
                TMP_Text created = Instantiate(_headerTemplate, _listRoot);
                created.name = "Header_" + index;
                _headers.Add(created);
            }

            TMP_Text header = _headers[index];
            header.gameObject.SetActive(true);
            return header;
        }

        private GuideListItem GetEntryItem(int index)
        {
            if (index >= _entryItems.Count)
            {
                GuideListItem created = Instantiate(_entryTemplate, _listRoot);
                created.name = "Entry_" + index;
                _entryItems.Add(created);
            }

            GuideListItem item = _entryItems[index];
            item.gameObject.SetActive(true);
            return item;
        }
    }
}
