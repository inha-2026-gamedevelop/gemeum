using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.VFX;

namespace Geumeum.Art.Editor
{
    /// <summary>재생 모드에서 고정 프레임으로 할퀴기 VFX를 촬영한다. VFX Graph는 실제 프레임에서만 진행한다.
    /// 기본값은 실제 전투 카메라(CameraConfigSO의 락온 값)를 그대로 재현한 리그다.</summary>
    public sealed class BossScratchVfxCaptureRunner : MonoBehaviour
    {
        public const int PREVIEW_LAYER = 30;

        [Header("출력")]
        public string folder = "D:/reference/BossScratch/v3/cap";
        public string prefix = "s-";
        public float[] times = new float[] { 0f };
        public int captureFramerate = 120;
        public int width = 1280;
        public int height = 720;

        [Header("전투 카메라 (CameraConfigSO 락온 값)")]
        public Vector3 playerPosition = new Vector3(0f, 0f, -3.5f);
        public Vector3 effectPosition = Vector3.zero;
        public float cameraPitch = 22f;
        public float pivotHeight = 1.6f;
        public float sideOffset = 0.6f;
        public float lockOnDistance = 9f;
        public float fieldOfView = 60f;

        [Header("환경")]
        /// <summary>true면 열린 씬의 조명·볼륨·배경을 그대로 쓴다. false면 검증용 회색 바닥 리그를 만든다.</summary>
        public bool useSceneEnvironment = true;
        public bool postProcessing = true;
        public float bloomIntensity = 1.05f;
        public float bloomThreshold = 0.95f;

        [Header("효과")]
        public float effectYaw;
        public string[] overrideNames;
        public float[] overrideValues;
        [Tooltip("0보다 크면 균일 간격 N타 구성을 만들어 넣는다")]
        public int uniformStrikeCount;
        public float uniformStrikeInterval = 0.5f;
        public float uniformRadius = 3f;

        private Camera _camera;
        private RenderTexture _target;
        private VolumeProfile _profile;
        private Material _groundMaterial;
        private Material _figureMaterial;
        private BossScratchVfxDriver _driver;
        private VisualEffect _effect;
        private System.Collections.Generic.List<string> _systemNames;
        private readonly System.Collections.Generic.List<GameObject> _spawned =
            new System.Collections.Generic.List<GameObject>();
        private int _previousCaptureFramerate;

        private void Start()
        {
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            _previousCaptureFramerate = Time.captureFramerate;
            Time.captureFramerate = Mathf.Max(30, captureFramerate);
            Directory.CreateDirectory(folder);
            BuildRig();
            yield return null;
            // 첫 렌더는 후처리 이력이 비어 있어 색이 튄다. 버리는 렌더로 예열한다.
            for (int warm = 0; warm < 3; warm++)
            {
                _camera.Render();
                yield return null;
            }

            _driver.Play();
            // 발사 프레임은 VFX 갱신 전이라 렌더가 불안정하다. 한 프레임 넘긴 뒤를 0초로 잡는다.
            yield return null;
            int startFrame = Time.frameCount;
            int index = 0;
            int guard = Mathf.Max(60, Mathf.CeilToInt(times[times.Length - 1] * captureFramerate) + 120);

            Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
            var log = new System.Text.StringBuilder();
            while (index < times.Length && Time.frameCount - startFrame < guard)
            {
                yield return new WaitForEndOfFrame();
                float t = (Time.frameCount - startFrame) / (float)captureFramerate;
                while (index < times.Length && t + 1e-4f >= times[index])
                {
                    log.Append(index).Append(" want=").Append(times[index].ToString("F3"))
                       .Append(" frame=").Append(Time.frameCount - startFrame)
                       .Append(" driver=").Append(_driver.ElapsedTime.ToString("F3"))
                       .Append(" alive=").Append(TotalAlive());
                    log.AppendLine();
                    RenderTexture previous = RenderTexture.active;
                    _camera.Render();
                    RenderTexture.active = _target;
                    image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    image.Apply();
                    RenderTexture.active = previous;
                    File.WriteAllBytes(Path.Combine(folder, prefix + index.ToString("D3") + ".png"),
                        image.EncodeToPNG());
                    index++;
                }
            }
            DestroyImmediate(image);

            File.WriteAllText(Path.Combine(folder, "_done.txt"),
                index + "/" + times.Length + " frames at " + captureFramerate + " fps"
                + System.Environment.NewLine + log);
            Cleanup();
        }

