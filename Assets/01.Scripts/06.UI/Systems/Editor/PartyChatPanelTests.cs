// Third party
using NUnit.Framework;
using System.Reflection;

// Unity
using TMPro;
using UnityEditor;
using UnityEngine;

// Project
using Geumeum.Common;

namespace Geumeum.UI.Editor
{
    public sealed class PartyChatPanelTests
    {
        private GameObject _root;
        private PartyChatPanel _panel;

        [SetUp]
        public void SetUp()
        {
            LocalChatLog.Clear();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/02.Prefabs/UI/Resources/UI/PartyChatPanel.prefab");
            Assert.IsNotNull(prefab);
            _root = Object.Instantiate(prefab);
            _panel = _root.GetComponent<PartyChatPanel>();
            InvokeLifecycle("Awake");
            InvokeLifecycle("OnEnable");
        }

        [TearDown]
        public void TearDown()
        {
            if (_panel != null) { InvokeLifecycle("OnDisable"); }
            Object.DestroyImmediate(_root);
            LocalChatLog.Clear();
        }

        [Test]
        public void WindowAnchorsToBottomRightAndUsesProductionFont()
        {
            RectTransform window = _root.transform.Find("ChatWindow") as RectTransform;
            Assert.AreEqual(new Vector2(1f, 0f), window.anchorMin);
            Assert.AreEqual(window.anchorMin, window.anchorMax);
            Assert.AreEqual(new Vector2(1f, 0f), window.pivot);
            Assert.Less(window.anchoredPosition.x, 0f);
            Assert.Greater(window.anchoredPosition.y, 0f);
            Assert.AreSame(TMP_Settings.defaultFontAsset, _panel.InputField.textComponent.font);
        }

        [Test]
        public void TabsSeparatePartyAndSystemAndKeepContentPlain()
        {
            LocalChatLog.AddSystem("신력이 부족합니다");
            LocalChatLog.AddParty("민성", "<b>같이 가요</b>");
            Assert.AreEqual(2, _panel.VisibleMessageCount);
            _panel.SelectTab(2);
            Assert.AreEqual(1, _panel.VisibleMessageCount);
            TMP_Text row = _root.transform.Find("ChatWindow/Body/History/Viewport/Messages/Message")
                .GetComponent<TMP_Text>();
            Assert.AreEqual("[시스템] 신력이 부족합니다", row.text);
            Assert.IsFalse(row.richText);
            _panel.SelectTab(1);
            Assert.AreEqual(1, _panel.VisibleMessageCount);
            Assert.That(row.text, Does.StartWith("[파티] 민성: "));
        }

        [Test]
        public void TypingOwnsGameplayInputAndClosingRestoresIt()
        {
            Assert.IsTrue(LocalGameplayInputGate.AllowsGameplayInput);
            _panel.FocusInput();
            Assert.IsTrue(_panel.IsTyping);
            Assert.IsFalse(LocalGameplayInputGate.AllowsGameplayInput);
            _panel.CancelInput();
            Assert.IsFalse(_panel.IsTyping);
            Assert.IsTrue(LocalGameplayInputGate.AllowsGameplayInput);
        }

        [Test]
        public void ChatDoesNotTakeInputFromAnExistingModal()
        {
            using (LocalGameplayInputToken modal = LocalGameplayInputGate.Acquire())
            {
                _panel.FocusInput();
                Assert.IsFalse(_panel.IsTyping);
                Assert.AreEqual(1, LocalGameplayInputGate.ActiveTokenCount);
                _panel.CancelInput();
                Assert.IsFalse(LocalGameplayInputGate.AllowsGameplayInput);
            }

            Assert.IsTrue(LocalGameplayInputGate.AllowsGameplayInput);
        }

        [Test]
        public void CollapseAndDisableReleaseInputWithoutLosingHistory()
        {
            LocalChatLog.AddSystem("안내");
            _panel.FocusInput();
            _panel.SetCollapsed(true);
            Assert.IsTrue(_panel.IsCollapsed);
            Assert.IsTrue(LocalGameplayInputGate.AllowsGameplayInput);
            _panel.FocusInput();
            Assert.IsFalse(_panel.IsCollapsed);
            Assert.AreEqual(1, _panel.VisibleMessageCount);
            _panel.enabled = false;
            InvokeLifecycle("OnDisable");
            Assert.IsTrue(LocalGameplayInputGate.AllowsGameplayInput);
        }

        private void InvokeLifecycle(string method)
        {
            // 일반 MonoBehaviour는 EditMode에서 수명주기를 자동 실행하지 않는다
            typeof(PartyChatPanel).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_panel, null);
        }
    }
}
