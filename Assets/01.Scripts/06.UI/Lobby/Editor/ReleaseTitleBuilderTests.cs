using System.IO;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Geumeum.UI.Editor
{
    /// <summary>ReleaseTitleBuilder.Build() 실행 결과를 확인한다. 빌더를 먼저 실행해야 한다.</summary>
    public sealed class ReleaseTitleBuilderTests
    {
        [Test]
        public void ReleaseTitleKeepsGuidAndBuildSlotAndDevTitleStaysDevOnly()
        {
            Assert.AreEqual(ReleaseTitleBuilder.TARGET_GUID,
                AssetDatabase.AssetPathToGUID(ReleaseTitleBuilder.TARGET_PATH));
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            Assert.Greater(scenes.Length, 1);
            Assert.AreEqual(ReleaseTitleBuilder.TARGET_PATH, scenes[1].path, "Boot 다음 씬 순서는 바꾸지 않는다");
            Assert.IsTrue(scenes[1].enabled);

            string dev = File.ReadAllText(ReleaseTitleBuilder.SOURCE_PATH);
            StringAssert.Contains("Geumeum.UI.TitleMenuController", dev, "Dev_Title 원본은 개발 메뉴를 유지한다");
            StringAssert.Contains("_lobbySceneName: Dev_Lobby", dev);

            string release = File.ReadAllText(ReleaseTitleBuilder.TARGET_PATH);
            StringAssert.DoesNotContain("Dev_Lobby", release, "출시 Title은 개발 로비로 가지 않는다");
            StringAssert.DoesNotContain("TitleMenuController", release);
        }

        [Test]
        public void ReleaseTitleUsesDevTitleArtWithAccountMenu()
        {
            Scene scene = SceneManager.GetSceneByPath(ReleaseTitleBuilder.TARGET_PATH);
            bool opened = !scene.isLoaded;
            if (opened)
            {
                scene = EditorSceneManager.OpenScene(ReleaseTitleBuilder.TARGET_PATH, OpenSceneMode.Additive);
            }
            try
            {
                GameObject canvas = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    Assert.IsEmpty(root.GetComponentsInChildren<EventSystem>(true),
                        "오프라인 입력은 AccountLobbyUI의 FrontendEventSystem 하나만 쓴다");
                    if (root.name == "Canvas")
                    {
                        canvas = root;
                    }
                }
                Assert.IsNotNull(canvas);
                Transform screen = canvas.transform.Find("TitleScreen");
                Assert.IsNotNull(screen.Find("Background"), "Dev_Title 배경을 그대로 쓴다");
                Assert.AreEqual("그믐", screen.Find("Logo").GetComponent<TMP_Text>().text);
                Assert.IsNull(screen.GetComponent<TitleScreenController>());

                Transform menu = screen.Find("MenuPanel");
                Assert.IsNull(menu.GetComponent<TitleMenuController>());
                Assert.IsNull(menu.Find("SETTINGButton"), "동작 없는 설정 버튼을 남기지 않는다");
                Assert.AreEqual(3, menu.childCount);
                Assert.AreEqual("FindRoomsButton", menu.GetChild(0).name);
                Assert.AreEqual("CreateRoomButton", menu.GetChild(1).name);
                Assert.AreEqual("QuitButton", menu.GetChild(2).name);
                Assert.AreEqual("원정대 찾기", menu.GetChild(0).GetComponent<TMP_Text>().text);
                Assert.AreEqual("원정대 만들기", menu.GetChild(1).GetComponent<TMP_Text>().text);
                foreach (Button button in screen.GetComponentsInChildren<Button>(true))
                {
                    Assert.AreEqual(0, button.onClick.GetPersistentEventCount(), button.name);
                }

                Transform panel = screen.Find("CreateRoomPanel");
                Assert.IsFalse(panel.gameObject.activeSelf);
                Assert.AreEqual(screen.childCount - 1, panel.GetSiblingIndex(), "이름 입력 창이 맨 위에 그려진다");
                TMP_InputField input = panel.Find("RoomNameInput").GetComponent<TMP_InputField>();
                Assert.AreEqual(AccountLobbyPanel.MAX_ROOM_NAME_LENGTH, input.characterLimit);

                Transform prompt = screen.Find("PressAnyKeyPanel");
                Assert.IsTrue(prompt.GetComponent<Image>().raycastTarget, "안내 영역도 포인터로 넘길 수 있다");

                AccountTitleMenu menuController = screen.GetComponent<AccountTitleMenu>();
                Assert.IsNotNull(menuController);
                SerializedObject serialized = new SerializedObject(menuController);
                string[] required =
                {
                    "_pressAnyKeyPanel", "_promptButton", "_menuPanel", "_findButton", "_createButton",
                    "_quitButton", "_createPanel", "_roomNameInput", "_createConfirmButton",
                    "_createCancelButton", "_statusText", "_identityText",
                };
                foreach (string field in required)
                {
                    Assert.IsNotNull(serialized.FindProperty(field).objectReferenceValue, field);
                }
                Assert.AreSame(menu.GetChild(0).GetComponent<Button>(), menuController.FindControl("title.find"));
                Assert.AreSame(input, menuController.FindControl("title.create.input"));
            }
            finally
            {
                if (opened)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
    }
}