        private int TotalAlive()
        {
            if (_effect == null || _systemNames == null)
            {
                return -1;
            }
            int sum = 0;
            foreach (string n in _systemNames)
            {
                sum += (int)_effect.GetParticleSystemInfo(n).aliveCount;
            }
            return sum;
        }

        private GameObject NewObject(string objectName, bool previewLayer)
        {
            GameObject go = new GameObject(objectName);
            go.hideFlags = HideFlags.HideAndDontSave;
            if (previewLayer)
            {
                go.layer = PREVIEW_LAYER;
            }
            _spawned.Add(go);
            return go;
        }

        private void AddPrimitive(string objectName, PrimitiveType type, Vector3 position,
            Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = objectName;
            go.hideFlags = HideFlags.HideAndDontSave;
            go.layer = PREVIEW_LAYER;
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            DestroyImmediate(go.GetComponent<Collider>());
            _spawned.Add(go);
        }

        /// <summary>CameraRig와 같은 식으로 전투 카메라 위치·회전을 계산한다.</summary>
        private void PlaceCamera()
        {
            Vector3 toBoss = effectPosition - playerPosition;
            toBoss.y = 0f;
            float yaw = toBoss.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toBoss).eulerAngles.y : 0f;
            Quaternion rotation = Quaternion.Euler(cameraPitch, yaw, 0f);
            Vector3 pivot = playerPosition
                + (Vector3.up * pivotHeight)
                + ((rotation * Vector3.right) * sideOffset);
            Vector3 boomDirection = -(rotation * Vector3.forward);
            _camera.transform.position = pivot + (boomDirection * lockOnDistance);
            _camera.transform.rotation = rotation;
            _camera.fieldOfView = fieldOfView;
        }

        private void BuildRig()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/02.Prefabs/Art/VFX/Boss_ScratchVFX.prefab");
            GameObject instance = Instantiate(prefab);
            instance.name = "Scratch VFX";
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.position = effectPosition;
            instance.transform.rotation = Quaternion.Euler(0f, effectYaw, 0f);
            if (!useSceneEnvironment)
                foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = PREVIEW_LAYER;
            _driver = instance.GetComponent<BossScratchVfxDriver>();
            _effect = instance.GetComponent<VisualEffect>();
            _systemNames = new System.Collections.Generic.List<string>();
            _effect.GetSystemNames(_systemNames);
            if (uniformStrikeCount > 0)
                _driver.SetUniformStrikes(uniformStrikeCount, uniformStrikeInterval, uniformRadius);
            ApplyOverrides(_driver);
            _spawned.Add(instance);

