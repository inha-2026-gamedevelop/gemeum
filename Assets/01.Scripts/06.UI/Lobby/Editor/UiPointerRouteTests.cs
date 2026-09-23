using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Geumeum.UI.Editor
{
    /// <summary>
    /// 검증 도구의 포인터 경로가 우회 호출 없이 거절 조건을 지키는지 본다.
    /// EditMode에서는 레이캐스터가 등록되지 않으므로 실제 적중, 가림은 개발 빌드 E2E(front_* 명령)가 확인한다.
    /// </summary>
    public sealed class UiPointerRouteTests
    {
        private GameObject _canvas;
        private GameObject _eventSystemObject;
        private EventSystem _eventSystem;
        private Button _button;
        private int _clicks;

        [SetUp]
        public void SetUp()
        {
            _canvas = new GameObject("RouteCanvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            _canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            GameObject button = new GameObject("RouteButton", typeof(RectTransform), typeof(Image), typeof(Button));
            button.transform.SetParent(_canvas.transform, false);
            _button = button.GetComponent<Button>();
            _clicks = 0;
            _button.onClick.AddListener(() => ++_clicks);
            _eventSystemObject = new GameObject("RouteEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            _eventSystem = _eventSystemObject.GetComponent<EventSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_eventSystemObject);
            Object.DestroyImmediate(_canvas);
        }

        [Test]
        public void MissingEventSystemRejectsWithoutInvoking()
        {
            Assert.IsFalse(UiPointerRoute.TryClick(_button, null, out string failure));
            StringAssert.Contains("EventSystem", failure);
            Assert.AreEqual(0, _clicks);
        }

        [Test]
        public void DisabledEventSystemRejects()
        {
            // 오프라인 로비에서 RunEventSystem이 꺼져 있던 실제 결함과 같은 상태.
            _eventSystemObject.SetActive(false);
            Assert.IsFalse(UiPointerRoute.TryClick(_button, _eventSystem, out string failure));
            StringAssert.Contains("EventSystem", failure);
            Assert.AreEqual(0, _clicks);
        }

        [Test]
        public void NonInteractableTargetRejects()
        {
            _button.interactable = false;
            Assert.IsFalse(UiPointerRoute.TryClick(_button, _eventSystem, out string failure));
            StringAssert.Contains("비활성", failure);
            Assert.AreEqual(0, _clicks);
        }

        [Test]
        public void HiddenTargetRejects()
        {
            _button.gameObject.SetActive(false);
            Assert.IsFalse(UiPointerRoute.TryProbe(_button, _eventSystem, out string failure));
            StringAssert.Contains("화면에 없다", failure);
        }

        [Test]
        public void TargetWithoutRaycastHitRejectsInsteadOfInvokingDirectly()
        {
            // 레이캐스터가 등록되지 않은 상태에서는 적중이 없으므로 onClick을 직접 부르지 않아야 한다.
            Assert.IsFalse(UiPointerRoute.TryClick(_button, _eventSystem, out string failure));
            Assert.IsNotEmpty(failure);
            Assert.AreEqual(0, _clicks);
        }
    }
}
