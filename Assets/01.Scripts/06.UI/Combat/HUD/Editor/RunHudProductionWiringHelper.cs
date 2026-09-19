// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Geumeum.Systems;

namespace Geumeum.UI.Editor
{
    /// <summary> Dev_HUD에서 완성된 HUD · 관리 창 · 결계 벽을 런타임 흐름(SessionManagers · Field)에 옮겨 붙인다 </summary>
    public static class RunHudProductionWiringHelper
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string DEV_HUD_SCENE_PATH     = "Assets/00.Scenes/Dev_System/Dev_HUD.unity";
        private const string FIELD_SCENE_PATH       = "Assets/00.Scenes/Release/Field.unity";
        private const string SESSION_MANAGERS_PATH  = "Assets/02.Prefabs/Network/SessionManagers.prefab";
        private const string RUN_MANAGEMENT_UI_PATH = "Assets/02.Prefabs/UI/RunManagementUI.prefab";
        private const string RUN_HUD_PATH           = "Assets/02.Prefabs/UI/RunHUD.prefab";
        private const string MINIMAP_DOT_PATH       = "Assets/02.Prefabs/UI/HUD/MinimapDot.prefab";
        private const string BANNER_FONT_PATH       = "Assets/13.UI/00.UI_Resource/Fonts/Hangeuljaemin4-Regular SDF.asset";

        private const string DEV_HUD_CANVAS_NAME      = "Canvas";
        private const string BOSS_BAR_NAME            = "HUD_BossBar";
        private const string BARRIER_VISUAL_NAME      = "@BarrierVisual";
        private const string RUN_HUD_NAME             = "RunHUD";
        private const string RUN_MANAGEMENT_UI_NAME   = "RunManagementUI";
        private const string LEGACY_MANAGEMENT_PATH   = "FunctionalManagementUI/ManagementPanel";
        private const string BANNER_NAME              = "BarrierWarningBanner";
        private const string BANNER_MESSAGE_NAME      = "Message";
        private const string KNOB_SPRITE_PATH         = "UI/Skin/Knob.psd";

