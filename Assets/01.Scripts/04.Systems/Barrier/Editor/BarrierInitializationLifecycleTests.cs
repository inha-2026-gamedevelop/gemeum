// System
using System;
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

// Third party
using Mirror;
using NUnit.Framework;

// Project
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;
using Geumeum.Systems;

public sealed class BarrierInitializationLifecycleTests
{
    private const BindingFlags PRIVATE_INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PRIVATE_STATIC = BindingFlags.Static | BindingFlags.NonPublic;

    private readonly List<GameObject> _objects = new List<GameObject>();
    private bool _wasServerActive;
    private GameDatabaseSO _previousDatabase;
    private RunSceneDirector _previousDirector;
    private BarrierMapBinding _previousBinding;
    private NavMeshDataInstance _navMeshInstance;

    [SetUp]
    public void SetUp()
    {
        _wasServerActive = NetworkServer.active;
        _previousDatabase = (GameDatabaseSO) typeof(GameDB).GetField(
            "_database", PRIVATE_STATIC).GetValue(null);
        _previousDirector = RunSceneDirector.ServerInstance;
        _previousBinding = BarrierMapBindingLocator.Current;

        typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
            .SetValue(null, true);
        BarrierMapBindingLocator.ResetStatic();

        GameDatabaseSO database = AssetDatabase.LoadAssetAtPath<GameDatabaseSO>(
            "Assets/09.Data/Resources/GameDB.asset");
        Assert.IsNotNull(database);
        GameDB.Initialize(database);
    }

    [TearDown]
    public void TearDown()
    {
        if (_navMeshInstance.valid)
        {
            _navMeshInstance.Remove();
        }

        for (int i = _objects.Count - 1; i >= 0; --i)
        {
            if (_objects[i] != null)
            {
                UnityEngine.Object.DestroyImmediate(_objects[i]);
            }
        }

        BarrierMapBindingLocator.ResetStatic();
        if (_previousBinding != null)
        {
            BarrierMapBindingLocator.Register(_previousBinding);
        }

        SetStaticProperty(typeof(RunSceneDirector),
            nameof(RunSceneDirector.ServerInstance), _previousDirector);
        if (_previousDatabase == null)
        {
            GameDB.ResetStatic();
        }
        else
        {
            GameDB.Initialize(_previousDatabase);
        }

        typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
            .SetValue(null, _wasServerActive);
    }

    [Test]
    public void FarmingBeforeFieldBindingWaitsThenInitializesAndRepeatsNextRun()
    {
        BarrierController controller = CreateController();

        Invoke(controller, "HandlePhaseChanged",
            new RunPhaseChangedEvent(RunPhase.Lobby, RunPhase.Farming));

        Assert.IsTrue(Get<bool>(controller, "_hasPendingActivation"));
        Assert.AreEqual(0u, controller.Snapshot.Revision);
        Assert.AreEqual(RunPhase.Lobby, controller.Snapshot.CurrentPhase);

        AddFlatNavMesh();
        RunSceneDirector director = CreateReadyFieldDirector();
        CreateBinding("FirstFieldBinding");
        Invoke(controller, "ServerTick", 0.02f);

        Assert.IsFalse(Get<bool>(controller, "_hasPendingActivation"));
        Assert.AreEqual(1u, controller.Snapshot.Revision);
        Assert.AreEqual(RunPhase.Farming, controller.Snapshot.CurrentPhase);
        Assert.IsTrue(controller.Snapshot.IsActive);

        Invoke(controller, "HandleRunReset", new RunResetEvent(1u));
        UnityEngine.Object.DestroyImmediate(BarrierMapBindingLocator.Current.gameObject);
        Set(director, "_currentScene", RunSceneId.Lobby);

        Invoke(controller, "HandlePhaseChanged",
            new RunPhaseChangedEvent(RunPhase.Lobby, RunPhase.Farming));
        Assert.IsTrue(Get<bool>(controller, "_hasPendingActivation"));
        Assert.AreEqual(0u, controller.Snapshot.Revision);

        Set(director, "_currentScene", RunSceneId.Field);
        CreateBinding("SecondFieldBinding");
        Invoke(controller, "ServerTick", 0.02f);

        Assert.IsFalse(Get<bool>(controller, "_hasPendingActivation"));
        Assert.AreEqual(1u, controller.Snapshot.Revision);
        Assert.AreEqual(RunPhase.Farming, controller.Snapshot.CurrentPhase);
    }

