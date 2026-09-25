// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using Mirror;

namespace Geumeum.UI.Editor
{
    /// <summary>
    /// 잡귀 머리 위 체력 바 프리팹을 만들고 잡귀 프리팹 세 종에 중첩 프리팹으로 붙인다
    ///
    /// 바 프리팹은 이미 있으면 다시 만들지 않는다. 손으로 다듬은 모양을 덮어쓰지 않기 위해서다
    /// 잡귀 프리팹에 이미 바가 있으면 건너뛰므로 여러 번 실행해도 결과가 같다
    /// </summary>
    public static class EnemyHpBarWiringHelper
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string BAR_PREFAB_PATH = "Assets/02.Prefabs/UI/HUD/EnemyHpBar.prefab";
        private const string BAR_NAME        = "EnemyHpBar";
        private const string BACKGROUND_NAME = "Background";
        private const string FILL_NAME       = "Fill";

        // HUD 체력 바와 같은 그림을 쓴다. Filled 타입은 스프라이트가 없으면 비율을 무시하고 꽉 채워 그린다
        private const string TRACK_SPRITE_PATH = "Assets/13.UI/00.UI_Resource/HUD/Bar/Bar_Track.png";
        private const string FILL_SPRITE_PATH  = "Assets/13.UI/00.UI_Resource/HUD/Bar/Bar_Fill_Health.png";

        private static readonly string[] ENEMY_PREFAB_PATHS =
        {
            "Assets/02.Prefabs/Enemy/Aekgwi.prefab",
            "Assets/02.Prefabs/Enemy/Changgwi.prefab",
            "Assets/02.Prefabs/Enemy/Heokkaebi.prefab",
        };

        // 캔버스 픽셀 크기와 월드 배율. 70 x 165 픽셀이 0.7 x 1.65 미터 영역이 된다
        // 바 그림은 위아래 투명 여백이 커서 보이는 띠는 이보다 훨씬 얇다. 높이를 줄이면 띠가 선처럼 눌린다
        // 창귀를 9m 거리에서 보고 정한 값이다
        private const float BAR_WIDTH        = 70f;
        private const float BAR_HEIGHT       = 165f;
        private const float BAR_WORLD_SCALE  = 0.01f;
        private const float FILL_INSET       = 0f;

        // 파티 줄 체력 채움과 같은 색. 채움 그림은 흰색이라 색을 입혀야 붉어진다
        private static readonly Color FILL_COLOR = new Color(0.702f, 0.228f, 0.180f, 1f);

        // 콜라이더 꼭대기에서 이만큼 위에 둔다. 카메라가 내려다보므로 조금만 올려도 화면에서는 크게 뜬다
        private const float HEAD_OFFSET      = 0.1f;

