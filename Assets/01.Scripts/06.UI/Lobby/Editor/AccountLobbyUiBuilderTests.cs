using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Geumeum.UI.Editor
{
    public sealed class AccountLobbyUiBuilderTests
    {
        [Test]
        public void VisualTreeHasBoundPagesKoreanFontAndResponsiveCanvas()
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                AccountLobbyUiBuilder.FONT_PATH);
            Assert.IsNotNull(font, "프로젝트의 한글 TMP 글꼴이 필요하다");
            GameObject root = AccountLobbyUiBuilder.CreateVisualTree(null, font);
            try
            {
                Assert.AreEqual(AccountLobbyUiBuilder.ROOT_NAME, root.name);
                CanvasScaler scaler = root.GetComponent<CanvasScaler>();
                Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
                Assert.AreEqual(new Vector2(1920f, 1080f), scaler.referenceResolution);
                Assert.IsNull(root.transform.Find("Backdrop/HomePage"), "첫 화면은 Title 씬이 맡는다");
                Assert.IsNotNull(root.transform.Find("Backdrop/RoomsPage"));
                Assert.IsNotNull(root.transform.Find("Backdrop/PartyPage"));
                Assert.IsNotNull(root.transform.Find("Backdrop/LoadingPage"));
                // RunEventSystem은 Mirror 스폰 전까지 꺼져 있으므로 오프라인 입력을 따로 가진다.
                Transform input = root.transform.Find(AccountLobbyUiBuilder.EVENT_SYSTEM_NAME);
                Assert.IsNotNull(input);
                Assert.IsFalse(input.gameObject.activeSelf, "패널이 다른 EventSystem 유무를 보고 켠다");
                Assert.IsNotNull(input.GetComponent<UnityEngine.EventSystems.EventSystem>());
                Assert.IsNotNull(input.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>());
                Assert.AreEqual(1, root.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true).Length);

                TMP_Text[] labels = root.GetComponentsInChildren<TMP_Text>(true);
                Assert.Greater(labels.Length, 12);
                foreach (TMP_Text label in labels)
                {
                    Assert.AreSame(font, label.font, label.name);
                }

                SerializedObject panel = new SerializedObject(root.GetComponent<AccountLobbyPanel>());
                string[] required =
                {
                    "_canvas", "_canvasGroup", "_frontendEventSystem", "_loadingPage", "_roomsPage",
                    "_partyPage", "_statusText", "_identityText", "_roomSummaryText",
                    "_partyTitleText", "_partyMembersText", "_partyHintText", "_roomNameInput",
                    "_roomListContent", "_roomRowTemplate",
                    "_roomsRefreshButton", "_roomsCreateButton", "_roomsBackButton",
                    "_roomsPartyButton", "_partyBrowseButton", "_partyLeaveButton",
                    "_partyReadyButton", "_partyBotsButton", "_partyStartButton", "_partyRetryButton"
                };
                foreach (string field in required)
                {
                    SerializedProperty property = panel.FindProperty(field);
                    Assert.IsNotNull(property, field);
                    Assert.IsNotNull(property.objectReferenceValue, field);
                }
                Assert.AreEqual(3, panel.FindProperty("_preferenceButtons").arraySize);
                Assert.IsNull(panel.FindProperty("_homePage"));
                Assert.IsNull(panel.FindProperty("_homeFindButton"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void FindControlMapsHarnessIdsToBoundSelectables()
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                AccountLobbyUiBuilder.FONT_PATH);
            GameObject root = AccountLobbyUiBuilder.CreateVisualTree(null, font);
            try
            {
                AccountLobbyPanel panel = root.GetComponent<AccountLobbyPanel>();
                SerializedObject serialized = new SerializedObject(panel);
                Assert.AreSame(serialized.FindProperty("_roomsCreateButton").objectReferenceValue,
                    panel.FindControl("rooms.create"));
                Assert.AreSame(serialized.FindProperty("_roomsRefreshButton").objectReferenceValue,
                    panel.FindControl("rooms.refresh"));
                Assert.AreSame(serialized.FindProperty("_roomNameInput").objectReferenceValue,
                    panel.FindControl("rooms.input"));
                Assert.AreSame(serialized.FindProperty("_partyStartButton").objectReferenceValue,
                    panel.FindControl("party.start"));
                Assert.AreSame(serialized.FindProperty("_preferenceButtons").GetArrayElementAtIndex(2)
                    .objectReferenceValue, panel.FindControl("party.pref:2"));
                Assert.IsNull(panel.FindControl("party.pref:3"));
                Assert.IsNull(panel.FindControl("rooms.row:00000000-0000-0000-0000-000000000000"),
                    "렌더링되지 않은 방 행은 누를 수 없다");
                Assert.IsNull(panel.FindControl("home.find"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}