    private BarrierController CreateController()
    {
        GameObject gameObject = CreateObject("BarrierLifecycleController");
        gameObject.AddComponent<NetworkIdentity>();
        return gameObject.AddComponent<BarrierController>();
    }

    private RunSceneDirector CreateReadyFieldDirector()
    {
        GameObject gameObject = CreateObject("BarrierLifecycleDirector");
        gameObject.AddComponent<NetworkIdentity>();
        RunSceneDirector director = gameObject.AddComponent<RunSceneDirector>();
        Set(director, "_currentScene", RunSceneId.Field);
        Set(director, "_state", RunTransitionState.Idle);
        SetStaticProperty(typeof(RunSceneDirector),
            nameof(RunSceneDirector.ServerInstance), director);
        return director;
    }

    private BarrierMapBinding CreateBinding(string name)
    {
        GameObject root = CreateObject(name);
        root.SetActive(false);
        BarrierMapBinding binding = root.AddComponent<BarrierMapBinding>();

        Transform initial = CreateChild(root.transform, "Initial", Vector3.zero);
        Transform start = CreateChild(root.transform, "Start", new Vector3(-2f, 0f, -2f));
        Transform shop = CreateChild(root.transform, "Shop", new Vector3(2f, 0f, 0f));
        Transform gate = CreateChild(root.transform, "Gate", new Vector3(0f, 0f, 2f));
        Set(binding, "_initialCenter", initial);
        Set(binding, "_landmarks", CreateLandmarks(start, shop, gate));

        root.SetActive(true);
        // 일반 MonoBehaviour의 OnEnable은 EditMode fixture 활성 전환에서 자동 호출되지 않는다.
        BarrierMapBindingLocator.Register(binding);
        Assert.AreSame(binding, BarrierMapBindingLocator.Current);
        return binding;
    }

    private static Array CreateLandmarks(Transform start, Transform shop, Transform gate)
    {
        Type entryType = typeof(BarrierMapBinding).GetNestedType(
            "LandmarkEntry", BindingFlags.NonPublic);
        Assert.IsNotNull(entryType);
        Array entries = Array.CreateInstance(entryType, 3);
        entries.SetValue(CreateLandmark(entryType, BarrierLandmarkKind.Start, start), 0);
        entries.SetValue(CreateLandmark(entryType, BarrierLandmarkKind.Shop, shop), 1);
        entries.SetValue(CreateLandmark(entryType, BarrierLandmarkKind.BossEntrance, gate), 2);
        return entries;
    }

    private static object CreateLandmark(Type entryType, BarrierLandmarkKind kind,
        Transform target)
    {
        object entry = Activator.CreateInstance(entryType);
        entryType.GetField("Kind").SetValue(entry, kind);
        entryType.GetField("Target").SetValue(entry, target);
        return entry;
    }

    private void AddFlatNavMesh()
    {
        Assert.Greater(NavMesh.GetSettingsCount(), 0);
        NavMeshBuildSettings settings = NavMesh.GetSettingsByIndex(0);
        var sources = new List<NavMeshBuildSource>
        {
            new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                size = new Vector3(20f, 0.2f, 20f),
                transform = Matrix4x4.TRS(new Vector3(0f, -0.1f, 0f),
                    Quaternion.identity, Vector3.one),
                area = 0,
            },
        };

        NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources,
            new Bounds(Vector3.zero, new Vector3(30f, 4f, 30f)),
            Vector3.zero, Quaternion.identity);
        Assert.IsNotNull(data);
        _navMeshInstance = NavMesh.AddNavMeshData(data);
        Assert.IsTrue(_navMeshInstance.valid);
    }

    private GameObject CreateObject(string name)
    {
        var gameObject = new GameObject(name);
        _objects.Add(gameObject);
        return gameObject;
    }

    private static Transform CreateChild(Transform parent, string name, Vector3 position)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        child.transform.position = position;
        return child.transform;
    }

    private static T Get<T>(object target, string name)
    {
        return (T) target.GetType().GetField(name, PRIVATE_INSTANCE).GetValue(target);
    }

    private static void Set(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, PRIVATE_INSTANCE);
        Assert.IsNotNull(field, name);
        field.SetValue(target, value);
    }

    private static void SetStaticProperty(Type type, string name, object value)
    {
        PropertyInfo property = type.GetProperty(name,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.IsNotNull(property, name);
        property.SetValue(null, value);
    }

    private static object Invoke(object target, string name, params object[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(name, PRIVATE_INSTANCE);
        Assert.IsNotNull(method, name);
        return method.Invoke(target, arguments);
    }
}
