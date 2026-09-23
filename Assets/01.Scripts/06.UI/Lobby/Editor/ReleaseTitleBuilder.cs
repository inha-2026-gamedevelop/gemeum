using System;
using System.IO;
using System.Security.Cryptography;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Geumeum.UI.Editor
{
    /// <summary>
    /// 개발용 Dev_Title의 화면 구성을 출시 Title 씬으로 복사하고 계정 원정대 메뉴로 바꿔 배선한다.
    /// Dev_Title은 읽기만 하며, Title.unity.meta(GUID)와 빌드 씬 목록은 바꾸지 않는다.
    /// </summary>
    public static class ReleaseTitleBuilder
    {
        public const string SOURCE_PATH = "Assets/00.Scenes/Dev_System/Dev_Title.unity";
        public const string TARGET_PATH = "Assets/00.Scenes/Release/Title.unity";
        public const string TARGET_GUID = "f6432d75f5a3fe940bcf899aab271fba";

        private static readonly Color G_StatusColor = new Color(0.7882353f, 0.74509805f, 0.64705884f, 1f);

        [MenuItem("그믐/UI/출시 Title 화면 만들기", false, 13)]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("플레이 모드에서는 Title 씬을 만들지 않는다");
            }
            if (AssetDatabase.AssetPathToGUID(TARGET_PATH) != TARGET_GUID)
            {
                throw new InvalidOperationException("출시 Title 씬 GUID가 예상과 다르다: " + TARGET_PATH);
            }
            if (AccountLobbyPanelMissing())
            {
                throw new InvalidOperationException("AccountLobbyUI 프리팹을 먼저 만들어야 한다");
            }
            Scene source = SceneManager.GetSceneByPath(SOURCE_PATH);
            if ((source.isLoaded) && (source.isDirty))
            {
                throw new InvalidOperationException("Dev_Title에 저장하지 않은 변경이 있다. 원본은 수정하지 않는다");
            }
            Scene opened = SceneManager.GetSceneByPath(TARGET_PATH);
            if (opened.isLoaded)
            {
                if ((opened.isDirty) || (SceneManager.sceneCount == 1))
                {
                    throw new InvalidOperationException("Title 씬을 닫거나 다른 씬을 연 뒤 다시 실행한다");
                }
                EditorSceneManager.CloseScene(opened, true);
            }

            string sourceFile = FullPath(SOURCE_PATH);
            string sourceHash = Hash(sourceFile);
            File.Copy(sourceFile, FullPath(TARGET_PATH), true);
            AssetDatabase.ImportAsset(TARGET_PATH, ImportAssetOptions.ForceUpdate);
            if (AssetDatabase.AssetPathToGUID(TARGET_PATH) != TARGET_GUID)
            {
                throw new InvalidOperationException("복사 후 Title 씬 GUID가 바뀌었다");
            }

            Scene scene = EditorSceneManager.OpenScene(TARGET_PATH, OpenSceneMode.Additive);
            try
            {
                Adapt(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                {
                    throw new InvalidOperationException("Title 씬 저장 실패");
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }

            if (Hash(sourceFile) != sourceHash)
            {
                throw new InvalidOperationException("Dev_Title 원본이 바뀌었다");
            }
            Debug.Log("[ReleaseTitleBuilder] Dev_Title 화면을 출시 Title 원정대 메뉴로 만들었다");
        }

        private static void Adapt(Scene scene)
        {
            GameObject canvas = Root(scene, "Canvas");
            // 오프라인 계정 입력은 SessionNetwork의 AccountLobbyUI/FrontendEventSystem 하나만 쓴다.
            UnityEngine.Object.DestroyImmediate(Root(scene, "EventSystem"));

            Transform screen = Child(canvas.transform, "TitleScreen");
            UnityEngine.Object.DestroyImmediate(screen.GetComponent<TitleScreenController>());
            Transform menu = Child(screen, "MenuPanel");
            UnityEngine.Object.DestroyImmediate(menu.GetComponent<TitleMenuController>());
            // 동작 없는 설정 버튼은 출시 화면에 남기지 않는다.
            UnityEngine.Object.DestroyImmediate(Child(menu, "SETTINGButton").gameObject);

            Button find = MenuButton(menu, "JOINButton", "FindRoomsButton", "원정대 찾기", 0);
            Button create = MenuButton(menu, "HOSTButton", "CreateRoomButton", "원정대 만들기", 1);
            Button quit = MenuButton(menu, "QUITGAMEButton", "QuitButton", "게임 종료", 2);

            Transform panel = Child(screen, "JoinPanel");
            panel.name = "CreateRoomPanel";
            panel.gameObject.SetActive(false);
            SetText(Child(panel, "Title"), "새 원정대 이름");
            Transform inputObject = Child(panel, "AddressInput");
            inputObject.name = "RoomNameInput";
            TMP_InputField input = inputObject.GetComponent<TMP_InputField>();
            input.text = string.Empty;
            input.characterLimit = AccountLobbyPanel.MAX_ROOM_NAME_LENGTH;
            input.lineType = TMP_InputField.LineType.SingleLine;
            SetText(input.placeholder.transform,
                $"원정대 이름 ({AccountLobbyPanel.MIN_ROOM_NAME_LENGTH}~{AccountLobbyPanel.MAX_ROOM_NAME_LENGTH}자)");
            Transform confirm = Child(panel, "ConfirmButton");
            SetText(confirm, "만들기");
            ClearPersistentCalls(confirm.GetComponent<Button>());
            Transform cancel = Child(panel, "CancelButton");
            SetText(cancel, "취소");
            ClearPersistentCalls(cancel.GetComponent<Button>());

            Transform prompt = Child(screen, "PressAnyKeyPanel");
            TMP_Text promptLabel = Child(prompt, "PromptText").GetComponent<TMP_Text>();
            SetText(promptLabel.transform, "아무 키나 눌러 시작");
            Button promptButton = PromptButton(prompt.gameObject);

            TMP_Text status = Label("StatusText", screen, promptLabel.font, 24f,
                new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(1400f, 44f),
                TextAlignmentOptions.Center);
            TMP_Text identity = Label("IdentityText", screen, promptLabel.font, 26f,
                new Vector2(1f, 1f), new Vector2(-340f, -50f), new Vector2(600f, 44f),
                TextAlignmentOptions.MidlineRight);
            // 이름 입력 창은 메뉴와 상태 문구 위에 그린다.
            panel.SetAsLastSibling();

            AccountTitleMenu controller = screen.gameObject.AddComponent<AccountTitleMenu>();
            SerializedObject serialized = new SerializedObject(controller);
            Put(serialized, "_pressAnyKeyPanel", prompt.gameObject);
            Put(serialized, "_promptButton", promptButton);
            Put(serialized, "_menuPanel", menu.gameObject);
            Put(serialized, "_findButton", find);
            Put(serialized, "_createButton", create);
            Put(serialized, "_quitButton", quit);
            Put(serialized, "_createPanel", panel.gameObject);
            Put(serialized, "_roomNameInput", input);
            Put(serialized, "_createConfirmButton", confirm.GetComponent<Button>());
            Put(serialized, "_createCancelButton", cancel.GetComponent<Button>());
            Put(serialized, "_statusText", status);
            Put(serialized, "_identityText", identity);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Button MenuButton(Transform menu, string oldName, string newName, string label, int order)
        {
            Transform item = Child(menu, oldName);
            item.name = newName;
            item.SetSiblingIndex(order);
            SetText(item, label);
            Button button = item.GetComponent<Button>();
            if (button == null)
            {
                throw new InvalidOperationException("메뉴 버튼이 없다: " + oldName);
            }
            ClearPersistentCalls(button);
            return button;
        }

        /// <summary>개발용 씬에 저장된 호출 대상을 지운다. 동작은 AccountTitleMenu가 코드로 연결한다.</summary>
        private static void ClearPersistentCalls(Button button)
        {
            if (button == null)
            {
                throw new InvalidOperationException("버튼 컴포넌트가 없다");
            }
            SerializedObject serialized = new SerializedObject(button);
            SerializedProperty calls = serialized.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            if (calls == null)
            {
                throw new InvalidOperationException("버튼 호출 목록을 찾지 못했다: " + button.name);
            }
            calls.ClearArray();
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>안내 영역을 누르는 포인터 경로도 메뉴를 열게 한다. 키 입력은 AccountTitleMenu가 본다.</summary>
        private static Button PromptButton(GameObject prompt)
        {
            Image hitArea = prompt.GetComponent<Image>();
            if (hitArea == null)
            {
                hitArea = prompt.AddComponent<Image>();
            }
            hitArea.color = new Color(0f, 0f, 0f, 0f);
            hitArea.raycastTarget = true;
            Button button = prompt.GetComponent<Button>();
            if (button == null)
            {
                button = prompt.AddComponent<Button>();
            }
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hitArea;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            return button;
        }

        private static TMP_Text Label(string name, Transform parent, TMP_FontAsset font, float size,
            Vector2 anchor, Vector2 position, Vector2 dimensions, TextAlignmentOptions alignment)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;
            TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = size;
            label.color = G_StatusColor;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.richText = false;
            label.text = string.Empty;
            return label;
        }

        private static void SetText(Transform target, string value)
        {
            TMP_Text text = target.GetComponent<TMP_Text>();
            if (text == null)
            {
                throw new InvalidOperationException("글자 요소가 없다: " + target.name);
            }
            text.text = value;
            EditorUtility.SetDirty(text);
        }

        private static GameObject Root(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    return root;
                }
            }
            throw new InvalidOperationException("Title 씬 최상위 오브젝트가 없다: " + name);
        }

        private static Transform Child(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child == null)
            {
                throw new InvalidOperationException($"{parent.name}/{name}을 찾지 못했다");
            }
            return child;
        }

        private static void Put(SerializedObject serialized, string name, UnityEngine.Object value)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null)
            {
                throw new InvalidOperationException("직렬화 필드가 없다: " + name);
            }
            property.objectReferenceValue = value;
        }

        private static bool AccountLobbyPanelMissing()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AccountLobbyUiBuilder.UI_PREFAB_PATH);
            Transform input = prefab == null ? null : prefab.transform.Find(AccountLobbyUiBuilder.EVENT_SYSTEM_NAME);
            return (input == null) || (input.GetComponent<EventSystem>() == null);
        }

        private static string FullPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        }

        public static string Hash(string file)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(file))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }
    }
}
