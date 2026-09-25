// System
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.UI;

namespace Geumeum.UI
{
    /// <summary>
    /// 평소에는 지도를 숨기고, 화면 구석에 여는 키만 알려 둔다.
    /// 키를 누르면 화면 정중앙에 큰 지도로 펼치고, 다시 누르면 접어 안내만 남긴다.
    /// 설정에서 누르는 동안 모드를 고르면 키를 떼는 순간 접는다.
    ///
    /// 확대판을 따로 만들지 않고 같은 MinimapPlayerDots의 배치 · 표시 반경 · 점 크기만 바꾼다 —
    /// 점과 결계를 두 벌 그리면 둘이 어긋날 수 있다.
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
        [SerializeField] private KeyCode _toggleKey = KeyCode.Tab;

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

        [Header("접었을 때")]
        // 지도를 통째로 숨기면 여는 방법이 화면 어디에도 남지 않는다. 키 안내만 구석에 띄워 둔다
        [Tooltip("접힌 동안에만 띄울 키 안내. 지도 바깥에 두어야 같이 숨지 않는다. 비워 두면 띄우지 않는다")]
        [SerializeField] private GameObject _closedHint = null;

        private RectTransform _rect;
        private Canvas        _canvas;
        private CanvasGroup   _group;

        // 접힌 모습은 씬에 놓인 값이 정본이다. 숫자를 코드에 다시 적지 않고 시작할 때 받아 둔다
        private Vector2 _compactAnchorMin;
        private Vector2 _compactAnchorMax;
        private Vector2 _compactPivot;
        private Vector2 _compactAnchoredPosition;
        private Vector3 _compactScale;
        private float   _compactRangeMeters;
        private float   _compactDotScale;

        private bool _isExpanded = false;

        // 씬에 꺼진 사본이 함께 있을 수 있어 살아 있는 것만 모아 둔다. Find로 씬을 훑지 않으려는 것이다
        private static readonly List<MinimapExpandToggle> G_Live = new List<MinimapExpandToggle>();

        /// <summary> ESC 우선순위를 정할 때 ESC 메뉴가 펼친 지도를 찾는 창구다 </summary>
        public static MinimapExpandToggle Instance { get; private set; }

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

            // 접힌 동안 지도를 숨기는 수단이다. 프리팹에 없어도 동작하도록 여기서 붙인다
            _group = GetComponent<CanvasGroup>();
            if (_group == null)
            {
                _group = gameObject.AddComponent<CanvasGroup>();
            }

            _compactAnchorMin        = _rect.anchorMin;
            _compactAnchorMax        = _rect.anchorMax;
            _compactPivot            = _rect.pivot;
            _compactAnchoredPosition = _rect.anchoredPosition;
            _compactScale            = _rect.localScale;
            _compactRangeMeters      = _dots.RangeMeters;
            _compactDotScale         = _dots.DotScale;

            // 씬에 켜진 채로 저장돼도 시작은 접힌 상태다
            SetBackdropVisible(false);
            SetMapVisible(false);
            SetHintVisible(true);
        }

        // 꺼진 사본도 Awake는 돌기 때문에 등록은 OnEnable에서 한다
        private void OnEnable()
        {
            if (!G_Live.Contains(this))
            {
                G_Live.Add(this);
            }

            Instance = this;

            SetHintVisible(!_isExpanded);
        }

        private void OnDisable()
        {
            ReleaseRegistration();

            // 펼친 채로 꺼지면 다음에 켤 때 지도가 화면을 덮은 상태로 나타난다
            Collapse();

            // 지도가 없는 화면에 안내만 남으면 눌러도 아무 일도 일어나지 않는다
            SetHintVisible(false);
        }

        private void OnDestroy()
        {
            ReleaseRegistration();
        }

        /// <summary> 쥐고 있던 쪽이 빠지면 아직 살아 있는 사본에게 넘긴다 </summary>
        private void ReleaseRegistration()
        {
            G_Live.Remove(this);
            if (Instance != this)
            {
                return;
            }

            Instance = (G_Live.Count > 0) ? G_Live[G_Live.Count - 1] : null;
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

            // 여는 방식은 사용자 설정이다. 누르는 동안 모드는 뗀 순간(GetKeyUp)이 아니라 지금 눌려 있는지로 정한다 —
            // 뗀 프레임을 한 번 놓치거나 누른 채 창을 벗어나면 지도가 펼쳐진 채로 남는다
            if (ClientPreferences.MinimapOpenMode == MinimapOpenMode.Hold)
            {
                bool isHeld = Input.GetKey(_toggleKey);
                if (isHeld != _isExpanded)
                {
                    if (isHeld)
                    {
                        Expand();
                    }
                    else
                    {
                        Collapse();
                    }

                    return;
                }
            }
            else if (Input.GetKeyDown(_toggleKey))
            {
                Toggle();
                return;
            }

            if (_isExpanded && Input.GetKeyDown(_closeKey) && (!DefersCloseKeyToEscapeMenu()))
            {
                Collapse();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// ESC일 때만 ESC 메뉴에 우선권을 넘긴다. 메뉴가 같은 프레임에 지도를 접고
        /// 그 ESC를 소비하므로, 여기서도 접으면 Update 순서에 따라 지도가 닫히면서
        /// 메뉴까지 열린다.
        ///
        /// 메뉴가 없는 화면(개발 씬 등)에서는 그대로 여기서 접는다. 다른 키를 닫기 키로
        /// 지정한 경우도 메뉴와 겹치지 않으므로 넘기지 않는다
        /// </summary>
        private bool DefersCloseKeyToEscapeMenu()
        {
            return (_closeKey == KeyCode.Escape) && (EscapeMenuPanel.Instance != null);
        }

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

            SetHintVisible(false);
            SetMapVisible(true);
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

            SetBackdropVisible(false);
            SetMapVisible(false);
            SetHintVisible(true);

            if (_dots != null)
            {
                _dots.RangeMeters = _compactRangeMeters;
                _dots.DotScale    = _compactDotScale;
            }
        }

        /// <summary>
        /// 지도를 통째로 보이거나 숨긴다.
        /// GameObject를 끄면 이 컴포넌트까지 같이 꺼져 여는 키를 못 받으므로 알파로 숨기고,
        /// 숨은 동안 점과 결계를 계속 그리지 않도록 MinimapPlayerDots만 따로 끈다
        /// </summary>
        private void SetMapVisible(bool isVisible)
        {
            if (_group != null)
            {
                _group.alpha          = isVisible ? 1f : 0f;
                _group.blocksRaycasts = isVisible;
            }

            if (_dots != null)
            {
                _dots.enabled = isVisible;
            }
        }

        private void SetHintVisible(bool isVisible)
        {
            if ((_closedHint != null) && (_closedHint.activeSelf != isVisible))
            {
                _closedHint.SetActive(isVisible);
            }
        }

        private void SetBackdropVisible(bool isVisible)
        {
            if (_backdrop != null)
            {
                _backdrop.enabled = isVisible;
            }
        }
    }
}