        private const float DOT_SIZE           = 10f;
        private const float BANNER_WIDTH       = 900f;
        private const float BANNER_HEIGHT      = 64f;
        private const float BANNER_TOP_OFFSET  = -120f;
        private const float BANNER_FONT_SIZE   = 36f;

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem("Geumeum/Production/UI/Wire run HUD and management UI")]
        private static void WireAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[RunHudWiring] 플레이 모드에서는 실행하지 않는다");
                return;
            }

            Scene devHud = OpenAdditive(DEV_HUD_SCENE_PATH, out bool openedDevHud);
            try
            {
                GameObject dot = EnsureMinimapDotPrefab();
                GameObject hud = BuildRunHudPrefab(devHud, dot);
                if (hud == null)
                {
                    return;
                }

                WireSessionManagers(hud);
                WireFieldBarrierVisual(devHud);
            }
            finally
            {
                if (openedDevHud)
                {
                    EditorSceneManager.CloseScene(devHud, true);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[RunHudWiring] 완료: RunHUD · RunManagementUI · Field 결계 벽 연결");
        }

        private static Scene OpenAdditive(string path, out bool opened)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            opened = !scene.isLoaded;
            return opened ? EditorSceneManager.OpenScene(path, OpenSceneMode.Additive) : scene;
        }

        private static GameObject EnsureMinimapDotPrefab()
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(MINIMAP_DOT_PATH);
            if (existing != null)
            {
                return existing;
            }

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(MINIMAP_DOT_PATH));
            var go = new GameObject("MinimapDot", typeof(RectTransform), typeof(Image));
            try
            {
                var rect = go.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(DOT_SIZE, DOT_SIZE);

                var image = go.GetComponent<Image>();
                image.sprite        = AssetDatabase.GetBuiltinExtraResource<Sprite>(KNOB_SPRITE_PATH);
                image.raycastTarget = false;

                return PrefabUtility.SaveAsPrefabAsset(go, MINIMAP_DOT_PATH);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static GameObject BuildRunHudPrefab(Scene devHud, GameObject dotPrefab)
        {
            GameObject source = FindRoot(devHud, DEV_HUD_CANVAS_NAME);
            if ((source == null) || (source.GetComponent<HUDController>() == null))
            {
                Debug.LogError("[RunHudWiring] Dev_HUD에서 HUDController가 붙은 Canvas를 찾지 못했다");
                return null;
            }

            GameObject copy = Object.Instantiate(source);
            try
            {
                copy.name = RUN_HUD_NAME;

                // 보스 바는 아직 값을 넣는 컨트롤러가 없어 빈 막대가 떠 있게 된다
                Transform bossBar = copy.transform.Find(BOSS_BAR_NAME);
                if (bossBar != null)
                {
                    bossBar.gameObject.SetActive(false);
                }

                var minimap = copy.GetComponentInChildren<MinimapPlayerDots>(true);
                if (minimap != null)
                {
                    SetReference(minimap, "_dotPrefab", dotPrefab.GetComponent<RectTransform>());
                }

                EnsureBarrierBanner(copy.transform);
                return PrefabUtility.SaveAsPrefabAsset(copy, RUN_HUD_PATH);
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }
        }

        private static void EnsureBarrierBanner(Transform canvas)
        {
            if (canvas.Find(BANNER_NAME) != null)
            {
                return;
            }

            var banner = new GameObject(BANNER_NAME, typeof(RectTransform), typeof(CanvasGroup));
            var rect = banner.GetComponent<RectTransform>();
            rect.SetParent(canvas, false);
            rect.anchorMin        = new Vector2(0.5f, 1f);
            rect.anchorMax        = new Vector2(0.5f, 1f);
            rect.pivot            = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, BANNER_TOP_OFFSET);
            rect.sizeDelta        = new Vector2(BANNER_WIDTH, BANNER_HEIGHT);

            var message = new GameObject(BANNER_MESSAGE_NAME, typeof(RectTransform), typeof(TextMeshProUGUI));
            var messageRect = message.GetComponent<RectTransform>();
            messageRect.SetParent(rect, false);
            messageRect.anchorMin = Vector2.zero;
            messageRect.anchorMax = Vector2.one;
            messageRect.offsetMin = Vector2.zero;
            messageRect.offsetMax = Vector2.zero;

            var text = message.GetComponent<TextMeshProUGUI>();
            text.font          = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BANNER_FONT_PATH);
            text.fontSize      = BANNER_FONT_SIZE;
            text.alignment     = TextAlignmentOptions.Center;
            text.raycastTarget = false;

            var component = banner.AddComponent<BarrierWarningBanner>();
            SetReference(component, "_canvasGroup", banner.GetComponent<CanvasGroup>());
            SetReference(component, "_messageText", text);
        }

        private static void WireSessionManagers(GameObject hudPrefab)
        {
            GameObject managementPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RUN_MANAGEMENT_UI_PATH);
            if (managementPrefab == null)
            {
                Debug.LogError($"[RunHudWiring] 관리 창 프리팹이 없다: {RUN_MANAGEMENT_UI_PATH}");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(SESSION_MANAGERS_PATH);
            try
            {
                if (root.transform.Find(RUN_HUD_NAME) == null)
                {
                    var hud = (GameObject)PrefabUtility.InstantiatePrefab(hudPrefab, root.transform);
                    hud.name = RUN_HUD_NAME;
                }

                Transform management = root.transform.Find(RUN_MANAGEMENT_UI_NAME);
                if (management == null)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(managementPrefab, root.transform);
                    instance.name = RUN_MANAGEMENT_UI_NAME;
                    management = instance.transform;
                }

                var panel = management.GetComponentInChildren<FunctionalManagementPanel>(true);
                SetReference(panel, "_currency", root.GetComponent<CurrencyManager>());
                SetReference(panel, "_shopTransactions", root.GetComponent<ShopTransactionService>());
                SetReference(panel, "_transferService", root.GetComponent<EquipmentTransferService>());

                // 같은 정적 Instance를 두 창이 다투지 않게 옛 빌더 창은 끈다. 결과 창은 같은 캔버스에 있어 남긴다
                Transform legacy = root.transform.Find(LEGACY_MANAGEMENT_PATH);
                if (legacy != null)
                {
                    legacy.gameObject.SetActive(false);
                }

                PrefabUtility.SaveAsPrefabAsset(root, SESSION_MANAGERS_PATH);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void WireFieldBarrierVisual(Scene devHud)
        {
            GameObject source = FindRoot(devHud, BARRIER_VISUAL_NAME);
            if (source == null)
            {
                Debug.LogError("[RunHudWiring] Dev_HUD에서 결계 벽 오브젝트를 찾지 못했다");
                return;
            }

            Scene field = OpenAdditive(FIELD_SCENE_PATH, out bool openedField);
            try
            {
                if (FindRoot(field, BARRIER_VISUAL_NAME) != null)
                {
                    return;
                }

                GameObject copy = Object.Instantiate(source);
                copy.name = BARRIER_VISUAL_NAME;
                SceneManager.MoveGameObjectToScene(copy, field);
                EditorSceneManager.SaveScene(field);
            }
            finally
            {
                if (openedField)
                {
                    EditorSceneManager.CloseScene(field, true);
                }
            }
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            var roots = new List<GameObject>();
            scene.GetRootGameObjects(roots);
            return roots.Find(go => go.name == name);
        }

        private static void SetReference(Object target, string property, Object value)
        {
            if ((target == null) || (value == null))
            {
                Debug.LogError($"[RunHudWiring] 참조 연결 실패: {property} (대상 {target}, 값 {value})");
                return;
            }

            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
