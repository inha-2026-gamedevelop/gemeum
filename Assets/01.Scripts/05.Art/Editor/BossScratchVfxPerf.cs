using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.VFX;

namespace Geumeum.Art.Editor
{
    /// <summary>아레나 실측 성능 측정. 효과 없음 / 1개 / 3개를 같은 전투 카메라에서 비교한다.</summary>
    public sealed class BossScratchVfxPerfRunner : MonoBehaviour
    {
        public string reportPath = "D:/reference/BossScratch/v3/perf.txt";
        public int renderSamples = 90;
        public int width = 1280;
        public int height = 720;
        public Vector3 playerPosition = new Vector3(0f, 0f, -3.5f);
        public Vector3 effectPosition = Vector3.zero;
        public float cameraPitch = 22f;
        public float pivotHeight = 1.6f;
        public float sideOffset = 0.6f;
        public float lockOnDistance = 9f;
        public float fieldOfView = 60f;
        public float peakDelay = 0.05f;

        private Camera _camera;
        private RenderTexture _target;
        private Texture2D _probe;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        private void Start() { StartCoroutine(Run()); }

        private IEnumerator Run()
        {
            var report = new System.Text.StringBuilder();
            int previousCapture = Time.captureFramerate;
            Time.captureFramerate = 120;

            GameObject camGo = new GameObject("__ScratchPerfCam");
            camGo.hideFlags = HideFlags.HideAndDontSave;
            _camera = camGo.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.allowHDR = true;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 600f;
            _camera.clearFlags = CameraClearFlags.Skybox;
            _camera.cullingMask = ~0;
            UniversalAdditionalCameraData data = _camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.volumeLayerMask = ~0;
            _target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf);
            _target.Create();
            _camera.targetTexture = _target;

            Vector3 toBoss = effectPosition - playerPosition;
            toBoss.y = 0f;
            float yaw = toBoss.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toBoss).eulerAngles.y : 0f;
            Quaternion rot = Quaternion.Euler(cameraPitch, yaw, 0f);
            Vector3 pivot = playerPosition + (Vector3.up * pivotHeight) + ((rot * Vector3.right) * sideOffset);
            _camera.transform.position = pivot + (-(rot * Vector3.forward) * lockOnDistance);
            _camera.transform.rotation = rot;
            _camera.fieldOfView = fieldOfView;
            _probe = new Texture2D(1, 1, TextureFormat.RGBA32, false);

            for (int warm = 0; warm < 6; warm++) { _camera.Render(); yield return null; }

            report.AppendLine("해상도 " + width + "x" + height + " / 전투 카메라 pitch " + cameraPitch
                + " FOV " + fieldOfView + " 락온 " + lockOnDistance + "m / Boss_Arena 전역 Volume 사용");
            report.AppendLine("표본 " + renderSamples + "회 즉시 렌더, 매회 1x1 ReadPixels로 GPU 동기화");
            report.AppendLine("효과는 타격 +" + peakDelay.ToString("F2") + "초(정점)에서 일시정지해 같은 상태를 반복 렌더");
            report.AppendLine();

            // 첫 회차는 셰이더·PSO 준비가 섞이므로 두 번 돌고 2회차를 읽는다
            int[] counts = { 0, 1, 3, 0, 1, 3 };
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/02.Prefabs/Art/VFX/Boss_ScratchVFX.prefab");

            foreach (int n in counts)
            {
                var drivers = new List<BossScratchVfxDriver>();
                var made = new List<GameObject>();
                for (int i = 0; i < n; i++)
                {
                    GameObject inst = Instantiate(prefab);
                    inst.hideFlags = HideFlags.HideAndDontSave;
                    inst.transform.position = effectPosition + new Vector3((i - 1) * 2.2f, 0f, (i - 1) * 1.1f);
                    inst.transform.rotation = Quaternion.Euler(0f, 180f + (i * 40f), 0f);
                    drivers.Add(inst.GetComponent<BossScratchVfxDriver>());
                    made.Add(inst);
                }
                foreach (BossScratchVfxDriver d0 in drivers)
                {
                    d0.Play();
                }
                int waitFrames = Mathf.CeilToInt(peakDelay * 120f) + 2;
                for (int f = 0; f < waitFrames; f++)
                {
                    yield return null;
                }

                int alive = 0;
                foreach (BossScratchVfxDriver d0 in drivers)
                {
                    VisualEffect ve = d0.GetComponent<VisualEffect>();
                    ve.pause = true;
                    var names = new List<string>();
                    ve.GetSystemNames(names);
                    foreach (string nm in names)
                    {
                        alive += (int)ve.GetParticleSystemInfo(nm).aliveCount;
                    }
                }
                yield return new WaitForEndOfFrame();

                RenderOnce();
                var sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                for (int s = 0; s < renderSamples; s++)
                {
                    RenderOnce();
                }
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds / renderSamples;

                report.AppendLine("효과 " + n + "개 : " + ms.ToString("F3") + " ms/frame"
                    + " | 입자 " + alive
                    + " | drawCalls " + UnityStats.drawCalls
                    + " | setPass " + UnityStats.setPassCalls
                    + " | dynamicBatched " + UnityStats.dynamicBatchedDrawCalls
                    + " | triangles " + UnityStats.triangles);

                foreach (GameObject go in made)
                {
                    if (go != null)
                    {
                        DestroyImmediate(go);
                    }
                }
                for (int f = 0; f < 4; f++)
                {
                    yield return null;
                }
            }

            Time.captureFramerate = previousCapture;
            _camera.targetTexture = null;
            _target.Release();
            DestroyImmediate(_target);
            DestroyImmediate(_probe);
            DestroyImmediate(camGo);
            foreach (GameObject go in _spawned)
            {
                if (go != null)
                {
                    DestroyImmediate(go);
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, report.ToString());
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(reportPath), "_perfdone.txt"), "done");
            DestroyImmediate(gameObject);
        }

        private void RenderOnce()
        {
            _camera.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = _target;
            _probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            _probe.Apply();
            RenderTexture.active = prev;
        }
    }

    /// <summary>성능 측정 진입점.</summary>
    public static class BossScratchVfxPerf
    {
        public static string Begin(string reportPath, int samples, int width, int height, float pitch)
        {
            if (!Application.isPlaying)
            {
                return "not playing";
            }
            string dir = Path.GetDirectoryName(reportPath);
            Directory.CreateDirectory(dir);
            string marker = Path.Combine(dir, "_perfdone.txt");
            if (File.Exists(marker))
            {
                File.Delete(marker);
            }
            GameObject go = new GameObject("__ScratchPerf");
            go.hideFlags = HideFlags.HideAndDontSave;
            BossScratchVfxPerfRunner r = go.AddComponent<BossScratchVfxPerfRunner>();
            r.reportPath = reportPath;
            r.renderSamples = samples;
            r.width = width;
            r.height = height;
            r.cameraPitch = pitch;
            return "perf started";
        }
    }
}
