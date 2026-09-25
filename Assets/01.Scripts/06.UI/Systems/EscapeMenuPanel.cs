// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.UI;

// Project
using Geumeum.Common;
using Geumeum.Network;

namespace Geumeum.UI
{
    /// <summary>
    /// ESC로 여닫는 메뉴. 배낭 · 특성 · 도감 · 설정으로 가는 입구만 맡고 내용은 각 창이 맡는다.
    /// ESC 입력은 이 컴포넌트가 단독으로 읽는다 — 창마다 따로 읽으면 같은 프레임에 닫히고 다시 열린다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EscapeMenuPanel : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("구성")]
        [SerializeField] private CanvasGroup   _canvasGroup;
        [SerializeField] private RectTransform _slidePanel;
        [SerializeField] private Button        _inventoryButton;
        [SerializeField] private Button        _talentButton;
        [SerializeField] private Button        _guideButton;
        [SerializeField] private Button        _settingsButton;
        [SerializeField] private SettingsPanel _settingsPanel;

        [Header("슬라이드")]
        [Tooltip("닫힌 상태에서 화면 밖으로 밀어 둘 거리. 위로 빼려면 y를 양수로 준다. " +
            "바 높이에 아래 그라데이션 길이까지 더한 값보다 커야 꼬리가 남지 않는다")]
        [SerializeField] private Vector2 _hiddenOffset = new Vector2(0f, 170f);
        [Tooltip("너무 짧으면 슬라이드가 보이지 않고 그냥 나타난 것처럼 읽힌다")]
        [SerializeField, Min(0f)] private float _slideSeconds = 0.32f;

        // 씬에 꺼진 사본이 함께 있을 수 있어 살아 있는 것만 모아 둔다. Find로 씬을 훑지 않으려는 것이다
        private static readonly List<EscapeMenuPanel> G_Live = new List<EscapeMenuPanel>();

        private LocalGameplayInputToken _inputToken;

        private Vector2 _shownPosition;
        private float _slide;
        private bool  _isOpen;

        public static EscapeMenuPanel Instance { get; private set; }

        /// <summary> 메뉴가 열리고 닫힐 때. 같이 숨겨야 하는 화면이 듣는다 </summary>
        public static event Action<bool> VisibilityChanged;

        public bool IsVisible => _isOpen;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            // 슬라이드의 기준점은 씬에 놓인 위치다. 코드에 좌표를 박으면 스킨을 고칠 때마다 코드가 따라가야 한다
            _shownPosition = (_slidePanel != null)
                ? _slidePanel.anchoredPosition
                : Vector2.zero;

            BindButtons();
            _slide  = 0f;
            _isOpen = false;

            // ApplySlide는 alpha만 다룬다. 여기서 막아 두지 않으면 안 보이는 Backdrop이
            // 화면 전체의 클릭을 계속 먹는다
            if (_canvasGroup != null)
            {
                _canvasGroup.interactable   = false;
                _canvasGroup.blocksRaycasts = false;
            }

