// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Geumeum.UI
{
    /// <summary>
    /// 도감 탭의 글. 분류 → 소제목 → 항목(제목 · 본문) 세 단계다.
    /// 화면 배치는 프리팹이, 무엇을 보여 줄지는 이 에셋이 정한다 — 문구를 고칠 때 코드를 건드리지 않는다.
    /// 판정 값의 원본이 아니다. 수치를 적었다면 밸런스가 바뀔 때 같이 고쳐야 한다
    /// </summary>
    [CreateAssetMenu(fileName = "GuideCatalog", menuName = "Geumeum/UI/Guide Catalog")]
    public sealed class GuideCatalogSO : ScriptableObject
    {
        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        public sealed class Entry
        {
            public string Title;
            [TextArea(3, 12)] public string Body;
        }

        [Serializable]
        public sealed class Section
        {
            [Tooltip("항목 목록 위에 붙는 소제목. 비우면 소제목 줄을 만들지 않는다")]
            public string  Heading;
            public Entry[] Entries = Array.Empty<Entry>();
        }

        [Serializable]
        public sealed class Category
        {
            public string    Name;
            public Section[] Sections = Array.Empty<Section>();
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private Category[] _categories = Array.Empty<Category>();

        public IReadOnlyList<Category> Categories => _categories;
    }
}