        // 콜라이더를 못 찾았을 때 쓰는 높이
        private const float FALLBACK_HEIGHT  = 2.4f;

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem("그믐/Production/UI/Wire enemy HP bars")]
        private static void WireAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[EnemyHpBarWiring] 플레이 모드에서는 실행하지 않는다");
                return;
            }

            GameObject bar = EnsureBarPrefab();
            if (bar == null)
            {
                return;
            }

            foreach (string path in ENEMY_PREFAB_PATHS)
            {
                AttachToEnemy(path, bar);
            }

            AssetDatabase.SaveAssets();
        }

        private static GameObject EnsureBarPrefab()
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(BAR_PREFAB_PATH);
            if (existing != null)
            {
                return existing;
            }

            GameObject root = new GameObject(BAR_NAME, typeof(RectTransform));
            try
            {
                RectTransform rootRect = (RectTransform)root.transform;
                rootRect.sizeDelta  = new Vector2(BAR_WIDTH, BAR_HEIGHT);
                rootRect.localScale = Vector3.one * BAR_WORLD_SCALE;

                Canvas canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;

                CanvasGroup group = root.AddComponent<CanvasGroup>();
                group.alpha          = 0f;
                group.interactable   = false;
                group.blocksRaycasts = false;

                Sprite track     = AssetDatabase.LoadAssetAtPath<Sprite>(TRACK_SPRITE_PATH);
                Sprite fillImage = AssetDatabase.LoadAssetAtPath<Sprite>(FILL_SPRITE_PATH);
                if ((track == null) || (fillImage == null))
                {
                    Debug.LogError("[EnemyHpBarWiring] HUD 바 스프라이트를 찾지 못했다");
                    return null;
                }

                CreateImage(BACKGROUND_NAME, rootRect, track, 0f);
                Image fill = CreateImage(FILL_NAME, rootRect, fillImage, FILL_INSET);
                fill.type       = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Horizontal;
                fill.fillOrigin = (int)Image.OriginHorizontal.Left;
                fill.fillAmount = 1f;
                fill.color      = FILL_COLOR;

                EnemyHpBar view = root.AddComponent<EnemyHpBar>();
                SerializedObject so = new SerializedObject(view);
                so.FindProperty("_canvasGroup").objectReferenceValue = group;
                so.FindProperty("_hpFill").objectReferenceValue      = fill;
                so.ApplyModifiedPropertiesWithoutUndo();

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, BAR_PREFAB_PATH, out bool success);
                if (!success)
                {
                    Debug.LogError($"[EnemyHpBarWiring] 바 프리팹을 저장하지 못했다: {BAR_PREFAB_PATH}");
                    return null;
                }

                Debug.Log($"[EnemyHpBarWiring] 바 프리팹을 만들었다: {BAR_PREFAB_PATH}");
                return saved;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Image CreateImage(string name, RectTransform parent, Sprite sprite, float inset)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);

            Image image = go.AddComponent<Image>();
            image.sprite        = sprite;
            image.raycastTarget = false;
            return image;
        }

        private static void AttachToEnemy(string path, GameObject bar)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            if (contents == null)
            {
                Debug.LogError($"[EnemyHpBarWiring] 잡귀 프리팹을 열지 못했다: {path}");
                return;
            }

            try
            {
                if (contents.GetComponentInChildren<EnemyHpBar>(true) != null)
                {
                    Debug.Log($"[EnemyHpBarWiring] 이미 붙어 있어 건너뛴다: {path}");
                    return;
                }

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(bar, contents.transform);
                instance.transform.localPosition = Vector3.up * FindHeadHeight(contents);
                instance.transform.localRotation = Quaternion.identity;

                PrefabUtility.SaveAsPrefabAsset(contents, path);
                Debug.Log($"[EnemyHpBarWiring] 바를 붙였다: {path}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            RestoreAssetId(path);
        }

        /// <summary>
        /// LoadPrefabContents로 연 프리팹은 Mirror가 씬 오브젝트로 보고 저장할 때 assetId를 0으로 지운다
        /// 에디터는 읽을 때 다시 계산해 넘어가지만 빌드에는 0이 그대로 실려 스폰이 깨진다
        /// 저장된 프리팹에서 한 번 읽어 GUID 기준 값을 되살리고 디스크에 다시 쓴다
        /// </summary>
        private static void RestoreAssetId(string path)
        {
            GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if ((saved == null) || (!saved.TryGetComponent(out NetworkIdentity identity)))
            {
                return;
            }

            if (identity.assetId == 0u)
            {
                Debug.LogError($"[EnemyHpBarWiring] assetId를 되살리지 못했다: {path}");
                return;
            }

            EditorUtility.SetDirty(identity);
        }

        /// <summary> 루트 기준 콜라이더 꼭대기 높이. 판정용 캡슐이 몸 크기를 가장 잘 나타낸다 </summary>
        private static float FindHeadHeight(GameObject root)
        {
            CapsuleCollider capsule = root.GetComponentInChildren<CapsuleCollider>(true);
            if (capsule == null)
            {
                Debug.LogWarning($"[EnemyHpBarWiring] {root.name}에 CapsuleCollider가 없어 기본 높이를 쓴다");
                return FALLBACK_HEIGHT;
            }

            Vector3 top   = capsule.center + (Vector3.up * (capsule.height * 0.5f));
            Vector3 world = capsule.transform.TransformPoint(top);
            return root.transform.InverseTransformPoint(world).y + HEAD_OFFSET;
        }
    }
}
