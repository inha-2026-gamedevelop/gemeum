using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Geumeum.Art.Editor
{
    /// <summary>열린 게임 씬을 수정하지 않고 할퀴기 프리팹을 제작·시간별 비교한다.</summary>
    public sealed class BossScratchPreview : EditorWindow
    {
        public const string PREFAB_PATH = "Assets/02.Prefabs/Art/VFX/Boss_DoubleScratch.prefab";
        private const int PREVIEW_LAYER = 30;

        /// <summary>참조 영상과 같은 화각으로 맞추기 위한 카메라·노출 값.</summary>
        [System.Serializable]
        public struct Rig
        {
            public Vector3 cameraPosition;
            public Vector3 lookAt;
            public float fieldOfView;
            public float bloomIntensity;
            public float bloomThreshold;
            public int width;
            public int height;

            public static Rig Reference()
            {
                Rig r;
                r.cameraPosition = new Vector3(0.6f, 6.2f, -14.8f);
                r.lookAt = new Vector3(0.1f, 1.1f, 1.0f);
                r.fieldOfView = 40f;
                r.bloomIntensity = 1.15f;
                r.bloomThreshold = 0.95f;
                r.width = 720;
                r.height = 1280;
                return r;
            }
        }

        private Scene _scene;
        private Camera _camera;
        private BossDoubleScratchVfx _effect;
        private RenderTexture _target;
        private Material _groundMaterial;
        private VolumeProfile _profile;
        private Bloom _bloom;
        private Rig _rig = Rig.Reference();
        private float _time;
        private bool _animate = true;
        private bool _postProcessing = true;
        private double _lastUpdate;

        [MenuItem("Geumeum/VFX/Boss Double Scratch Preview")]
        public static void Open()
        {
            GetWindow<BossScratchPreview>("Boss Double Scratch");
        }

        private void OnEnable()
        {
            _lastUpdate = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        private void Setup()
        {
            if (_camera != null) return;

            _scene = EditorSceneManager.NewPreviewScene();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _scene);
            _effect = instance.GetComponent<BossDoubleScratchVfx>();
            _effect.Sample(0f);
            foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = PREVIEW_LAYER;

            GameObject cameraObject = NewObject("Scratch preview camera");
            _camera = cameraObject.AddComponent<Camera>();
            _camera.scene = _scene;
            _camera.enabled = false;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.012f, 0.013f, 0.016f);
            _camera.cullingMask = 1 << PREVIEW_LAYER;
            _camera.allowHDR = true;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 220f;
            UniversalAdditionalCameraData data = _camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.volumeLayerMask = 1 << PREVIEW_LAYER;

            GameObject volumeObject = NewObject("Preview post processing");
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            _profile = CreateInstance<VolumeProfile>();
            _bloom = _profile.Add<Bloom>(true);
            _bloom.intensity.Override(_rig.bloomIntensity);
            _bloom.threshold.Override(_rig.bloomThreshold);
            _bloom.scatter.Override(0.72f);
            Tonemapping tonemapping = _profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.Neutral);
            volume.sharedProfile = _profile;

            _groundMaterial = new Material(Shader.Find("Hidden/Geumeum/BossScratchPreviewGround"));
            AddPrimitive("Ground", PrimitiveType.Cube, new Vector3(0f, -0.2f, 6f),
                new Vector3(90f, 0.4f, 90f), _groundMaterial);

            Material figure = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            figure.SetColor("_BaseColor", new Color(0.035f, 0.033f, 0.038f));
            figure.SetFloat("_Smoothness", 0.15f);
            AddPrimitive("Boss stand-in (2.6 m)", PrimitiveType.Capsule, new Vector3(0f, 1.3f, 0f),
                new Vector3(1.1f, 1.3f, 1.1f), figure);
            AddPrimitive("Target A", PrimitiveType.Capsule, new Vector3(-5.2f, 0.9f, -1.6f),
                new Vector3(0.7f, 0.9f, 0.7f), figure);
            AddPrimitive("Target B", PrimitiveType.Capsule, new Vector3(5.6f, 0.9f, 0.4f),
                new Vector3(0.7f, 0.9f, 0.7f), figure);
            AddPrimitive("Target C", PrimitiveType.Capsule, new Vector3(1.8f, 0.9f, -4.4f),
                new Vector3(0.7f, 0.9f, 0.7f), figure);

            GameObject lightObject = NewObject("Preview key light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.85f;
            light.color = new Color(0.86f, 0.9f, 1f);
            light.cullingMask = 1 << PREVIEW_LAYER;
            light.shadows = LightShadows.None;
            light.transform.rotation = Quaternion.Euler(52f, -28f, 0f);

            ApplyRig(_rig);
        }

        private void ApplyRig(Rig rig)
        {
            _rig = rig;
            _camera.transform.position = rig.cameraPosition;
            _camera.transform.LookAt(rig.lookAt);
            _camera.fieldOfView = rig.fieldOfView;
            if (_bloom != null)
            {
                _bloom.intensity.Override(rig.bloomIntensity);
                _bloom.threshold.Override(rig.bloomThreshold);
            }
            if (_target == null || _target.width != rig.width || _target.height != rig.height)
            {
                if (_target != null) { _target.Release(); DestroyImmediate(_target); }
                _target = new RenderTexture(rig.width, rig.height, 24, RenderTextureFormat.ARGBHalf);
                _target.Create();
            }
            _camera.targetTexture = _target;
        }

        private GameObject NewObject(string objectName)
        {
            GameObject go = new GameObject(objectName);
            go.layer = PREVIEW_LAYER;
            SceneManager.MoveGameObjectToScene(go, _scene);
            return go;
        }

        private void AddPrimitive(string objectName, PrimitiveType type, Vector3 position,
            Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = objectName;
            go.layer = PREVIEW_LAYER;
            SceneManager.MoveGameObjectToScene(go, _scene);
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            DestroyImmediate(go.GetComponent<Collider>());
        }

        private void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (_animate && _effect != null)
            {
                _time += (float)(now - _lastUpdate);
                if (_time > _effect.Duration + 0.5f) _time = 0f;
            }
            _lastUpdate = now;
            if (_camera != null) Repaint();
        }

        private void OnGUI()
        {
            Setup();
            EditorGUILayout.LabelField("두 번 교차하는 지면 할퀴기 · 로컬 +Z가 공격 방향", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _animate = GUILayout.Toggle(_animate, "자동 재생", "Button");
            _postProcessing = GUILayout.Toggle(_postProcessing, "미리보기 후처리", "Button");
            if (GUILayout.Button("프리팹 선택"))
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            EditorGUILayout.EndHorizontal();
            EditorGUI.BeginChangeCheck();
            _time = EditorGUILayout.Slider("재생 시각", _time, 0f, _effect.Duration + 0.1f);
            if (EditorGUI.EndChangeCheck()) _animate = false;
            RenderAt(_time);
            Rect rect = GUILayoutUtility.GetAspectRect(_rig.width / (float)_rig.height);
            GUI.DrawTexture(rect, _target, ScaleMode.ScaleToFit, false);
            EditorGUILayout.HelpBox(
                "캡슐은 크기 기준입니다. 실제 보스 모션·판정 연결은 포함하지 않습니다. 기존 게임 씬과 Volume은 변경하지 않습니다.",
                MessageType.Info);
        }

        private void RenderAt(float seconds)
        {
            _effect.Sample(seconds);
            _camera.GetUniversalAdditionalCameraData().renderPostProcessing = _postProcessing;
            _camera.Render();
        }

        /// <summary>참조 영상과 같은 해상도·화각으로 지정한 시각들을 캡처한다.</summary>
        public static string Capture(string folder, float[] times, Rig rig, string prefix)
        {
            BossScratchPreview window = GetWindow<BossScratchPreview>("Boss Double Scratch");
            window.Setup();
            window._animate = false;
            window.ApplyRig(rig);
            Directory.CreateDirectory(folder);

            RenderTexture previous = RenderTexture.active;
            Texture2D image = new Texture2D(rig.width, rig.height, TextureFormat.RGB24, false);
            try
            {
                for (int i = 0; i < times.Length; i++)
                {
                    window.RenderAt(times[i]);
                    RenderTexture.active = window._target;
                    image.ReadPixels(new Rect(0, 0, rig.width, rig.height), 0, 0);
                    image.Apply();
                    File.WriteAllBytes(Path.Combine(folder, prefix + i.ToString("D3") + ".png"),
                        image.EncodeToPNG());
                }
            }
            finally
            {
                RenderTexture.active = previous;
                DestroyImmediate(image);
            }
            window._time = 0.15f;
            return folder + " (" + times.Length + " frames)";
        }

        /// <summary>외부 도구에서 리플렉션으로 부르기 쉬운 평면 인자 캡처 진입점.</summary>
        public static string CaptureFlat(string folder, string prefix, float[] times,
            float camX, float camY, float camZ, float lookX, float lookY, float lookZ,
            float fov, float bloomIntensity, float bloomThreshold, int width, int height,
            float effectX, float effectY, float effectZ, float effectYaw,
            string[] overrideNames, float[] overrideValues)
        {
            BossScratchPreview w = GetWindow<BossScratchPreview>("Boss Double Scratch");
            w.Setup();
            w._effect.transform.position = new Vector3(effectX, effectY, effectZ);
            w._effect.transform.rotation = Quaternion.Euler(0f, effectYaw, 0f);
            if (overrideNames != null)
            {
                SerializedObject so = new SerializedObject(w._effect);
                for (int i = 0; i < overrideNames.Length; i++)
                {
                    SerializedProperty p = so.FindProperty(overrideNames[i]);
                    if (p == null) continue;
                    if (p.propertyType == SerializedPropertyType.Integer)
                        p.intValue = Mathf.RoundToInt(overrideValues[i]);
                    else p.floatValue = overrideValues[i];
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            Rig rig;
            rig.cameraPosition = new Vector3(camX, camY, camZ);
            rig.lookAt = new Vector3(lookX, lookY, lookZ);
            rig.fieldOfView = fov;
            rig.bloomIntensity = bloomIntensity;
            rig.bloomThreshold = bloomThreshold;
            rig.width = width;
            rig.height = height;
            return Capture(folder, times, rig, prefix);
        }

        public static string CaptureSequence(string folder, float start, float end, float fps, Rig rig)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt((end - start) * fps) + 1);
            float[] times = new float[count];
            for (int i = 0; i < count; i++) times[i] = start + i / fps;
            return Capture(folder, times, rig, "frame-");
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            if (_scene.IsValid()) EditorSceneManager.ClosePreviewScene(_scene);
            if (_target != null) { _target.Release(); DestroyImmediate(_target); }
            if (_groundMaterial != null) DestroyImmediate(_groundMaterial);
            if (_profile != null)
            {
                foreach (VolumeComponent component in _profile.components) DestroyImmediate(component);
                DestroyImmediate(_profile);
            }
        }
    }
}