            ApplySlide();
        }

        // 같은 씬에 꺼져 있는 사본이 또 있을 수 있다. Awake에서 잡으면 꺼진 쪽이 Instance를 차지한다
        private void OnEnable()
        {
            if (!G_Live.Contains(this))
            {
                G_Live.Add(this);
            }

            Instance = this;
        }

        private void OnDisable()
        {
            ReleaseRegistration();
            SetOpen(false);
            _slide = 0f;
            ApplySlide();
        }

        private void OnDestroy()
        {
            ReleaseInputToken();
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
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                HandleEscape();
            }

            if (_isOpen)
            {
                // CameraRig도 같은 프레임에 ESC로 커서를 뒤집으므로 모달 상태를 매 프레임 다시 세운다
                LocalGameplayInputGate.Maintain(_inputToken);
            }

            AdvanceSlide();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 메뉴를 연다. 이미 다른 모달이 입력을 쥐고 있으면 열지 않는다 </summary>
        public void Open()
        {
            if (_isOpen)
            {
                return;
            }

            RunManager run = RunManager.LocalInstance;
            if ((run != null) && (run.Phase == RunPhase.Result))
            {
                return;
            }

            if (!LocalGameplayInputGate.AllowsGameplayInput)
            {
                return;
            }

            _inputToken = LocalGameplayInputGate.Acquire();
            SetOpen(true);
        }

        public void Close()
        {
            if (!_isOpen)
            {
                return;
            }

            SetOpen(false);
        }

        public void Toggle()
        {
            if (_isOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        /// <summary> 배낭으로 간다. 메뉴는 닫고 입력 소유권은 관리 창에 넘긴다 </summary>
        public void OpenInventory()
        {
            OpenManagement(true);
        }

        public void OpenTalents()
        {
            OpenManagement(false);
        }

        public void OpenSettings()
        {
            if (_settingsPanel != null)
            {
                _settingsPanel.Open();
            }
        }

        /// <summary> 도감은 설정 창의 한 탭이다. 같은 창을 도감 탭으로 연다 </summary>
        public void OpenGuide()
        {
            if (_settingsPanel != null)
            {
                _settingsPanel.OpenGuide();
            }
        }

        /// <summary> ESC 한 번에 하나만 닫힌다. 가장 위에 뜬 것부터 거둔다 </summary>
        private void HandleEscape()
        {
            FunctionalManagementPanel management = FunctionalManagementPanel.Instance;
            if ((management != null) && management.IsVisible)
            {
                management.Close();
                return;
            }

            if ((_settingsPanel != null) && _settingsPanel.IsVisible)
            {
                _settingsPanel.Close();
                return;
            }

            // 펼친 지도도 ESC로 거두는 층이다. 지도 쪽은 메뉴가 있으면 ESC를 처리하지
            // 않고 넘기므로(MinimapExpandToggle.DefersCloseKeyToEscapeMenu),
            // Update 순서와 무관하게 한 번 눌러 지도만 닫히고 메뉴는 열리지 않는다
            MinimapExpandToggle minimap = MinimapExpandToggle.Instance;
            if ((minimap != null) && minimap.IsExpanded)
            {
                minimap.Collapse();
                return;
            }

            if (_isOpen)
            {
                Close();
                return;
            }

            Open();
        }

        private void OpenManagement(bool isInventory)
        {
            FunctionalManagementPanel management = FunctionalManagementPanel.Instance;
            if (management == null)
            {
                return;
            }

            // 관리 창도 같은 토큰을 얻어야 하므로 먼저 놓아준다
            Close();
            if (isInventory)
            {
                management.OpenInventory();
            }
            else
            {
                management.OpenTalents();
            }
        }

        private void SetOpen(bool isOpen)
        {
            bool changed = (_isOpen != isOpen);

            _isOpen = isOpen;
            if (!isOpen)
            {
                ReleaseInputToken();
                if (_settingsPanel != null)
                {
                    _settingsPanel.Close();
                }
            }

            if (_canvasGroup != null)
            {
                _canvasGroup.interactable   = isOpen;
                _canvasGroup.blocksRaycasts = isOpen;
            }

            if (changed)
            {
                VisibilityChanged?.Invoke(isOpen);
            }
        }

        private void AdvanceSlide()
        {
            float target = _isOpen ? 1f : 0f;
            if (_slide == target)
            {
                return;
            }

            float step = (_slideSeconds <= 0f)
                ? 1f
                : (Time.unscaledDeltaTime / _slideSeconds);
            _slide = Mathf.MoveTowards(_slide, target, step);
            ApplySlide();
        }

        private void ApplySlide()
        {
            // smoothstep. 등속으로 밀면 스르륵이 아니라 툭 서는 느낌이 난다
            float eased = _slide * _slide * (3f - (2f * _slide));

            if (_slidePanel != null)
            {
                _slidePanel.anchoredPosition = Vector2.Lerp(
                    _shownPosition + _hiddenOffset, _shownPosition, eased);
            }

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = eased;
            }
        }

        private void BindButtons()
        {
            BindButton(_inventoryButton, OpenInventory);
            BindButton(_talentButton, OpenTalents);
            BindButton(_guideButton, OpenGuide);
            BindButton(_settingsButton, OpenSettings);
        }

        private static void BindButton(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }

        private void ReleaseInputToken()
        {
            _inputToken?.Dispose();
            _inputToken = null;
        }
    }
}
