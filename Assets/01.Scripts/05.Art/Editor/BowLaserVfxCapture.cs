#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Geumeum.Art.Editor
{
    /// <summary>원본 56–62초와 비교할 고정 시간 샘플을 저장한다. 실제 전투 검증과는 별도다.</summary>
    [InitializeOnLoad]
    public static class BowLaserVfxCapture
    {
        const int Layer = 30;
        static Scene _scene;
        static GameObject _root, _actor;
        static Camera _camera;
        static RenderTexture _target;
        static Texture2D _readback;
        static BowLaserVfxView _beam;
        static BowChargeVfxView _charge;
        static AnimationClip _hold, _release;
        static readonly List<UnityEngine.Object> Owned = new List<UnityEngine.Object>();
        static string _folder;
        static int _frame, _fps, _total, _firstFrame;
        static bool _oldAsync;
        static bool _oldBackground;
        static bool _gameAngle;
        public static string Status { get; private set; } = "Idle";
        public static bool IsCapturing { get; private set; }
        public static float FirstShot = 1.70f;
        public static float SecondShot = 4.20f;
        public static float BeamDistance = 12f;
        public static float CameraSize = 6f;

        static BowLaserVfxCapture()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Cancel;
            EditorApplication.quitting += Cancel;
        }

        public static string Begin(string folder, int fps = 60, int width = 1280, int height = 720, bool gameAngle = false)
        {
            if (IsCapturing)
            {
                throw new InvalidOperationException("Capture is already running.");
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Use this isolated preview capture outside Play Mode.");
            }

            _folder = Path.GetFullPath(folder);
            _gameAngle = gameAngle;
            Directory.CreateDirectory(_folder);
            _fps = Mathf.Clamp(fps, 1, 120);
            _total = _fps * 6;
            _frame = 0;
            _firstFrame = 0;
            _oldAsync = ShaderUtil.allowAsyncCompilation;
            _oldBackground = Application.runInBackground;
            ShaderUtil.allowAsyncCompilation = false;
            Application.runInBackground = true;
            try
            {
                Setup(width, height, gameAngle);
                IsCapturing = true;
                Status = "Capturing 0/" + _total;
                EditorApplication.update += Tick;
                return Status;
            }
            catch
            {
                Cleanup();
                throw;
            }
        }

        public static string CaptureSingle(string folder, float time, int width = 1280, int height = 720, bool gameAngle = false)
        {
            Begin(folder, 60, width, height, gameAngle);
            _frame = Mathf.Max(0, Mathf.RoundToInt(time * _fps));
            _firstFrame = _frame;
            _total = _frame + 1;
            Status = "Capturing sample at " + time.ToString("F3") + " seconds";
            return Status;
        }

        static GameObject Child(string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave, layer = Layer };
            SceneManager.MoveGameObjectToScene(go, _scene);
            go.transform.SetParent(_root.transform, false);
            return go;
        }

        static void Setup(int width, int height, bool gameAngle)
        {
            _scene = EditorSceneManager.NewPreviewScene();
            _root = new GameObject("Bow VFX capture (temporary)") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(_root, _scene);
            _camera = Child("Capture camera").AddComponent<Camera>();
            _camera.scene = _scene;
            _camera.enabled = false;
            _camera.cullingMask = 1 << Layer;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(.003f, .003f, .004f);
            _camera.orthographic = false;
            _camera.fieldOfView = gameAngle ? 60f : 23.7f;
            _camera.orthographicSize = gameAngle ? 7.5f : CameraSize;
            Vector3 focus = gameAngle ? new Vector3(3.5f, 1.25f, 0) : new Vector3(5.1f, 1.25f, 0);
            _camera.transform.position = focus + (gameAngle ? new Vector3(-7, 12, -12).normalized * 9f : new Vector3(-25f, 4f, -14));
            _camera.transform.LookAt(focus);
            _camera.nearClipPlane = .1f;
            _camera.farClipPlane = 100;
            _camera.allowHDR = true;
            var cameraData = _camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.volumeLayerMask = 1 << Layer;
            _target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf) { hideFlags = HideFlags.HideAndDontSave };
            _target.Create();
            _camera.targetTexture = _target;
            _readback = new Texture2D(width, height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Owned.Add(profile);
            var bloom = profile.Add<Bloom>(true);
            Owned.Add(bloom);
            bloom.intensity.Override(.8f);
            bloom.threshold.Override(.8f);
            bloom.scatter.Override(.65f);
            var volume = Child("Capture bloom").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1000;
            volume.sharedProfile = profile;
            var key = Child("Capture key light").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = .4f;
            key.color = new Color(.75f, .8f, 1f);
            key.cullingMask = 1 << Layer;
            key.transform.rotation = Quaternion.Euler(55, -30, 0);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            SceneManager.MoveGameObjectToScene(ground, _scene);
            ground.transform.SetParent(_root.transform, false);
            ground.transform.localScale = Vector3.one * 10;
            UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());
            var floorFilter = ground.GetComponent<MeshFilter>();
            var floorMesh = UnityEngine.Object.Instantiate(floorFilter.sharedMesh);
            var floorVertices = floorMesh.vertices;
            var floorUv = new Vector2[floorVertices.Length];
            for (int i = 0; i < floorUv.Length; i++)
            {
                floorUv[i] = new Vector2(floorVertices[i].x * .1f + .5f, floorVertices[i].z * .1f + .5f);
            }

            floorMesh.uv = floorUv;
            floorFilter.sharedMesh = floorMesh;
            Owned.Add(floorMesh);
            const int floorResolution = 1024;
            var checker = new Texture2D(floorResolution, floorResolution, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[floorResolution * floorResolution];
            Vector3 floorForward = new Vector3(25, 0, 14).normalized;
            for (int y = 0; y < floorResolution; y++)
            {
                for (int x = 0; x < floorResolution; x++)
            {
                float wx = -50f + 100f * x / (floorResolution - 1);
                float wz = -50f + 100f * y / (floorResolution - 1);
                float value = ((Mathf.FloorToInt(wx) + Mathf.FloorToInt(wz)) & 1) == 0 ? .4f : .55f;
                float distance = Vector3.Dot(new Vector3(wx, 0, wz), floorForward);
                float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6f, 30f, distance));
                pixels[y * floorResolution + x] = new Color(value * fade, value * fade, value * fade, 1);
            }
            }

            checker.SetPixels(pixels);
            checker.Apply();
            Owned.Add(checker);
            var groundMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            groundMat.SetTexture("_BaseMap", checker);
            groundMat.SetTextureScale("_BaseMap", Vector2.one);
            // PreviewScene에서는 환경광이 없어도 바닥 명암을 유지하고 실제 VFX 점광원은 그대로 받는다.
            groundMat.EnableKeyword("_EMISSION");
            groundMat.SetTexture("_EmissionMap", checker);
            groundMat.SetTextureScale("_EmissionMap", Vector2.one);
            groundMat.SetColor("_EmissionColor", new Color(.07f, .07f, .07f));
            groundMat.SetFloat("_Smoothness", 0);
            Owned.Add(groundMat);
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;

            _actor = InstantiateAsset("Assets/02.Prefabs/Art/Player/Singung_Art.prefab");
            _actor.transform.rotation = Quaternion.LookRotation(Vector3.right);
            foreach (var behaviour in _actor.GetComponentsInChildren<MonoBehaviour>(true))
            {
                behaviour.enabled = false;
            }

            foreach (var renderer in _actor.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null || !materials[i].HasProperty("_EmissionColor"))
                    {
                        continue;
                    }

                    var copy = new Material(materials[i]);
                    Owned.Add(copy);
                    copy.EnableKeyword("_EMISSION");
                    if (copy.HasProperty("_BaseMap"))
                    {
                        copy.SetTexture("_EmissionMap", copy.GetTexture("_BaseMap"));
                    }

                    Color tint = copy.HasProperty("_BaseColor") ? copy.GetColor("_BaseColor") : Color.white;
                    copy.SetColor("_EmissionColor", tint * .045f);
                    materials[i] = copy;
                }
                renderer.sharedMaterials = materials;
            }
            _hold = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/04.Animations/Bow_Hold.anim");
            _release = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/04.Animations/Bow_Release.anim");
            if (_hold != null)
            {
                _hold.SampleAnimation(_actor, 0);
            }

            var animator = _actor.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                if (hand != null)
                {
                    var bow = InstantiateAsset("Assets/06.Model/Weapon/Bow/SM_Bow.fbx");
                    bow.transform.SetParent(hand, false);
                    bow.transform.localRotation = Quaternion.Euler(0, 0, 90);
                }
            }
            _beam = InstantiateAsset("Assets/02.Prefabs/Art/VFX/Player_BowLaserRelease.prefab").GetComponent<BowLaserVfxView>();
            _charge = InstantiateAsset("Assets/02.Prefabs/Art/VFX/Player_BowCharge.prefab").GetComponent<BowChargeVfxView>();
            if (_beam == null || _charge == null)
            {
                throw new InvalidOperationException("Run BowLaserVfxBuilder.Build first.");
            }

            Vector3 muzzle = new Vector3(.55f, 1.15f, 0);
            _beam.transform.SetPositionAndRotation(muzzle, Quaternion.LookRotation(Vector3.right));
            _charge.transform.SetPositionAndRotation(muzzle, Quaternion.LookRotation(Vector3.right));
            foreach (Transform t in _root.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = Layer;
                t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        static GameObject InstantiateAsset(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                throw new FileNotFoundException(path);
            }

            var instance = UnityEngine.Object.Instantiate(asset, _root.transform);
            return instance;
        }

        static void Tick()
        {
            if (!IsCapturing)
            {
                return;
            }

            try
            {
                float t = _frame / (float)_fps;
                float age = t >= SecondShot ? t - SecondShot : t - FirstShot;
                bool firing = age >= 0 && age < _beam.Duration;
                _beam.gameObject.SetActive(firing);
                if (firing)
                {
                    // 발사별 고정 시드로 재촬영 결과를 유지하면서 두 발의 세부 모양을 구분한다.
                    _beam.SetVariation(t >= SecondShot ? 0x8F34A21Du : 0x31C6E907u);
                    _beam.Evaluate(age, BeamDistance);
                }
                float next = t < FirstShot ? FirstShot : SecondShot;
                float chargeAge = t - (next - .45f);
                bool charging = chargeAge >= 0 && t < next;
                _charge.gameObject.SetActive(charging);
                if (charging)
                {
                    _charge.Evaluate(Mathf.Clamp01(chargeAge / .45f), chargeAge);
                }

                if (firing && _release != null)
                {
                    _release.SampleAnimation(_actor, Mathf.Min(age, _release.length));
                }
                else if (_hold != null)
                {
                    _hold.SampleAnimation(_actor, Mathf.Repeat(t, Mathf.Max(.01f, _hold.length)));
                }

                _actor.transform.position = Vector3.zero;
                _actor.transform.rotation = Quaternion.LookRotation(Vector3.right);
                var previous = RenderTexture.active;
                try
                {
                    _camera.Render();
                    RenderTexture.active = _target;
                    _readback.ReadPixels(new Rect(0, 0, _target.width, _target.height), 0, 0);
                    _readback.Apply(false);
                    File.WriteAllBytes(Path.Combine(_folder, "frame_" + _frame.ToString("D4") + ".png"), _readback.EncodeToPNG());
                }
                finally { RenderTexture.active = previous; }
                ++_frame;
                Status = "Capturing " + _frame + "/" + _total;
                if (_frame >= _total)
                {
                    File.WriteAllText(Path.Combine(_folder, "capture.txt"), "Isolated Unity preview, not gameplay capture.\nCamera=" + (_gameAngle ? "Approximate game angle preview (60 degree FOV, 9 metre distance)" : "Reference angle preview") + "\nReference 56–62 seconds.\nFPS=" + _fps + "\nFirstFrame=" + _firstFrame + "\nFrames=" + (_total - _firstFrame) + "\nFirstShot=" + FirstShot + "\nSecondShot=" + SecondShot + "\n");
                    Status = "Complete: " + _folder + " (" + (_total - _firstFrame) + " frames)";
                    Cleanup();
                }
            }
            catch (Exception ex)
            {
                Status = "Failed: " + ex;
                Cleanup();
                Debug.LogException(ex);
            }
        }

        public static void Cancel()
        {
            if (!IsCapturing && _root == null)
            {
                return;
            }

            Status = "Cancelled after " + _frame + " frames";
            Cleanup();
        }

        static void Cleanup()
        {
            EditorApplication.update -= Tick;
            IsCapturing = false;
            if (_camera != null)
            {
                _camera.targetTexture = null;
            }

            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }

            if (_scene.IsValid())
            {
                EditorSceneManager.ClosePreviewScene(_scene);
            }

            if (_target != null) { _target.Release(); UnityEngine.Object.DestroyImmediate(_target); }
            if (_readback != null)
            {
                UnityEngine.Object.DestroyImmediate(_readback);
            }

            foreach (var item in Owned)
            {
                if (item != null)
                {
                    UnityEngine.Object.DestroyImmediate(item);
                }
            }

            Owned.Clear();
            ShaderUtil.allowAsyncCompilation = _oldAsync;
            Application.runInBackground = _oldBackground;
            _root = null;
        }
    }
}
#endif