            GameObject cameraObject = NewObject("Scratch capture camera", !useSceneEnvironment);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.allowHDR = true;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 600f;
            UniversalAdditionalCameraData data = _camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = postProcessing;
            _target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf);
            _target.Create();
            _camera.targetTexture = _target;
            PlaceCamera();

            if (useSceneEnvironment)
            {
                // 열린 씬의 하늘·조명·전역 Volume을 그대로 쓴다. 공용 프로필은 건드리지 않는다.
                _camera.clearFlags = CameraClearFlags.Skybox;
                _camera.cullingMask = ~0;
                data.volumeLayerMask = ~0;
                return;
            }

            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.010f, 0.011f, 0.014f);
            _camera.cullingMask = 1 << PREVIEW_LAYER;
            data.volumeLayerMask = 1 << PREVIEW_LAYER;

            GameObject volumeObject = NewObject("Scratch capture volume", true);
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Bloom bloom = _profile.Add<Bloom>(true);
            bloom.intensity.Override(bloomIntensity);
            bloom.threshold.Override(bloomThreshold);
            bloom.scatter.Override(0.72f);
            Tonemapping tonemapping = _profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.Neutral);
            volume.sharedProfile = _profile;

            _groundMaterial = new Material(Shader.Find("Hidden/Geumeum/BossScratchPreviewGround"));
            _groundMaterial.SetColor("_BaseColor", new Color(0.44f, 0.45f, 0.48f));
            _groundMaterial.SetColor("_Ambient", new Color(1.12f, 1.13f, 1.22f));
            _groundMaterial.SetFloat("_Tile", 1.9f);
            AddPrimitive("Ground", PrimitiveType.Cube, effectPosition + new Vector3(0f, -0.2f, 6f),
                new Vector3(140f, 0.4f, 140f), _groundMaterial);

            _figureMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _figureMaterial.SetColor("_BaseColor", new Color(0.045f, 0.043f, 0.050f));
            _figureMaterial.SetFloat("_Smoothness", 0.15f);
            AddPrimitive("Boss stand-in", PrimitiveType.Capsule, effectPosition + new Vector3(0f, 1.1f, 0f),
                new Vector3(0.95f, 1.1f, 0.95f), _figureMaterial);
            AddPrimitive("Player stand-in", PrimitiveType.Capsule, playerPosition + new Vector3(0f, 0.85f, 0f),
                new Vector3(0.65f, 0.85f, 0.65f), _figureMaterial);

            GameObject lightObject = NewObject("Scratch capture light", true);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = new Color(0.86f, 0.9f, 1f);
            light.cullingMask = 1 << PREVIEW_LAYER;
            light.shadows = LightShadows.None;
            light.transform.rotation = Quaternion.Euler(52f, -28f, 0f);
        }

        private void ApplyOverrides(BossScratchVfxDriver driver)
        {
            if (driver == null || overrideNames == null || overrideNames.Length == 0)
            {
                return;
            }
            SerializedObject so = new SerializedObject(driver);
            for (int i = 0; i < overrideNames.Length; i++)
            {
                SerializedProperty p = so.FindProperty(overrideNames[i]);
                if (p == null)
                {
                    continue;
                }
                if (p.propertyType == SerializedPropertyType.Integer)
                    p.intValue = Mathf.RoundToInt(overrideValues[i]);
                else if (p.propertyType == SerializedPropertyType.Boolean)
                    p.boolValue = overrideValues[i] > 0.5f;
                else p.floatValue = overrideValues[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private void Cleanup()
        {
            Time.captureFramerate = _previousCaptureFramerate;
            if (_camera != null)
            {
                _camera.targetTexture = null;
            }
            if (_target != null) { _target.Release(); DestroyImmediate(_target); }
            if (_groundMaterial != null)
            {
                DestroyImmediate(_groundMaterial);
            }
            if (_figureMaterial != null)
            {
                DestroyImmediate(_figureMaterial);
            }
            if (_profile != null)
            {
                foreach (VolumeComponent component in _profile.components)
                {
                    DestroyImmediate(component);
                }
                DestroyImmediate(_profile);
            }
            foreach (GameObject go in _spawned)
            {
                if (go != null)
                {
                    DestroyImmediate(go);
                }
            }
            _spawned.Clear();
            DestroyImmediate(gameObject);
        }
    }

    /// <summary>외부 도구에서 부르기 쉬운 캡처 진입점. 재생 모드에서만 동작한다.</summary>
    public static class BossScratchVfxCapture
    {
        public static string BeginGameCamera(string folder, string prefix, float[] times, int fps,
            int width, int height,
            float playerX, float playerY, float playerZ,
            float effectX, float effectY, float effectZ,
            float pitch, float lockOnDistance, float fov, float pivotHeight, float sideOffset,
            float effectYaw, bool useSceneEnvironment, bool postProcessing,
            int uniformStrikeCount, float uniformStrikeInterval, float uniformRadius,
            string[] overrideNames, float[] overrideValues)
        {
            if (!Application.isPlaying)
            {
                return "not playing";
            }
            string done = Path.Combine(folder, "_done.txt");
            if (File.Exists(done))
            {
                File.Delete(done);
            }

            GameObject go = new GameObject("__ScratchCapture");
            go.hideFlags = HideFlags.HideAndDontSave;
            BossScratchVfxCaptureRunner runner = go.AddComponent<BossScratchVfxCaptureRunner>();
            runner.folder = folder;
            runner.prefix = prefix;
            runner.times = times;
            runner.captureFramerate = fps;
            runner.width = width;
            runner.height = height;
            runner.playerPosition = new Vector3(playerX, playerY, playerZ);
            runner.effectPosition = new Vector3(effectX, effectY, effectZ);
            runner.cameraPitch = pitch;
            runner.lockOnDistance = lockOnDistance;
            runner.fieldOfView = fov;
            runner.pivotHeight = pivotHeight;
            runner.sideOffset = sideOffset;
            runner.effectYaw = effectYaw;
            runner.useSceneEnvironment = useSceneEnvironment;
            runner.postProcessing = postProcessing;
            runner.uniformStrikeCount = uniformStrikeCount;
            runner.uniformStrikeInterval = uniformStrikeInterval;
            runner.uniformRadius = uniformRadius;
            runner.overrideNames = overrideNames;
            runner.overrideValues = overrideValues;
            return "started " + times.Length + " frames -> " + folder;
        }
    }
}
