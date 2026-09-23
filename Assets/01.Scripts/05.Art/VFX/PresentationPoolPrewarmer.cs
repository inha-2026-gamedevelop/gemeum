// System
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// Unity
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.VFX;

// Third party
using Mirror;

// Project
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Art
{
    /// <summary>Boot에서 등록된 플레이어 VFX 풀과 실제 GPU 렌더를 끝낸 뒤 접속을 연다.</summary>
    [DisallowMultipleComponent]
    public sealed class PresentationPoolPrewarmer : MonoBehaviour
    {
        private const int COOK_LAYER = 31;
        private static readonly Vector3 COOK_ORIGIN = new Vector3(100000f, 100000f, 100000f);

        [SerializeField] private GameObject[] _additionalPlayerPrefabs;
        [SerializeField] private ShaderVariantCollection _shaderVariants;
        [SerializeField, Min(1)] private int _cookFrames = 3;

        private static PresentationPoolPrewarmer _instance;
        private static TaskCompletionSource<bool> _completion;
        private static bool _ready;
        private static string _failure;
        private Coroutine _routine;
        private GameObject _cookingInstance;
        private Camera _cookCamera;
        private RenderTexture _cookTexture;
        private MeshFilter _cookMeshFilter;
        private MeshRenderer _cookMeshRenderer;
        private Mesh _originalCookMesh;
        private Mesh _temporaryCookMesh;
        private bool _originalRendererEnabled;
        private int _warmedCount;
        private int _plannedInstances;
        private int _cookedCount;
        private int _graphCount;
        private int _drawnPixels;
        private bool _skippedGraphics;
        private readonly List<LayerRecord> _layers = new List<LayerRecord>();
        private readonly List<NetworkManagerHUD> _bootHuds = new List<NetworkManagerHUD>();

        private struct PoolPlan
        {
            public GameObject Prefab;
            public int Initial;
            public int Maximum;
        }

        private struct LayerRecord
        {
            public GameObject Object;
            public int Layer;
        }

        public static bool IsReady => _ready;
        public static string Failure => _failure;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            _instance = null;
            _completion = null;
            _ready = false;
            _failure = null;
        }

        private void OnEnable()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogError("[PresentationPoolPrewarmer] Boot 프리워머가 중복 배치되었다", this);
                enabled = false;
                return;
            }
            _instance = this;
            _ready = false;
            _failure = null;
            _warmedCount = 0;
            _plannedInstances = 0;
            _cookedCount = 0;
            _graphCount = 0;
            _drawnPixels = 0;
            _skippedGraphics = false;
            _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Mirror는 StartHost/StartClient에서야 이 설정을 적용한다. Boot 게이트는
            // 그보다 먼저 돌므로, 프리팹이 요청한 경우 로딩 중에도 프레임이 진행되게 한다.
            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                NetworkManager[] managers = FindObjectsByType<NetworkManager>(
                    FindObjectsInactive.Include);
                for (int i = 0; i < managers.Length; ++i)
                {
                    if (managers[i].gameObject.activeInHierarchy && managers[i].enabled)
                    {
                        manager = managers[i];
                        break;
                    }
                }
            }
            if (manager != null && manager.runInBackground)
            {
                Application.runInBackground = true;
            }
            NetworkManagerHUD[] huds = FindObjectsByType<NetworkManagerHUD>(
                FindObjectsInactive.Include);
            for (int i = 0; i < huds.Length; ++i)
            {
                if (huds[i].enabled)
                {
                    _bootHuds.Add(huds[i]);
                    huds[i].enabled = false;
                }
            }
            _routine = StartCoroutine(CoGuardedInitialize());
        }

        private void OnDisable()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
            CleanupCook();
            if (_ready)
            {
                RestoreHuds();
            }
            if (_instance == this)
            {
                if (!_ready && _failure == null)
                {
                    Fail("완료 전 Boot 프리워머가 비활성화되었다");
                }
                _instance = null;
            }
        }

        /// <summary>Boot 씬에서만 완료를 기다린다. Dev 씬 단독 실행은 즉시 통과한다.</summary>
        public static async Task WaitForBootAsync(CancellationToken cancellationToken = default)
        {
            if (_failure != null)
            {
                throw new InvalidOperationException(_failure);
            }
            if (_ready)
            {
                return;
            }
            if (!SceneManager.GetSceneByName(Constants.Scene.BOOT).isLoaded)
            {
                return;
            }
            if (_instance == null || _completion == null)
            {
                throw new InvalidOperationException("Boot 씬의 PresentationPoolPrewarmer가 없다");
            }
            Task readyTask = _completion.Task;
            if (cancellationToken.CanBeCanceled)
            {
                Task canceledTask = Task.Delay(Timeout.Infinite, cancellationToken);
                if (await Task.WhenAny(readyTask, canceledTask) != readyTask)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            await readyTask;
        }

        private IEnumerator CoGuardedInitialize()
        {
            IEnumerator work = CoInitialize();
            while (true)
            {
                bool moved;
                object current;
                try
                {
                    moved = work.MoveNext();
                    current = moved ? work.Current : null;
                }
                catch (Exception exception)
                {
                    Fail("예상하지 못한 프리워밍 예외: " + exception);
                    yield break;
                }
                if (!moved)
                {
                    yield break;
                }
                yield return current;
            }
        }

        private IEnumerator CoInitialize()
        {
            // 다른 Boot MonoBehaviour의 Awake/OnEnable을 먼저 마치게 한다.
            yield return null;
            if (!SceneManager.GetSceneByName(Constants.Scene.BOOT).isLoaded)
            {
                Complete();
                yield break;
            }
            if (ShouldSkipGraphics())
            {
                _skippedGraphics = true;
                Complete();
                yield break;
            }
            if (!GameDB.IsInitialized || GameDB.Presentation == null || !LocalObjectPoolService.HasInstance)
            {
                Fail("GameDB.Presentation 또는 LocalObjectPoolService가 초기화되지 않았다");
                yield break;
            }

            LocalObjectPoolService pool = LocalObjectPoolService.Instance;
            Dictionary<GameObject, PoolPlan> plans = new Dictionary<GameObject, PoolPlan>();
            PresentationConfigSO config = GameDB.Presentation;
            AddPlan(plans, config.MeleeSlashPrefab, 3, 18);
            AddPlan(plans, config.BellPulsePrefab, 3, 9);
            AddPlan(plans, config.HealCastPrefab, 3, 9);
            AddPlan(plans, config.ParryPrefab, 3, 9);
            AddPlan(plans, config.MarkPrefab, 6, 18);

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null || manager.playerPrefab == null)
            {
                Fail("NetworkManager.playerPrefab이 등록되지 않았다");
                yield break;
            }
            if (!CollectPlayer(plans, manager.playerPrefab))
            {
                Fail("기본 플레이어 VFX 등록 목록이 유효하지 않다");
                yield break;
            }
            if (_additionalPlayerPrefabs != null)
            {
                for (int i = 0; i < _additionalPlayerPrefabs.Length; ++i)
                {
                    if (_additionalPlayerPrefabs[i] == null)
                    {
                        Fail($"추가 플레이어 프리팹 {i}가 비어 있다");
                        yield break;
                    }
                    if (!CollectPlayer(plans, _additionalPlayerPrefabs[i]))
                    {
                        Fail($"추가 플레이어 프리팹 {i} VFX 목록이 유효하지 않다");
                        yield break;
                    }
                }
            }

            foreach (PoolPlan plan in plans.Values)
            {
                _plannedInstances += plan.Initial;
            }
            foreach (PoolPlan plan in plans.Values)
            {
                // 최초 호출에 병합된 최대 상한을 등록하고 한 프레임에 한 개씩 증설한다.
                for (int count = 0; count <= plan.Initial; ++count)
                {
                    if (!pool.EnsurePrewarmed(plan.Prefab, count, plan.Maximum))
                    {
                        Fail($"풀 프리워밍 실패: {plan.Prefab.name}");
                        yield break;
                    }
                    yield return null;
                }
                ++_warmedCount;
            }
            if (_shaderVariants != null)
            {
                _shaderVariants.WarmUp();
                yield return null;
            }
            if (!CreateCookCamera())
            {
                Fail("GPU 쿡 카메라 또는 RenderTexture 생성에 실패했다");
                yield break;
            }

            foreach (PoolPlan plan in plans.Values)
            {
                _cookingInstance = pool.Rent(plan.Prefab, COOK_ORIGIN + Vector3.forward * 6f, Quaternion.identity);
                if (_cookingInstance == null)
                {
                    Fail($"렌더 쿡 대여 실패: {plan.Prefab.name}");
                    yield break;
                }
                PooledVisualLifetime lifetime = _cookingInstance.GetComponent<PooledVisualLifetime>();
                if (lifetime != null)
                {
                    lifetime.SetAutomaticReturnSuppressed(true);
                }
                SetCookLayers(_cookingInstance);
                PrepareTransientSlashMesh(_cookingInstance);
                ParticleSystem[] particles = _cookingInstance.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < particles.Length; ++i)
                {
                    ParticleSystem particle = particles[i];
                    if (particle == null || !particle.gameObject.activeInHierarchy)
                    {
                        continue;
                    }
                    float delay = particle.main.startDelay.constantMax;
                    float simulationSeconds = Mathf.Clamp(delay + 0.3f, 0.3f, 2f);
                    particle.Simulate(simulationSeconds, false, true, true);
                    particle.Play(false);
                }
                VisualEffect[] graphs = _cookingInstance.GetComponentsInChildren<VisualEffect>(true);
                BowLaserVfxView bowLaser = _cookingInstance.GetComponent<BowLaserVfxView>();
                BowChargeVfxView bowCharge = _cookingInstance.GetComponent<BowChargeVfxView>();

                // VFX Graph 활성 직후 simulation tick과 bounds 갱신을 기다린다.
                for (int frame = 0; frame < Mathf.Max(2, _cookFrames); ++frame)
                {
                    yield return null;
                    // 수동 시계로 그리는 활은 방출 모듈이 꺼져 있어 대표 프레임을 직접 평가한다.
                    if (bowLaser != null)
                    {
                        bowLaser.Evaluate(.12f, bowLaser.MaxDistance);
                    }
                    if (bowCharge != null)
                    {
                        bowCharge.Evaluate(1f, .35f);
                    }
                    if (frame > 0)
                    {
                        for (int i = 0; i < graphs.Length; ++i)
                        {
                            if (graphs[i] != null && graphs[i].isActiveAndEnabled)
                            {
                                graphs[i].Simulate(1f / 60f, 1);
                            }
                        }
                    }
                    try
                    {
                        var request = new UniversalRenderPipeline.SingleCameraRequest
                        {
                            destination = _cookTexture
                        };
                        RenderPipeline.SubmitRenderRequest(_cookCamera, request);
                    }
                    catch (Exception exception)
                    {
                        Fail($"렌더 쿡 실패 ({plan.Prefab.name}): {exception.Message}");
                        yield break;
                    }
                }
                if (!SystemInfo.supportsAsyncGPUReadback)
                {
                    Fail("GPU readback을 지원하지 않아 렌더 완료를 확인할 수 없다");
                    yield break;
                }
                AsyncGPUReadbackRequest readback = AsyncGPUReadback.Request(_cookTexture, 0);
                float deadline = Time.realtimeSinceStartup + 10f;
                while (!readback.done && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }
                if (!readback.done || readback.hasError)
                {
                    Fail($"GPU 렌더 완료 확인 실패: {plan.Prefab.name}");
                    yield break;
                }
                int prefabPixels = 0;
                var pixels = readback.GetData<Color32>();
                for (int i = 0; i < pixels.Length; ++i)
                {
                    Color32 pixel = pixels[i];
                    if (pixel.r != 0 || pixel.g != 0 || pixel.b != 0 || pixel.a != 0)
                    {
                        ++prefabPixels;
                        ++_drawnPixels;
                    }
                }
                if (prefabPixels == 0)
                {
                    Debug.LogWarning($"[PresentationPoolPrewarmer] 렌더 쿡은 완료됐으나 픽셀이 비었다: {plan.Prefab.name} particles={particles.Length} graphs={graphs.Length}", this);
                }
                else
                {
                    Debug.Log($"[PresentationPoolPrewarmer] 렌더 쿡: {plan.Prefab.name} pixels={prefabPixels} particles={particles.Length} graphs={graphs.Length}", this);
                }
                for (int i = 0; i < graphs.Length; ++i)
                {
                    if (graphs[i] != null && graphs[i].isActiveAndEnabled)
                    {
                        ++_graphCount;
                    }
                }
                ++_cookedCount;
                ReturnCookingInstance();
                yield return null;
            }
            CleanupCook();
            Complete();
        }

        private static bool ShouldSkipGraphics()
        {
#if UNITY_SERVER
            return true;
#else
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                return true;
            }
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; ++i)
            {
                if (string.Equals(args[i], "-server", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(args[i], "-account-server", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
#endif
        }

        private static void AddPlan(Dictionary<GameObject, PoolPlan> plans,
            GameObject prefab, int initial, int maximum)
        {
            if (prefab == null)
            {
                return;
            }
            if (plans.TryGetValue(prefab, out PoolPlan existing))
            {
                existing.Initial = Mathf.Max(existing.Initial, initial);
                existing.Maximum = Mathf.Max(existing.Maximum, maximum, existing.Initial);
                plans[prefab] = existing;
            }
            else
            {
                plans.Add(prefab, new PoolPlan
                {
                    Prefab = prefab,
                    Initial = initial,
                    Maximum = Mathf.Max(initial, maximum)
                });
            }
        }

        private static bool CollectPlayer(Dictionary<GameObject, PoolPlan> plans, GameObject player)
        {
            PlayerVfxController[] controllers = player.GetComponentsInChildren<PlayerVfxController>(true);
            List<PlayerVfxEntry> entries = new List<PlayerVfxEntry>();
            for (int i = 0; i < controllers.Length; ++i)
            {
                if (!controllers[i].CopyPrewarmEntries(entries))
                {
                    return false;
                }
            }
            for (int i = 0; i < entries.Count; ++i)
            {
                PlayerVfxEntry entry = entries[i];
                AddPlan(plans, entry.Prefab, entry.InitialCapacity, entry.MaxSize);
            }
            return true;
        }

        private bool CreateCookCamera()
        {
            _cookTexture = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32)
            {
                name = "BootVfxCook"
            };
            if (!_cookTexture.Create())
            {
                CleanupCook();
                return false;
            }
            GameObject cameraObject = new GameObject("BootVfxCookCamera");
            cameraObject.transform.position = COOK_ORIGIN;
            cameraObject.transform.rotation = Quaternion.identity;
            _cookCamera = cameraObject.AddComponent<Camera>();
            _cookCamera.enabled = false;
            _cookCamera.cullingMask = 1 << COOK_LAYER;
            _cookCamera.clearFlags = CameraClearFlags.SolidColor;
            _cookCamera.backgroundColor = Color.clear;
            _cookCamera.orthographic = true;
            _cookCamera.orthographicSize = 12f;
            _cookCamera.nearClipPlane = 0.01f;
            _cookCamera.farClipPlane = 100f;
            return true;
        }

        private void SetCookLayers(GameObject instance)
        {
            _layers.Clear();
            Transform[] children = instance.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; ++i)
            {
                GameObject current = children[i].gameObject;
                _layers.Add(new LayerRecord { Object = current, Layer = current.layer });
                current.layer = COOK_LAYER;
            }
        }

        private void PrepareTransientSlashMesh(GameObject instance)
        {
            if (instance.GetComponent<MeleeSlashView>() == null)
            {
                return;
            }
            MeshFilter filter = instance.GetComponent<MeshFilter>();
            MeshRenderer renderer = instance.GetComponent<MeshRenderer>();
            if (filter == null || renderer == null || renderer.sharedMaterial == null ||
                (filter.sharedMesh != null && filter.sharedMesh.vertexCount > 0))
            {
                return;
            }

            _cookMeshFilter = filter;
            _cookMeshRenderer = renderer;
            _originalCookMesh = filter.sharedMesh;
            _originalRendererEnabled = renderer.enabled;
            _temporaryCookMesh = new Mesh { name = "BootMeleeSlashCookQuad" };
            _temporaryCookMesh.vertices = new[]
            {
                new Vector3(-1f, -1f, 0f),
                new Vector3(-1f, 1f, 0f),
                new Vector3(1f, 1f, 0f),
                new Vector3(1f, -1f, 0f)
            };
            _temporaryCookMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _temporaryCookMesh.SetUVs(0, new List<Vector4>
            {
                new Vector4(0.05f, 0f, 0f, 0f),
                new Vector4(0.05f, 1f, 0f, 0f),
                new Vector4(0.05f, 1f, 1f, 0f),
                new Vector4(0.05f, 0f, 1f, 0f)
            });
            _temporaryCookMesh.RecalculateBounds();
            filter.sharedMesh = _temporaryCookMesh;
            renderer.enabled = true;
        }

        private void RestoreTransientSlashMesh()
        {
            if (_cookMeshFilter != null)
            {
                _cookMeshFilter.sharedMesh = _originalCookMesh;
            }
            if (_cookMeshRenderer != null)
            {
                _cookMeshRenderer.enabled = _originalRendererEnabled;
            }
            if (_temporaryCookMesh != null)
            {
                Destroy(_temporaryCookMesh);
            }
            _cookMeshFilter = null;
            _cookMeshRenderer = null;
            _originalCookMesh = null;
            _temporaryCookMesh = null;
        }

        private void ReturnCookingInstance()
        {
            RestoreTransientSlashMesh();
            for (int i = 0; i < _layers.Count; ++i)
            {
                if (_layers[i].Object != null)
                {
                    _layers[i].Object.layer = _layers[i].Layer;
                }
            }
            _layers.Clear();
            if (_cookingInstance != null && LocalObjectPoolService.HasInstance)
            {
                LocalObjectPoolService.Instance.TryReturn(_cookingInstance);
            }
            _cookingInstance = null;
        }

        private void CleanupCook()
        {
            ReturnCookingInstance();
            if (_cookCamera != null)
            {
                Destroy(_cookCamera.gameObject);
                _cookCamera = null;
            }
            if (_cookTexture != null)
            {
                _cookTexture.Release();
                Destroy(_cookTexture);
                _cookTexture = null;
            }
        }

        private void RestoreHuds()
        {
            for (int i = 0; i < _bootHuds.Count; ++i)
            {
                if (_bootHuds[i] != null)
                {
                    _bootHuds[i].enabled = true;
                }
            }
            _bootHuds.Clear();
        }

        private void Complete()
        {
            _ready = true;
            _routine = null;
            RestoreHuds();
            _completion.TrySetResult(true);
            Debug.Log($"[PresentationPoolPrewarmer] Boot 프리워밍 완료 prefabs={_warmedCount} instances={_plannedInstances} cooked={_cookedCount} graphs={_graphCount} pixels={_drawnPixels} skippedGraphics={_skippedGraphics}", this);
        }

        private void Fail(string reason)
        {
            _failure = reason;
            _routine = null;
            CleanupCook();
            _completion.TrySetException(new InvalidOperationException(reason));
            Debug.LogError($"[PresentationPoolPrewarmer] Boot 프리워밍 실패: {reason} prefabs={_warmedCount} instances={_plannedInstances} cooked={_cookedCount} graphs={_graphCount} pixels={_drawnPixels}", this);
        }
    }

    public static class PresentationPoolRules
    {
        public static bool TryResolvePrewarm(bool serverActive,
            bool clientActive, out bool shouldPrewarm)
        {
            shouldPrewarm = clientActive;
            return serverActive || clientActive;
        }
    }
}
