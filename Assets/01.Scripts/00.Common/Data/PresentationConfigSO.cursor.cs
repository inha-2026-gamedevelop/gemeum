// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.Data
{
    /// <summary>
    /// 마우스 커서 모양(부적 화살). 커서가 보일 때(타이틀, 로비, UI를 연 상태)만 쓰인다. 각자 화면의 표현 값이다
    /// 화면 높이에 따라 세 크기 중 하나를 고른다. 하드웨어 커서는 텍스처 크기 그대로 그려지기 때문이다
    /// </summary>
    public partial class PresentationConfigSO
    {
        /****************************************
        *                Types
        ****************************************/

        /// <summary> 커서 한 상태의 크기별 텍스처와 끝점. 끝점은 텍스처 크기에 대한 비율(왼쪽 위 0,0)이다 </summary>
        [Serializable]
        public struct CursorArt
        {
            public Texture2D Small;  // 32px
            public Texture2D Medium; // 48px
            public Texture2D Large;  // 64px

            [Tooltip("클릭 지점. 텍스처 크기에 대한 비율, 왼쪽 위가 (0, 0)")]
            public Vector2 HotspotNormalized;
        }

        /****************************************
        *                Fields
        ****************************************/

        [Header("커서")]
        [Tooltip("끄면 운영체제 기본 커서를 쓴다")]
        [SerializeField] private bool _customCursorEnabled = true;

        [SerializeField] private CursorArt _cursorDefault;

        [Tooltip("누를 수 있는 UI 위")]
        [SerializeField] private CursorArt _cursorHover;

        [Tooltip("마우스 왼쪽 버튼을 누르고 있는 동안")]
        [SerializeField] private CursorArt _cursorPressed;

        [Tooltip("불러오는 중. 끝점은 가운데")]
        [SerializeField] private CursorArt _cursorBusy;

        [Tooltip("화면 높이가 이 값 이상이면 48px 커서를 쓴다")]
        [SerializeField, Min(1)] private int _cursorMediumMinScreenHeight = 900;

        [Tooltip("화면 높이가 이 값 이상이면 64px 커서를 쓴다")]
        [SerializeField, Min(1)] private int _cursorLargeMinScreenHeight = 1600;

        /****************************************
        *              Properties
        ****************************************/

        public bool      CustomCursorEnabled          => _customCursorEnabled;
        public CursorArt CursorDefault                => _cursorDefault;
        public CursorArt CursorHover                  => _cursorHover;
        public CursorArt CursorPressed                => _cursorPressed;
        public CursorArt CursorBusy                   => _cursorBusy;
        public int       CursorMediumMinScreenHeight  => Mathf.Max(1, _cursorMediumMinScreenHeight);
        public int       CursorLargeMinScreenHeight   => Mathf.Max(1, _cursorLargeMinScreenHeight);
    }
}
