using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Geumeum.EditorTools
{
    /// <summary> 제작 프리팹에 기능 연결만 보완한다. 팀원의 Dev 씬은 수정하지 않는다 </summary>
    public static class ProductionUiRepair
    {
        [MenuItem("그믐/Production/UI/Repair functional bindings")]
        public static void Repair()
        {
            RepairManagement();
            RepairHud();
            AssetDatabase.SaveAssets();
        }

        private static void RepairManagement()
        {
            const string path = "Assets/02.Prefabs/UI/RunManagementUI.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var panel = root.GetComponentInChildren<Geumeum.UI.FunctionalManagementPanel>(true);
                var title = panel.transform.Find("Window/Title").GetComponent<TMP_Text>();
                Set(panel, "_pageTitle", title);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void RepairHud()
        {
            const string path = "Assets/02.Prefabs/UI/RunHUD.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var font = root.GetComponentInChildren<TMP_Text>(true).font;
                Transform prompt = root.transform.Find("InteractionPrompt");
                if (prompt == null)
                {
                    var rect = CreateRect(root.transform, "InteractionPrompt", new Vector2(680f, 48f));
                    rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f);
                    rect.anchoredPosition = new Vector2(0f, 95f);
                    var background = rect.gameObject.AddComponent<Image>();
                    background.color = new Color(.06f, .07f, .09f, .9f); background.raycastTarget = false;
                    var label = CreateRect(rect, "Label", new Vector2(650f, 44f)).gameObject.AddComponent<TextMeshProUGUI>();
                    label.font = font; label.fontSize = 20f; label.alignment = TextAlignmentOptions.Center;
                    label.color = new Color(.94f, .89f, .78f); label.raycastTarget = false;
                    prompt = rect;
                }
                var view = root.GetComponent<Geumeum.UI.InteractionPromptView>() ?? root.AddComponent<Geumeum.UI.InteractionPromptView>();
                Set(view, "_label", prompt.GetComponentInChildren<TMP_Text>(true));
                Set(view, "_root", prompt.gameObject);
                var dots = root.GetComponentInChildren<Geumeum.UI.MinimapPlayerDots>(true);
                var toggle = dots.GetComponent<Geumeum.UI.MinimapExpandToggle>() ?? dots.gameObject.AddComponent<Geumeum.UI.MinimapExpandToggle>();
                Set(toggle, "_dots", dots);
                Transform backdrop = dots.transform.Find("ExpandedBackdrop");
                if (backdrop == null)
                {
                    var rect = CreateRect(dots.transform, "ExpandedBackdrop", ((RectTransform)dots.transform).sizeDelta);
                    rect.SetAsFirstSibling();
                    var image = rect.gameObject.AddComponent<Image>();
                    image.color = new Color(.05f, .06f, .08f, .92f); image.raycastTarget = false;
                    backdrop = rect;
                }
                Set(toggle, "_backdrop", backdrop.GetComponent<Image>());
                var so = new SerializedObject(toggle);
                so.FindProperty("_expandedScale").floatValue = 3f;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static RectTransform CreateRect(Transform parent, string name, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f,.5f);
            rect.sizeDelta = size; return rect;
        }
        private static void Set(Object target, string property, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(property).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
