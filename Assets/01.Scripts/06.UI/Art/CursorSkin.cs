// System
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Data;

namespace Geumeum.UI
{
    /// <summary>
    /// 게임 커서를 부적 화살 모양으로 바꾼다. 누를 수 있는 UI 위면 도깨비불 광채, 누르는 동안은 눌린 모양,
    /// 씬을 불러오는 동안은 방울 모양이다. 커서가 잠겨 숨은 전투 중에는 아무것도 하지 않는다
    /// 씬에 배치하지 않는다. 게임 시작 때 DontDestroyOnLoad 오브젝트 하나를 스스로 만든다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CursorSkin : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string HOST_NAME = "[CursorSkin]";

        private const int MOUSE_BUTTON_LEFT = 0;

        // 마우스가 멈춰 있어도 버튼이 켜지고 꺼질 수 있어 가끔은 다시 검사한다(초)
        private const float IDLE_RAYCAST_INTERVAL = 0.15f;

        /****************************************
        *                Fields
        ****************************************/

        private static CursorSkin _instance;

        // 매 검사마다 새로 만들지 않도록 재사용한다
        private readonly List<RaycastResult> _raycastResults = new List<RaycastResult>();
        private PointerEventData             _pointerData;
        private EventSystem                  _pointerDataOwner;

        private Vector3 _lastMousePosition = new Vector3(float.NaN, float.NaN, 0f);
        private float   _nextIdleRaycastTime;
        private bool    _isOverClickable;

        private bool            _hasApplied;
        private CursorSkinState _appliedState;
        private CursorSkinSize  _appliedSize;

        /****************************************
        *              Unity Event
        ****************************************/

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
#if !UNITY_SERVER
            if (Application.isBatchMode || (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null))
            {
                return;
            }

            if (_instance != null)
            {
                return;
            }

            if ((!GameDB.IsInitialized) || (GameDB.Presentation == null))
            {
                Debug.LogError("[CursorSkin] GameDB.Presentation이 없어 게임 커서를 쓰지 않는다");
                return;
            }

            GameObject host = new GameObject(HOST_NAME);
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<CursorSkin>();
#endif
        }

        private void OnDisable()
        {
            // 운영체제 기본 커서로 돌려 둔다. 다음에 켜지면 처음부터 다시 적용한다
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            _hasApplied = false;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Update()
        {
            PresentationConfigSO config = GameDB.IsInitialized ? GameDB.Presentation : null;
            if ((config == null) || (!config.CustomCursorEnabled))
            {
                ResetToSystemCursor();
                return;
            }

            // 잠긴 커서는 보이지 않는다. UI 검사도 할 필요가 없다
            if ((Cursor.lockState == CursorLockMode.Locked) || (!Cursor.visible))
            {
                _isOverClickable = false;
                return;
            }

            UpdateHover();

            CursorSkinState state = CursorSkinRules.SelectState(IsLoadingScene(), Input.GetMouseButton(MOUSE_BUTTON_LEFT), _isOverClickable);
            CursorSkinSize  size  = CursorSkinRules.SelectSize(Screen.height, config.CursorMediumMinScreenHeight, config.CursorLargeMinScreenHeight);

            if (_hasApplied && (state == _appliedState) && (size == _appliedSize))
            {
                return;
            }

            Apply(config, state, size);
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Apply(PresentationConfigSO config, CursorSkinState state, CursorSkinSize size)
        {
            PresentationConfigSO.CursorArt art = SelectArt(config, state);
            Texture2D texture = SelectTexture(art, size);

            // 이 상태의 그림이 비어 있으면 기본 모양으로 대신한다
            if (texture == null)
            {
                art     = config.CursorDefault;
                texture = SelectTexture(art, size);
            }

            Vector2 hotspot = (texture != null)
                ? CursorSkinRules.HotspotPixels(art.HotspotNormalized, texture.width, texture.height)
                : Vector2.zero;

            Cursor.SetCursor(texture, hotspot, CursorMode.Auto);

            _hasApplied   = true;
            _appliedState = state;
            _appliedSize  = size;
        }

        private void ResetToSystemCursor()
        {
            if (!_hasApplied)
            {
                return;
            }

            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            _hasApplied = false;
        }

        /// <summary> 마우스가 움직였거나 잠시 지났을 때만 UI 레이캐스트를 다시 한다 </summary>
        private void UpdateHover()
        {
            Vector3 mouse = Input.mousePosition;
            float   now   = Time.unscaledTime;

            if ((mouse == _lastMousePosition) && (now < _nextIdleRaycastTime))
            {
                return;
            }

            _lastMousePosition   = mouse;
            _nextIdleRaycastTime = now + IDLE_RAYCAST_INTERVAL;
            _isOverClickable     = IsPointerOverClickable(mouse);
        }

        private bool IsPointerOverClickable(Vector3 mouse)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            if ((_pointerData == null) || (_pointerDataOwner != eventSystem))
            {
                _pointerData      = new PointerEventData(eventSystem);
                _pointerDataOwner = eventSystem;
            }

            _pointerData.position = mouse;
            eventSystem.RaycastAll(_pointerData, _raycastResults);

            if (_raycastResults.Count == 0)
            {
                return false;
            }

            // 맨 위에 맞은 것만 본다. 아래에 버튼이 있어도 위의 패널이 가리면 누를 수 없다
            GameObject top     = _raycastResults[0].gameObject;
            GameObject handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(top);
            _raycastResults.Clear();

            if (handler == null)
            {
                return false;
            }

            return (!handler.TryGetComponent(out Selectable selectable)) || selectable.IsInteractable();
        }

        private static bool IsLoadingScene()
        {
            AsyncOperation loading = NetworkManager.loadingSceneAsync;
            return (loading != null) && (!loading.isDone);
        }

        private static PresentationConfigSO.CursorArt SelectArt(PresentationConfigSO config, CursorSkinState state)
        {
            switch (state)
            {
                case CursorSkinState.Hover:   return config.CursorHover;
                case CursorSkinState.Pressed: return config.CursorPressed;
                case CursorSkinState.Busy:    return config.CursorBusy;
                default:                      return config.CursorDefault;
            }
        }

        private static Texture2D SelectTexture(in PresentationConfigSO.CursorArt art, CursorSkinSize size)
        {
            switch (size)
            {
                case CursorSkinSize.Large:  return (art.Large  != null) ? art.Large  : ((art.Medium != null) ? art.Medium : art.Small);
                case CursorSkinSize.Medium: return (art.Medium != null) ? art.Medium : ((art.Small  != null) ? art.Small  : art.Large);
                default:                    return (art.Small  != null) ? art.Small  : ((art.Medium != null) ? art.Medium : art.Large);
            }
        }
    }
}
