#if UNITY_EDITOR
// System
using System;
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Third party
using DigitalRuby.WeatherMaker;
using Mirror;

// Project
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Art.Editor
{
    /// <summary>Idempotent production asset and open-scene wiring owned by the parent operator</summary>
    public static class ArtProductionWiringHelper
    {
        private const string PRESENTATION_PATH =
            "Assets/09.Data/Presentation/PresentationConfigSO.asset";
        private const string PLAYER_PATH = "Assets/02.Prefabs/Network/Player.prefab";
        private const string VFX_FOLDER = "Assets/02.Prefabs/Art/VFX";
        private const string WEATHER_PREFAB =
            "Assets/99.ThirdParty/Background/WeatherMaker/Prefab/WeatherMakerPrefab.prefab";

        private readonly struct VfxSpec
        {
            public readonly string Field;
            public readonly string Wrapper;
            public readonly string Source;
            public readonly float Lifetime;
            public readonly float SourceScale;

            public VfxSpec(string field, string wrapper, string source, float lifetime,
                float sourceScale)
            {
                Field = field;
                Wrapper = wrapper;
                Source = source;
                Lifetime = lifetime;
                SourceScale = sourceScale;
            }
        }

        private readonly struct SoundSpec
        {
            public readonly SoundKey Key;
            public readonly string Clip;
            public readonly SoundChannel Channel;
            public readonly bool Is2D;
            public readonly float Volume;
            public readonly float Pitch;
            public readonly float Interval;

            public SoundSpec(SoundKey key, string clip, SoundChannel channel,
                bool is2D, float volume, float pitch, float interval)
            {
                Key = key;
                Clip = clip;
                Channel = channel;
                Is2D = is2D;
                Volume = volume;
                Pitch = pitch;
                Interval = interval;
            }
        }

        private static readonly VfxSpec[] G_Vfx =
        {
            new VfxSpec("_meleeSlashPrefab", VFX_FOLDER + "/Player_MeleeSlash.prefab",
                "Assets/99.ThirdParty/VFX/Piloto Studio/Super Realistic FXs/Super Realistic FX Bundle 02/ARPG Spirit Bloom Spells/SB_Shockwave_Cone.prefab", 0.65f, 0.1f),
            new VfxSpec("_bellPulsePrefab", VFX_FOLDER + "/Player_BellPulse.prefab",
                "Assets/99.ThirdParty/VFX/Piloto Studio/Super Realistic FXs/Super Realistic FX Bundle 02/ARPG Spirit Bloom Spells/SB_Spirit_Bloom.prefab", 1.1f, 0.2f),
            new VfxSpec("_healCastPrefab", VFX_FOLDER + "/Player_HealCast.prefab",
                "Assets/99.ThirdParty/VFX/Piloto Studio/Super Realistic FXs/Super Realistic FX Bundle 02/ARPG Spirit Bloom Spells/SB_Buff_Up.prefab", 1.25f, 0.15f),
            new VfxSpec("_parryPrefab", VFX_FOLDER + "/Player_Parry.prefab",
                "Assets/99.ThirdParty/VFX/Piloto Studio/Super Realistic FXs/Super Realistic FX Bundle 02/ARPG Spirit Bloom Spells/SB_Shockwave_Cone.prefab", 0.55f, 0.1f),
            new VfxSpec("_markPrefab", VFX_FOLDER + "/Player_MarkApplied.prefab",
                "Assets/99.ThirdParty/VFX/Piloto Studio/Super Realistic FXs/Super Realistic FX Bundle 02/ARPG Spirit Bloom Spells/SB_Curse_Debuff.prefab", 1f, 0.15f),
        };

        private static readonly SoundSpec[] G_Sounds =
        {
            new SoundSpec(SoundKey.MeleeSwing1, "Assets/99.ThirdParty/Sounds/NaPH - RPG & Fantasy Sounds Bundle/Weapons and Combat/Whoosh Metal 4-1.wav", SoundChannel.PlayerNormal, false, 0.72f, 0.04f, 0.05f),
            new SoundSpec(SoundKey.MeleeSwing2, "Assets/99.ThirdParty/Sounds/NaPH - RPG & Fantasy Sounds Bundle/Weapons and Combat/Whoosh Metal 4-1.wav", SoundChannel.PlayerNormal, false, 0.78f, 0.04f, 0.05f),
            new SoundSpec(SoundKey.MeleeSwing3, "Assets/99.ThirdParty/Sounds/NaPH - RPG & Fantasy Sounds Bundle/Weapons and Combat/Whoosh Metal 4-1.wav", SoundChannel.PlayerNormal, false, 0.88f, 0.03f, 0.05f),
            new SoundSpec(SoundKey.MeleeHit, "Assets/99.ThirdParty/Sounds/NaPH - RPG & Fantasy Sounds Bundle/Weapons and Combat/Sword Hit 1-1.wav", SoundChannel.PlayerNormal, false, 0.9f, 0.05f, 0.04f),
            new SoundSpec(SoundKey.MeleeWhiff, "Assets/99.ThirdParty/Sounds/NaPH - RPG & Fantasy Sounds Bundle/Weapons and Combat/Whoosh Metal 4-1.wav", SoundChannel.PlayerNormal, false, 0.7f, 0.05f, 0.04f),
            new SoundSpec(SoundKey.MeleeCritical, "Assets/99.ThirdParty/Sounds/NaPH - RPG & Fantasy Sounds Bundle/Weapons and Combat/Sword Hit 1-1.wav", SoundChannel.PlayerCritical, false, 1f, 0.02f, 0.04f),
            new SoundSpec(SoundKey.BowFire, "Assets/99.ThirdParty/Sounds/NaPH - RPG & Fantasy Sounds Bundle/Weapons and Combat/Arrow Shot 1-1.wav", SoundChannel.PlayerNormal, false, 0.82f, 0.03f, 0.05f),
            new SoundSpec(SoundKey.BellSwing, "Assets/99.ThirdParty/Sounds/NaPH - RPG & Fantasy Sounds Bundle/Misc/Magic Chimes 1-3.wav", SoundChannel.PlayerNormal, false, 0.82f, 0.03f, 0.08f),
            new SoundSpec(SoundKey.HealCast, "Assets/99.ThirdParty/Sounds/NaPH - RPG & Fantasy Sounds Bundle/Misc/Magic Chimes 1-6.wav", SoundChannel.PlayerNormal, false, 0.72f, 0.02f, 0.1f),
            new SoundSpec(SoundKey.HealResolve, "Assets/99.ThirdParty/Sounds/NaPH - RPG & Fantasy Sounds Bundle/Misc/Magic Chimes 1-7.wav", SoundChannel.PlayerNormal, false, 0.86f, 0.02f, 0.1f),
            new SoundSpec(SoundKey.MarkApplied, "Assets/99.ThirdParty/Sounds/NaPH - RPG & Fantasy Sounds Bundle/Misc/Magic Chimes 1-3.wav", SoundChannel.PlayerNormal, false, 0.68f, 0.04f, 0.08f),
            new SoundSpec(SoundKey.ParryStart, "Assets/99.ThirdParty/Sounds/NaPH - RPG & Fantasy Sounds Bundle/Weapons and Combat/Whoosh Metal 4-1.wav", SoundChannel.PlayerCritical, false, 0.8f, 0.02f, 0.08f),
            new SoundSpec(SoundKey.BossTailBreak, "Assets/99.ThirdParty/Sounds/NaPH - RPG & Fantasy Sounds Bundle/Weapons and Combat/Sword Hit 1-1.wav", SoundChannel.World, true, 1f, 0.02f, 0.12f),
            new SoundSpec(SoundKey.BossGateOpen, "Assets/99.ThirdParty/Sounds/NaPH - RPG & Fantasy Sounds Bundle/Doors and Gates/Gate Open 3-1.wav", SoundChannel.World, true, 1f, 0.02f, 1f),
            new SoundSpec(SoundKey.BossPhaseShift, "Assets/99.ThirdParty/Sounds/NaPH - RPG & Fantasy Sounds Bundle/Misc/Magic Chimes 1-7.wav", SoundChannel.World, true, 1f, 0f, 1f),
        };

        [MenuItem("그믐/Production/Art/Create presentation assets")]
        public static void CreatePresentationAssets()
        {
            EnsureFolder("Assets/02.Prefabs/Art", "VFX");
            PresentationConfigSO config =
                AssetDatabase.LoadAssetAtPath<PresentationConfigSO>(PRESENTATION_PATH);
            if (config == null)
            {
                throw new InvalidOperationException("Missing " + PRESENTATION_PATH);
            }

            SerializedObject configObject = new SerializedObject(config);
            for (int i = 0; i < G_Vfx.Length; ++i)
            {
                GameObject wrapper = CreateOrLoadWrapper(in G_Vfx[i]);
                configObject.FindProperty(G_Vfx[i].Field).objectReferenceValue = wrapper;
            }

            configObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            WirePlayerPrefab();
            WireCharacterAnimationReceivers();
            WireSoundCatalog();
            AssetDatabase.SaveAssetIfDirty(config);
            Debug.Log("[ArtProductionWiringHelper] presentation assets wired");
        }

        [MenuItem("그믐/Production/Art/Wire active scene")]
        public static void WireActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                throw new InvalidOperationException("No active scene");
            }

            if (scene.name == Constants.Scene.BOOT)
            {
                LocalObjectPoolService pool = FindSceneComponent<LocalObjectPoolService>(scene);
                if (pool == null)
                {
                    GameObject host = FindSceneRoot(scene, "@LocalPresentationPool");
                    if (host == null)
                    {
                        host = new GameObject("@LocalPresentationPool");
                        SceneManager.MoveGameObjectToScene(host, scene);
                    }

                    pool = host.GetComponent<LocalObjectPoolService>();
                    if (pool == null)
                    {
                        pool = host.AddComponent<LocalObjectPoolService>();
                    }
                }

                if (pool.GetComponent<PresentationPoolPrewarmer>() == null)
                {
                    pool.gameObject.AddComponent<PresentationPoolPrewarmer>();
                    EditorUtility.SetDirty(pool.gameObject);
                }

                if (pool.GetComponent<RunEntranceSoundView>() == null)
                {
                    pool.gameObject.AddComponent<RunEntranceSoundView>();
                    EditorUtility.SetDirty(pool.gameObject);
                }

                if ((FindSceneComponent<LocalObjectPoolService>(scene) == null) ||
                    (pool.GetComponent<PresentationPoolPrewarmer>() == null) ||
                    (pool.GetComponent<RunEntranceSoundView>() == null))
                {
                    throw new InvalidOperationException(
                        "Boot local presentation service wiring failed");
                }
            }
            else if ((scene.name == Constants.Scene.FIELD) ||
                (scene.name == Constants.Scene.BOSS_ARENA))
            {
                WireDayCycle(scene, scene.name == Constants.Scene.FIELD
                    ? DayCycleSceneMode.Field
                    : DayCycleSceneMode.Boss);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[ArtProductionWiringHelper] active scene wired: " + scene.name);
        }

        private static GameObject CreateOrLoadWrapper(in VfxSpec spec)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(spec.Wrapper);
            if (existing != null)
            {
                ApplyWrapperSourceScale(in spec);
                return AssetDatabase.LoadAssetAtPath<GameObject>(spec.Wrapper);
            }

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(spec.Source);
            if (source == null)
            {
                throw new InvalidOperationException("Missing VFX source: " + spec.Source);
            }

            GameObject root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(spec.Wrapper));
            try
            {
                root.AddComponent<LocalPoolRoot>();
                PooledVisualLifetime lifetime = root.AddComponent<PooledVisualLifetime>();
                SerializedObject lifetimeObject = new SerializedObject(lifetime);
                lifetimeObject.FindProperty("_lifetimeSeconds").floatValue = spec.Lifetime;
                lifetimeObject.ApplyModifiedPropertiesWithoutUndo();

                GameObject visual = (GameObject) PrefabUtility.InstantiatePrefab(source);
                visual.transform.SetParent(root.transform, false);
                visual.transform.localScale = Vector3.one * spec.SourceScale;
                return PrefabUtility.SaveAsPrefabAsset(root, spec.Wrapper);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void ApplyWrapperSourceScale(in VfxSpec spec)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(spec.Source);
            if (source == null)
            {
                throw new InvalidOperationException("Missing VFX source: " + spec.Source);
            }

            GameObject root = PrefabUtility.LoadPrefabContents(spec.Wrapper);
            try
            {
                if (root.transform.childCount != 1)
                {
                    throw new InvalidOperationException(
                        $"VFX wrapper must have exactly one source child: {spec.Wrapper}");
                }

                Transform visual = root.transform.GetChild(0);
                GameObject originalSource =
                    PrefabUtility.GetCorrespondingObjectFromOriginalSource(visual.gameObject);
                if (originalSource != source)
                {
                    throw new InvalidOperationException(
                        $"VFX wrapper source mismatch: {spec.Wrapper}");
                }

                Vector3 expectedScale = Vector3.one * spec.SourceScale;
                if ((visual.localScale - expectedScale).sqrMagnitude <= Mathf.Epsilon)
                {
                    return;
                }

                visual.localScale = expectedScale;
                PrefabUtility.SaveAsPrefabAsset(root, spec.Wrapper);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void WirePlayerPrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PLAYER_PATH);
            long networkAssetId = CaptureRootNetworkAssetId(prefab, PLAYER_PATH);
            GameObject root = PrefabUtility.LoadPrefabContents(PLAYER_PATH);
            try
            {
                if (root.GetComponent<PlayerCombatPresentation>() == null)
                {
                    root.AddComponent<PlayerCombatPresentation>();
                }

                PlayerVfxController vfxController = root.GetComponent<PlayerVfxController>();
                if (vfxController == null)
                {
                    vfxController = root.AddComponent<PlayerVfxController>();
                }

                WirePlayerVfxEntries(vfxController);

                if (root.GetComponent<DamageFlashView>() == null)
                {
                    root.AddComponent<DamageFlashView>();
                }

                PrefabUtility.SaveAsPrefabAsset(root, PLAYER_PATH);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            RestoreRootNetworkAssetId(PLAYER_PATH, networkAssetId);
        }

        private static void WirePlayerVfxEntries(PlayerVfxController controller)
        {
            string[] keys =
            {
                PlayerVfxController.MELEE_SLASH_KEY,
                PlayerVfxController.BELL_PULSE_KEY,
                PlayerVfxController.HEAL_CAST_KEY,
                PlayerVfxController.PARRY_KEY,
                PlayerVfxController.MARK_KEY,
            };
            int[] initialCapacities = { 3, 3, 3, 3, 6 };
            int[] maxSizes = { 18, 9, 9, 9, 18 };

            SerializedObject serialized = new SerializedObject(controller);
            SerializedProperty entries = serialized.FindProperty("_entries");
            for (int i = 0; i < keys.Length; ++i)
            {
                bool exists = false;
                for (int j = 0; j < entries.arraySize; ++j)
                {
                    if (entries.GetArrayElementAtIndex(j)
                        .FindPropertyRelative("_key").stringValue == keys[i])
                    {
                        exists = true;
                        break;
                    }
                }

                if (exists)
                {
                    continue;
                }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(G_Vfx[i].Wrapper);
                if (prefab == null)
                {
                    throw new InvalidOperationException("Missing VFX wrapper: " + G_Vfx[i].Wrapper);
                }

                int index = entries.arraySize;
                entries.InsertArrayElementAtIndex(index);
                SerializedProperty entry = entries.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("_key").stringValue = keys[i];
                entry.FindPropertyRelative("_prefab").objectReferenceValue = prefab;
                entry.FindPropertyRelative("_initialCapacity").intValue = initialCapacities[i];
                entry.FindPropertyRelative("_maxSize").intValue = maxSizes[i];
                entry.FindPropertyRelative("_anchor").objectReferenceValue = null;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WireCharacterAnimationReceivers()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[]
            {
                "Assets/02.Prefabs/Art/Player",
            });
            for (int i = 0; i < guids.Length; ++i)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if ((prefab != null) &&
                    (prefab.GetComponentsInChildren<NetworkIdentity>(true).Length > 0))
                {
                    throw new InvalidOperationException(
                        "Art prefab unexpectedly contains NetworkIdentity: " + path);
                }

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Animator[] animators = root.GetComponentsInChildren<Animator>(true);
                    for (int j = 0; j < animators.Length; ++j)
                    {
                        if (animators[j].GetComponent<LegacyPlayerAnimationEventReceiver>() == null)
                        {
                            animators[j].gameObject.AddComponent<LegacyPlayerAnimationEventReceiver>();
                        }
                    }

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        private static void WireSoundCatalog()
        {
            SoundCatalogSO catalog = AssetDatabase.LoadAssetAtPath<SoundCatalogSO>(
                "Assets/09.Data/Sound/SoundCatalogSO.asset");
            if (catalog == null)
            {
                throw new InvalidOperationException("Missing SoundCatalogSO.asset");
            }

            SerializedObject serialized = new SerializedObject(catalog);
            SerializedProperty entries = serialized.FindProperty("_entries");
            for (int i = 0; i < G_Sounds.Length; ++i)
            {
                WriteSoundEntry(entries, in G_Sounds[i]);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
        }

        private static long CaptureRootNetworkAssetId(GameObject prefab,
            string path)
        {
            if (prefab == null)
            {
                throw new InvalidOperationException("Missing prefab: " + path);
            }

            NetworkIdentity identity = prefab.GetComponent<NetworkIdentity>();
            if (identity == null)
            {
                throw new InvalidOperationException(
                    "Network prefab has no root NetworkIdentity: " + path);
            }

            SerializedProperty assetId = new SerializedObject(identity)
                .FindProperty("_assetId");
            if ((assetId == null) || (assetId.longValue <= 0L))
            {
                throw new InvalidOperationException(
                    "Network prefab has no stable assetId before save: " + path);
            }

            return assetId.longValue;
        }

        private static void RestoreRootNetworkAssetId(string path,
            long expectedAssetId)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            NetworkIdentity identity = (prefab == null)
                ? null
                : prefab.GetComponent<NetworkIdentity>();
            if (identity == null)
            {
                throw new InvalidOperationException(
                    "Saved network prefab lost its root NetworkIdentity: " + path);
            }

            var serialized = new SerializedObject(identity);
            SerializedProperty assetId = serialized.FindProperty("_assetId");
            if (assetId == null)
            {
                throw new InvalidOperationException(
                    "Saved NetworkIdentity has no _assetId property: " + path);
            }

            assetId.longValue = expectedAssetId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(identity);
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssetIfDirty(prefab);

            var readback = new SerializedObject(identity);
            readback.Update();
            SerializedProperty persisted = readback.FindProperty("_assetId");
            if ((persisted == null) ||
                (persisted.longValue != expectedAssetId))
            {
                throw new InvalidOperationException(
                    "Network prefab assetId changed during save: " + path);
            }
        }

        private static void WriteSoundEntry(SerializedProperty entries,
            in SoundSpec spec)
        {
            int index = -1;
            for (int i = 0; i < entries.arraySize; ++i)
            {
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("Key").intValue ==
                    (int) spec.Key)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                index = entries.arraySize;
                entries.InsertArrayElementAtIndex(index);
            }

            SerializedProperty entry = entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("Key").intValue = (int) spec.Key;
            entry.FindPropertyRelative("Channel").intValue = (int) spec.Channel;
            entry.FindPropertyRelative("Is2D").boolValue = spec.Is2D;
            entry.FindPropertyRelative("Volume").floatValue = spec.Volume;
            entry.FindPropertyRelative("PitchJitter").floatValue = spec.Pitch;
            entry.FindPropertyRelative("MinInterval").floatValue = spec.Interval;

            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(spec.Clip);
            if (clip == null)
            {
                throw new InvalidOperationException("Missing sound clip: " + spec.Clip);
            }

            SerializedProperty clips = entry.FindPropertyRelative("Clips");
            clips.arraySize = 1;
            clips.GetArrayElementAtIndex(0).objectReferenceValue = clip;
        }

        private static void WireDayCycle(Scene scene, DayCycleSceneMode mode)
        {
            DayCycleController controller = FindSceneComponent<DayCycleController>(scene);
            if (controller == null)
            {
                GameObject host = (mode == DayCycleSceneMode.Boss)
                    ? FindSceneComponent<BossArenaPhaseView>(scene)?.gameObject
                    : FindSceneRoot(scene, "@Stage");
                if (host == null)
                {
                    throw new InvalidOperationException("No scene-local Art host in " + scene.name);
                }

                controller = host.AddComponent<DayCycleController>();
            }

            SerializedObject serialized = new SerializedObject(controller);
            serialized.FindProperty("_mode").enumValueIndex = (int) mode;
            serialized.FindProperty("_dayNightCycle").objectReferenceValue = null;

            if (mode == DayCycleSceneMode.Field)
            {
                GameObject weather = AssetDatabase.LoadAssetAtPath<GameObject>(WEATHER_PREFAB);
                if (weather == null)
                {
                    throw new InvalidOperationException("Missing WeatherMaker prefab");
                }

                WeatherMakerFullScreenFogScript fog =
                    weather.GetComponentInChildren<WeatherMakerFullScreenFogScript>(true);
                WeatherMakerSkySphereScript sky =
                    weather.GetComponentInChildren<WeatherMakerSkySphereScript>(true);
                serialized.FindProperty("_fieldFogProfile").objectReferenceValue =
                    (fog == null) ? null : fog.FogProfile;
                serialized.FindProperty("_fieldSkyProfile").objectReferenceValue =
                    (sky == null) ? null : sky.SkySphereProfile;

                GameObject stage = FindSceneRoot(scene, "@Stage");
                if (stage != null)
                {
                    Light[] lights = stage.GetComponentsInChildren<Light>(true);
                    for (int i = 0; i < lights.Length; ++i)
                    {
                        if (lights[i].type == LightType.Directional)
                        {
                            lights[i].gameObject.SetActive(false);
                            EditorUtility.SetDirty(lights[i].gameObject);
                        }
                    }
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
        }

        private static T FindSceneComponent<T>(Scene scene) where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; ++i)
            {
                T found = roots[i].GetComponentInChildren<T>(true);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static GameObject FindSceneRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; ++i)
            {
                if (roots[i].name == name)
                {
                    return roots[i];
                }
            }

            return null;
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}
#endif
