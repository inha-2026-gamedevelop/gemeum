// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Systems;

namespace Geumeum.EditorTools
{
    /// <summary>열려 있는 생산 Field 씬의 목표 결계 참조를 반복 실행 가능하게 연결한다</summary>
    public static class FieldBarrierWiringHelper
    {
        private const string FIELD_SCENE_PATH = "Assets/00.Scenes/Release/Field.unity";
        private const string HOST_PATH = "@Gameplay";
        private const string INITIAL_CENTER_PATH = "@Stage/BarrierCenter";
        private const string START_PATH = "@Gameplay/SpawnMarkers/Player_Spawn_1";
        private const string SHOP_PATH = "@Gameplay/ShopInteractionPoint";
        private const string BOSS_ENTRANCE_PATH = "@Gameplay/BossEntranceGate";

        private static readonly Vector3 G_FallbackCenter = new Vector3(2f, 0f, 6f);

        [MenuItem("그믐/필드/Field 목표 결계 배선")]
        private static void WireFieldBarrier()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[FieldBarrierWiringHelper] Play Mode에서는 배선할 수 없다");
                return;
            }

            Scene scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || (scene.path != FIELD_SCENE_PATH))
            {
                Debug.LogError($"[FieldBarrierWiringHelper] 먼저 {FIELD_SCENE_PATH} 씬을 열어야 한다");
                return;
            }

            Transform host = FindRequired(scene, HOST_PATH);
            Transform initialCenter = FindRequired(scene, INITIAL_CENTER_PATH);
            Transform start = FindRequired(scene, START_PATH);
            Transform shop = FindRequired(scene, SHOP_PATH);
            Transform bossEntrance = FindRequired(scene, BOSS_ENTRANCE_PATH);
            if ((host == null) || (initialCenter == null) || (start == null) ||
                (shop == null) || (bossEntrance == null))
            {
                return;
            }

            BarrierMapBinding binding = ResolveBinding(host);
            if (binding == null)
            {
                return;
            }

            var landmarks = new[]
            {
                new LandmarkWire(BarrierLandmarkKind.Start, start),
                new LandmarkWire(BarrierLandmarkKind.Shop, shop),
                new LandmarkWire(BarrierLandmarkKind.BossEntrance, bossEntrance),
            };
            RunPhase[] fallbackPhases =
            {
                RunPhase.Shrink1,
                RunPhase.Shrink2,
                RunPhase.Shrink3,
            };

            var serialized = new SerializedObject(binding);
            serialized.Update();
            if (Matches(serialized, initialCenter, landmarks, fallbackPhases))
            {
                Debug.Log("[FieldBarrierWiringHelper] Field 목표 결계 배선이 이미 정확하다", binding);
                Selection.activeObject = binding;
                return;
            }

            Undo.RecordObject(binding, "Field 목표 결계 배선");
            serialized.FindProperty("_initialCenter").objectReferenceValue = initialCenter;
            WriteLandmarks(serialized.FindProperty("_landmarks"), landmarks);
            WriteFallbacks(serialized.FindProperty("_fallbackCenters"), fallbackPhases);
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(binding);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeObject = binding;
            Debug.Log("[FieldBarrierWiringHelper] Field 목표 결계 배선을 적용했다. 검토 후 씬을 저장한다", binding);
        }

        private static BarrierMapBinding ResolveBinding(Transform host)
        {
            BarrierMapBinding[] bindings = Object.FindObjectsByType<BarrierMapBinding>(
                FindObjectsInactive.Include);
            if (bindings.Length > 1)
            {
                Debug.LogError("[FieldBarrierWiringHelper] BarrierMapBinding이 둘 이상이라 자동 배선할 수 없다");
                return null;
            }

            if (bindings.Length == 1)
            {
                if (bindings[0].transform != host)
                {
                    Debug.LogError($"[FieldBarrierWiringHelper] 기존 BarrierMapBinding이 {HOST_PATH} 밖에 있다", bindings[0]);
                    return null;
                }

                return bindings[0];
            }

            return Undo.AddComponent<BarrierMapBinding>(host.gameObject);
        }

        private static Transform FindRequired(Scene scene, string path)
        {
            int separator = path.IndexOf('/');
            string rootName = (separator < 0) ? path : path.Substring(0, separator);
            string childPath = (separator < 0) ? null : path.Substring(separator + 1);
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; ++i)
            {
                if (roots[i].name != rootName)
                {
                    continue;
                }

                Transform found = string.IsNullOrEmpty(childPath)
                    ? roots[i].transform
                    : roots[i].transform.Find(childPath);
                if (found != null)
                {
                    return found;
                }
            }

            Debug.LogError($"[FieldBarrierWiringHelper] 필수 Transform을 찾을 수 없다: {path}");
            return null;
        }

        private static bool Matches(SerializedObject serialized, Transform initialCenter,
            LandmarkWire[] landmarks, RunPhase[] fallbackPhases)
        {
            if (serialized.FindProperty("_initialCenter").objectReferenceValue != initialCenter)
            {
                return false;
            }

            SerializedProperty landmarkArray = serialized.FindProperty("_landmarks");
            if (landmarkArray.arraySize != landmarks.Length)
            {
                return false;
            }

            for (int i = 0; i < landmarks.Length; ++i)
            {
                SerializedProperty element = landmarkArray.GetArrayElementAtIndex(i);
                if ((element.FindPropertyRelative("Kind").intValue != (int) landmarks[i].Kind) ||
                    (element.FindPropertyRelative("Target").objectReferenceValue != landmarks[i].Target))
                {
                    return false;
                }
            }

            SerializedProperty fallbackArray = serialized.FindProperty("_fallbackCenters");
            if (fallbackArray.arraySize != fallbackPhases.Length)
            {
                return false;
            }

            for (int i = 0; i < fallbackPhases.Length; ++i)
            {
                SerializedProperty element = fallbackArray.GetArrayElementAtIndex(i);
                if ((element.FindPropertyRelative("Phase").intValue != (int) fallbackPhases[i]) ||
                    (element.FindPropertyRelative("Center").vector3Value != G_FallbackCenter))
                {
                    return false;
                }
            }

            return true;
        }

        private static void WriteLandmarks(SerializedProperty array, LandmarkWire[] landmarks)
        {
            array.arraySize = landmarks.Length;
            for (int i = 0; i < landmarks.Length; ++i)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("Kind").intValue = (int) landmarks[i].Kind;
                element.FindPropertyRelative("Target").objectReferenceValue = landmarks[i].Target;
            }
        }

        private static void WriteFallbacks(SerializedProperty array, RunPhase[] fallbackPhases)
        {
            array.arraySize = fallbackPhases.Length;
            for (int i = 0; i < fallbackPhases.Length; ++i)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("Phase").intValue = (int) fallbackPhases[i];
                element.FindPropertyRelative("Center").vector3Value = G_FallbackCenter;
            }
        }

        private readonly struct LandmarkWire
        {
            public BarrierLandmarkKind Kind { get; }
            public Transform Target { get; }

            public LandmarkWire(BarrierLandmarkKind kind, Transform target)
            {
                Kind = kind;
                Target = target;
            }
        }
    }
}
