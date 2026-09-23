using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Geumeum.UI
{
    /// <summary>
    /// 검증 도구가 실제 입력과 같은 EventSystem 경로로 UI를 누르게 한다.
    /// 활성 EventSystem, 레이캐스트 적중, 가림 여부를 모두 통과해야 클릭을 보낸다.
    /// </summary>
    public static class UiPointerRoute
    {
        private static readonly List<RaycastResult> G_Hits = new List<RaycastResult>();

        /// <summary>대상 중심에 포인터가 닿는지만 확인한다. 클릭은 보내지 않는다.</summary>
        public static bool TryProbe(Selectable target, out string failure)
        {
            return TryProbe(target, EventSystem.current, out failure);
        }

        public static bool TryProbe(Selectable target, EventSystem system, out string failure)
        {
            return TryResolve(target, system, out _, out _, out failure);
        }

        /// <summary>레이캐스트가 대상에 닿으면 down/up/click을 EventSystem으로 보낸다.</summary>
        public static bool TryClick(Selectable target, out string failure)
        {
            return TryClick(target, EventSystem.current, out failure);
        }

        public static bool TryClick(Selectable target, EventSystem system, out string failure)
        {
            if (!TryResolve(target, system, out PointerEventData data, out GameObject handler, out failure))
            {
                return false;
            }
            ExecuteEvents.Execute(handler, data, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(handler, data, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(handler, data, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(handler, data, ExecuteEvents.pointerClickHandler);
            return true;
        }

        /// <summary>대상을 선택한 뒤 Enter와 같은 submit 이벤트를 보낸다.</summary>
        public static bool TrySubmit(Selectable target, out string failure)
        {
            EventSystem system = EventSystem.current;
            if (!TryResolve(target, system, out _, out GameObject handler, out failure))
            {
                return false;
            }
            system.SetSelectedGameObject(handler);
            ExecuteEvents.Execute(system.currentSelectedGameObject, new BaseEventData(system),
                ExecuteEvents.submitHandler);
            return true;
        }

        /// <summary>입력칸을 포인터로 누른 뒤 값을 넣는다. 키 입력 대신 결과 문자열만 흉내 낸다.</summary>
        public static bool TryType(TMP_InputField input, string value, out string failure)
        {
            if (!TryClick(input, out failure))
            {
                return false;
            }
            input.text = value ?? string.Empty;
            input.onEndEdit.Invoke(input.text);
            return true;
        }

        private static bool TryResolve(Selectable target, EventSystem system, out PointerEventData data,
            out GameObject handler, out string failure)
        {
            data = null;
            handler = null;
            if ((system == null) || (!system.isActiveAndEnabled))
            {
                failure = "활성 EventSystem이 없다";
                return false;
            }
            if ((target == null) || (!target.isActiveAndEnabled))
            {
                failure = "대상이 화면에 없다";
                return false;
            }
            if (!target.IsInteractable())
            {
                failure = "대상이 비활성 상태다: " + target.name;
                return false;
            }

            RectTransform rect = target.transform as RectTransform;
            Canvas canvas = target.GetComponentInParent<Canvas>();
            if ((rect == null) || (canvas == null))
            {
                failure = "대상이 Canvas 아래에 없다: " + target.name;
                return false;
            }
            Canvas root = canvas.rootCanvas;
            Camera camera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera,
                rect.TransformPoint(rect.rect.center));

            data = new PointerEventData(system)
            {
                position = screen,
                pressPosition = screen,
                button = PointerEventData.InputButton.Left,
                clickCount = 1,
                clickTime = Time.unscaledTime,
                eligibleForClick = true,
            };
            G_Hits.Clear();
            system.RaycastAll(data, G_Hits);
            if (G_Hits.Count == 0)
            {
                failure = $"포인터가 아무 UI에도 닿지 않았다: {target.name} ({screen.x:0},{screen.y:0})";
                return false;
            }

            RaycastResult top = G_Hits[0];
            handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(top.gameObject);
            if (handler != target.gameObject)
            {
                failure = "다른 UI가 포인터를 가로챘다: " + PathOf(top.gameObject.transform);
                handler = null;
                return false;
            }
            data.pointerCurrentRaycast = top;
            data.pointerPressRaycast = top;
            data.pointerEnter = top.gameObject;
            data.rawPointerPress = top.gameObject;
            data.pointerPress = handler;
            failure = string.Empty;
            return true;
        }

        private static string PathOf(Transform transform)
        {
            string path = transform.name;
            for (Transform parent = transform.parent; parent != null; parent = parent.parent)
            {
                path = parent.name + "/" + path;
            }
            return path;
        }
    }
}
