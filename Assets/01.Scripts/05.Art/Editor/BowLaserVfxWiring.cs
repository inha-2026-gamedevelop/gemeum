using System;
using UnityEditor;
using UnityEngine;

namespace Geumeum.Art.Editor
{
    /// <summary>기존 플레이어 등록 목록을 보존하며 활 표현 두 항목만 연결한다.</summary>
    public static class BowLaserVfxWiring
    {
        public static string Wire()
        {
            const string playerPath = "Assets/02.Prefabs/Network/Player.prefab";
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(playerPath);
            PlayerVfxController controller = player.GetComponent<PlayerVfxController>();
            if (controller == null)
            {
                throw new InvalidOperationException("PlayerVfxController is missing");
            }

            SerializedObject serialized = new SerializedObject(controller);
            SerializedProperty entries = serialized.FindProperty("_entries");
            Register(entries, "bow_charge", "Player_BowCharge", 3, 6);
            Register(entries, "bow_laser_release", "Player_BowLaserRelease", 6, 12);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SavePrefabAsset(player);
            return "Bow charge/release registered; existing entries preserved";
        }

        private static void Register(SerializedProperty entries, string key, string prefabName,
            int capacity, int maximum)
        {
            string path = "Assets/02.Prefabs/Art/VFX/" + prefabName + ".prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                throw new InvalidOperationException("Missing " + path);
            }

            int index = -1;
            for (int i = 0; i < entries.arraySize; i++)
            {
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("_key").stringValue == key)
                {
                    index = i;
                }
            }

            if (index < 0) { index = entries.arraySize; entries.arraySize++; }
            SerializedProperty entry = entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("_key").stringValue = key;
            entry.FindPropertyRelative("_prefab").objectReferenceValue = prefab;
            entry.FindPropertyRelative("_initialCapacity").intValue = capacity;
            entry.FindPropertyRelative("_maxSize").intValue = maximum;
            entry.FindPropertyRelative("_anchor").objectReferenceValue = null;
        }
    }
}
