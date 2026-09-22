// Unity
using UnityEngine;
using UnityEngine.UI;

namespace Geumeum.UI
{
    /// <summary>
    /// 미니맵을 화면 정중앙의 큰 지도로 펼쳤다 되돌린다
    /// 확대판을 따로 만들지 않고 같은 MinimapPlayerDots의 배치, 표시 반경, 점 크기만 바꾼다 -
    /// 점과 결계를 두 벌 그리면 둘이 어긋날 수 있다
    /// 게임 입력은 막지 않는다. 지도를 펼친 채로 이동과 카메라가 그대로 동작한다
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class MinimapExpandToggle : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        // 펼친 지도는 화면 한가운데 놓는다
        private static readonly Vector2 CENTER_ANCHOR = new Vector2(0.5f, 0.5f);

        [Header("입력")]
        [Tooltip("이 키로 펼치고 접는다")]
        [SerializeField] private KeyCode _toggleKey = KeyCode.M;

        [Tooltip("펼친 상태에서 이 키를 누르면 접는다")]
        [SerializeField] private KeyCode _closeKey = KeyCode.Escape;

        [Header("펼친 모습")]
        [Tooltip("접었을 때 대비 몇 배로 키울지. 180px 미니맵 기준 4배면 720px가 된다")]
        [SerializeField] private float _expandedScale = 4f;

        [Tooltip("펼쳤을 때 가장자리가 몇 m를 뜻하는지. 결계 첫 반경이 한 화면에 들어오도록 잡는다")]
        [SerializeField] private float _expandedRangeMeters = 500f;

        // 1이면 지도와 같이 4배로 커져 뭉개지고, 1/배율이면 접힌 상태와 같은 크기라 큰 지도에서 너무 작다
        [Tooltip("펼쳤을 때 점 크기 배율. 1이면 지도와 같이 커지고, 0.25(=1/배율)면 접힌 상태와 같은 크기가 된다")]
        [SerializeField] private float _expandedDotScale = 0.5f;

        [Tooltip("점과 결계를 그리는 쪽. 비워 두면 같은 오브젝트에서 찾는다")]
        [SerializeField] private MinimapPlayerDots _dots = null;

        // 지도 배경이 아직 없어서 펼치면 지형 위에 선만 뜬다. 읽히게 깔아 두는 임시 판이다
        [Tooltip("펼친 동안에만 깔리는 배경판. 비워 두면 깔지 않는다")]
        [SerializeField] private Graphic _backdrop = null;

        // 접힌 지도에서만 쓰는 장식이다. 펼친 지도까지 따라오면 크기가 안 맞고 시야를 가린다
        [Tooltip("펼친 동안 숨길 것들. 접힌 지도의 테두리 같은 장식을 넣는다")]
        [SerializeField] private Graphic[] _compactOnly = null;

        private RectTransform _rect;
        private Canvas        _canvas;

        // 접힌 모습은 씬에 놓인 값이 정본이다. 숫자를 코드에 다시 적지 않고 시작할 때 받아 둔다
        private Vector2 _compactAnchorMin;
        private Vector2 _compactAnchorMax;
        private Vector2 _compactPivot;
        private Vector2 _compactAnchoredPosition;
        private Vector3 _compactScale;
        private float   _compactRangeMeters;
        private float   _compactDotScale;

        private bool _isExpanded = false;

        public bool IsExpanded => _isExpanded;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _rect = transform as RectTransform;

            if (_dots == null)
            {
                _dots = GetComponent<MinimapPlayerDots>();
            }

            if (_dots == null)
            {
                Debug.LogError("[MinimapExpandToggle] MinimapPlayerDots를 찾지 못했다", this);
                enabled = false;
                return;
            }

            _canvas = GetComponentInParent<Canvas>();

            _compactAnchorMin        = _rect.anchorMin;
            _compactAnchorMax        = _rect.anchorMax;
            _compactPivot            = _rect.pivot;
            _compactAnchoredPosition = _rect.anchoredPosition;
            _compactScale            = _rect.localScale;
            _compactRangeMeters      = _dots.RangeMeters;
            _compactDotScale         = _dots.DotScale;

            // 씬에 켜진 채로 저장돼도 접힌 상태에서는 보이지 않게 맞춘다
            SetBackdropVisible(false);
            SetCompactOnlyVisible(true);
        }

        private void OnDisable()
        {
            // 펼친 채로 꺼지면 다음에 켤 때 지도가 화면을 덮은 상태로 나타난다
            Collapse();
        }

        private void Update()
        {
            // 관리 창이 열리면 HudVisibilityGate가 HUD 캔버스를 끈다
            // 보이지도 않는 지도가 키를 먹으면 창을 닫았을 때 펼쳐진 채로 돌아온다
            if ((_canvas != null) && (!_canvas.enabled))
            {
                Collapse();
                return;
            }

            if (Input.GetKeyDown(_toggleKey))
            {
                Toggle();
                return;
            }

            if (_isExpanded && Input.GetKeyDown(_closeKey))
            {
                Collapse();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void Toggle()
        {
            if (_isExpanded)
            {
                Collapse();
            }
            else
            {
                Expand();
            }
        }

        public void Expand()
        {
            if (_isExpanded || (_dots == null))
            {
                return;
            }

            _isExpanded = true;

            float scale = Mathf.Max(_expandedScale, 0.01f);

            _rect.anchorMin        = CENTER_ANCHOR;
            _rect.anchorMax        = CENTER_ANCHOR;
            _rect.pivot            = CENTER_ANCHOR;
            _rect.anchoredPosition = Vector2.zero;
            _rect.localScale       = Vector3.one * scale;

            _dots.RangeMeters = _expandedRangeMeters;

            // 점은 지도 배율을 그대로 따르지 않는다. 같이 4배가 되면 뭉개진다
            _dots.DotScale = Mathf.Max(_expandedDotScale, 0.01f);

            SetCompactOnlyVisible(false);
            SetBackdropVisible(true);
        }

        public void Collapse()
        {
            if (!_isExpanded)
            {
                return;
            }

            _isExpanded = false;

            _rect.anchorMin        = _compactAnchorMin;
            _rect.anchorMax        = _compactAnchorMax;
            _rect.pivot            = _compactPivot;
            _rect.anchoredPosition = _compactAnchoredPosition;
            _rect.localScale       = _compactScale;

            SetCompactOnlyVisible(true);
            SetBackdropVisible(false);

            if (_dots != null)
            {
                _dots.RangeMeters = _compactRangeMeters;
                _dots.DotScale    = _compactDotScale;
            }
        }

        private void SetBackdropVisible(bool isVisible)
        {
            if (_backdrop != null)
            {
                _backdrop.enabled = isVisible;
            }
        }

        private void SetCompactOnlyVisible(bool isVisible)
        {
            if (_compactOnly == null)
            {
                return;
            }

            for (int i = 0; i < _compactOnly.Length; ++i)
            {
                if (_compactOnly[i] != null)
                {
                    _compactOnly[i].enabled = isVisible;
                }
            }
        }
    }
}
