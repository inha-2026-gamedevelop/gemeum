// Unity
using UnityEngine;

namespace Geumeum.Systems
{
    /// <summary>
    /// 네트워크·RunManager 없이 BarrierJudge 보간·판정만 눈으로 확인하는 독립 테스트 도구.
    /// Play만 누르면 인스펙터 값대로 반경이 줄어드는 걸 Scene 뷰에서 바로 볼 수 있다.
    /// BarrierController와 무관하고, 실제 결계 판정에는 전혀 관여하지 않는다 — 확인 끝나면 씬에서 빼도 된다
    /// </summary>
    public class BarrierJudgeGizmoPreview : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("전환 값")]
        [SerializeField] private Vector3 _fromCenter     = Vector3.zero;
        [SerializeField] private Vector3 _targetCenter   = Vector3.zero;
        [SerializeField] private float   _fromRadius     = 50f;
        [SerializeField] private float   _targetRadius   = 20f;
        [SerializeField] private float   _shrinkDuration = 5f; // 확인용이라 실제 30초보다 짧게 기본값을 둔다

        [Header("판정 확인용")]
        [SerializeField] private Transform _probe = null; // 비워두면 이 오브젝트 자신의 위치로 판정

        [Header("기둥 시각화 (선택, Game 뷰에서도 보임)")]
        [SerializeField] private bool     _showPillar     = true;
        [SerializeField] private float    _pillarHeight   = 60f;
        [SerializeField] private Material _pillarMaterial = null; // 비워두면 파이프라인 무관하게 항상 반투명으로 그려지는 확인용 머티리얼을 만든다. 정식 아트 머티리얼이 아니다

        private const int   PILLAR_SEGMENTS     = 48;    // 관 옆면 분할 수, 확인용이라 정밀도보다 가벼움을 우선한다
        private const int   LOG_INTERVAL_FRAMES = 30;    // 매 프레임 로그는 스팸이라 0.5초 간격 정도로만
        private const float PROBE_GIZMO_RADIUS  = 0.5f;  // 판정 확인용 구슬 크기(m)
        private const float MIN_VISUAL_RADIUS   = 0.01f; // 0 스케일은 Unity가 경고를 뱉으니 최소값으로 클램프(m)

        private float     _startTime;
        private Transform _pillarTransform;
        private Mesh      _runtimePillarMesh;     // 옆면만 있는 관 메쉬, OnDisable에서 직접 해제해야 하는 대상
        private Material  _runtimePillarMaterial; // BuildFallbackMaterial이 만들었을 때만 채워지고, OnDisable에서 직접 해제해야 하는 대상

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            _startTime = Time.time;

            if (_showPillar && Application.isPlaying)
            {
                CreatePillar();
            }
        }

        private void OnDisable()
        {
            if (_pillarTransform != null)
            {
                Destroy(_pillarTransform.gameObject);
                _pillarTransform = null;
            }

            if (_runtimePillarMesh != null)
            {
                Destroy(_runtimePillarMesh);
                _runtimePillarMesh = null;
            }

            if (_runtimePillarMaterial != null)
            {
                Destroy(_runtimePillarMaterial);
                _runtimePillarMaterial = null;
            }
        }

        // 컨텍스트 메뉴로 다시 눌러 축소를 처음부터 재생할 수 있다
        [ContextMenu("Restart Shrink")]
        private void RestartShrink()
        {
            _startTime = Time.time;
        }

        private void Update()
        {
            UpdatePillar();

            if (Time.frameCount % LOG_INTERVAL_FRAMES != 0)
            {
                return;
            }

            Vector3 probePosition = (_probe != null) ? _probe.position : transform.position;
            // Debug.Log($"[BarrierJudgeGizmoPreview] radius={CurrentRadius():F2}, outside={IsOutsideNow(probePosition)}");
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            Vector3 center = CurrentCenter();
            float   radius = CurrentRadius();

            Gizmos.color = Color.red;
            DrawWireCircleXZ(center, radius);

            Gizmos.color = new Color(1f, 0.25f, 0.25f, 0.3f);
            DrawWireCircleXZ(_targetCenter, _targetRadius);

            Vector3 probePosition = (_probe != null) ? _probe.position : transform.position;
            Gizmos.color = IsOutsideNow(probePosition) ? Color.red : Color.green;
            Gizmos.DrawSphere(probePosition, PROBE_GIZMO_RADIUS);
        }

        /****************************************
        *                Methods
        ****************************************/

        private BarrierTransitionState BuildState()
        {
            return new BarrierTransitionState
            {
                FromCenter      = _fromCenter,
                TargetCenter    = _targetCenter,
                FromRadius      = _fromRadius,
                TargetRadius    = _targetRadius,
                StartServerTime = 0.0,
                EndServerTime   = _shrinkDuration,
            };
        }

        private double ElapsedSinceStart() => Time.time - _startTime;

        private Vector3 CurrentCenter() => BarrierJudge.GetCenter(BuildState(), ElapsedSinceStart());

        private float CurrentRadius() => BarrierJudge.GetRadius(BuildState(), ElapsedSinceStart());

        private bool IsOutsideNow(Vector3 position) => BarrierJudge.IsOutside(position, BuildState(), ElapsedSinceStart());

        private void CreatePillar()
        {
            GameObject pillar = new GameObject("BarrierPillarPreview (Auto)");
            pillar.hideFlags = HideFlags.DontSave; // 확인용 오브젝트라 씬에 저장되지 않는다

            MeshFilter   meshFilter   = pillar.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = pillar.AddComponent<MeshRenderer>();

            _runtimePillarMesh          = BuildOpenCylinderMesh(PILLAR_SEGMENTS);
            meshFilter.sharedMesh       = _runtimePillarMesh;
            meshRenderer.sharedMaterial = (_pillarMaterial != null) ? _pillarMaterial : BuildFallbackMaterial();

            _pillarTransform = pillar.transform;
        }

        // 위아래 뚜껑 없이 옆면만 있는 관 메쉬. 반지름 0.5·높이 -1~1로 만들어
        // CreatePrimitive(Cylinder)와 같은 단위 크기를 쓰고, UpdatePillar의 스케일 계산을 그대로 재사용한다.
        // Sprites/Default처럼 Cull Off인 셰이더를 쓰면 안팎 어디서 봐도 보인다
        private static Mesh BuildOpenCylinderMesh(int segments)
        {
            Vector3[] vertices  = new Vector3[(segments + 1) * 2];
            Vector2[] uvs       = new Vector2[vertices.Length];
            int[]     triangles = new int[segments * 6];

            for (int i = 0; i <= segments; ++i)
            {
                float angle = (i / (float) segments) * Mathf.PI * 2f;
                float x     = Mathf.Cos(angle) * 0.5f;
                float z     = Mathf.Sin(angle) * 0.5f;

                int bottomIndex = i * 2;
                int topIndex    = bottomIndex + 1;

                vertices[bottomIndex] = new Vector3(x, -1f, z);
                vertices[topIndex]    = new Vector3(x, 1f, z);

                float u = i / (float) segments;
                uvs[bottomIndex] = new Vector2(u, 0f);
                uvs[topIndex]    = new Vector2(u, 1f);
            }

            for (int i = 0; i < segments; ++i)
            {
                int bottomA = i * 2;
                int topA    = bottomA + 1;
                int bottomB = (i + 1) * 2;
                int topB    = bottomB + 1;

                int triIndex = i * 6;
                triangles[triIndex]     = bottomA;
                triangles[triIndex + 1] = topA;
                triangles[triIndex + 2] = bottomB;

                triangles[triIndex + 3] = topA;
                triangles[triIndex + 4] = topB;
                triangles[triIndex + 5] = bottomB;
            }

            Mesh mesh = new Mesh { name = "BarrierPillarPreview_OpenCylinder" };
            mesh.vertices  = vertices;
            mesh.uv        = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        // 파이프라인(빌트인/URP)을 가리지 않고 항상 반투명으로 그려지는 확인용 셰이더다.
        // 실제 결계 셰이더는 05.Art의 BarrierVisual이 담당하고, 이건 그 전 단계의 눈검증용
        private Material BuildFallbackMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default");
            _runtimePillarMaterial = new Material(shader)
            {
                color = new Color(1f, 0.15f, 0.15f, 0.25f),
            };

            return _runtimePillarMaterial;
        }

        private void UpdatePillar()
        {
            if (_pillarTransform == null)
            {
                return;
            }

            Vector3 center = CurrentCenter();
            float   radius = Mathf.Max(CurrentRadius(), MIN_VISUAL_RADIUS);

            // 기본 Cylinder 프리미티브는 지름 1m, 높이 2m다 — 반경·원하는 높이에 맞춰 스케일한다
            _pillarTransform.position   = center + new Vector3(0f, _pillarHeight * 0.5f, 0f);
            _pillarTransform.localScale = new Vector3(radius * 2f, _pillarHeight * 0.5f, radius * 2f);
        }

        private static void DrawWireCircleXZ(Vector3 center, float radius, int segments = 64)
        {
            if (radius <= 0f)
            {
                return;
            }

            Vector3 prev = center + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; ++i)
            {
                float   angle = (i / (float) segments) * Mathf.PI * 2f;
                Vector3 next  = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
