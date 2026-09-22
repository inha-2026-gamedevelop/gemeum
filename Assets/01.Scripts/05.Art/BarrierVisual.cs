// Unity
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Art
{
    /// <summary>
    /// 결계 벽의 런타임 표현. 원통 셸 하나와 둘레를 도는 파티클 패치들을 배치한다
    /// 판정은 하지 않고 IBarrierState의 Center, Radius만 읽는다
    /// IsActive는 SyncVar 파생값이 아니라 서버 전용 필드에서 나오므로 여기서 쓰지 않는다 -
    /// 쓰면 호스트 화면에만 결계가 보이고 원격 클라이언트 둘은 아무것도 못 본다
    /// </summary>
    public class BarrierVisual : MonoBehaviour
    {
        /****************************************
        *                Constants
        ****************************************/

        // 반경, 중심이 이만큼 안 움직였으면 재배치를 건너뛴다. 축소가 멈춘 구간에서
        // 매 프레임 지형 레이캐스트를 반복하지 않게 하는 값이다
        private const float RADIUS_EPSILON     = 0.05f;
        private const float CENTER_EPSILON_SQR = 0.01f;

        // 구매 그래프가 노출한 유일한 프로퍼티 이름. 그래프를 고치지 않고 밝기를 누를 수 있는 창구다
        private const string EFFECT_ALPHA_PROPERTY = "Alpha";

        /****************************************
        *                Fields
        ****************************************/

        [Header("셸")]
        [SerializeField] private Material _wallMaterial = null;               // M_BarrierWall
        [SerializeField] private float    _wallHeight   = 120f;               // 결계 벽 높이(m)
        [SerializeField] private int      _wallSegments = 48;                 // 원통 둘레 분할 수
        [SerializeField] private float    _groundSink   = 30f;                // 능선에서 벽이 뜨지 않게 바닥을 이만큼 파묻는다

        // 뒷면을 자르는 셰이더(구매 에셋 Bubble Standard Shield 등)를 쓸 때만 켠다
        // 그런 셰이더는 원통 안쪽에서 보면 전부 뒷면이라 화면에 아무것도 안 나온다 -
        // 안쪽을 향하는 면을 한 벌 더 깔아야 보인다. 자체 셰이더는 Cull Off라 켜면 두 번 그려져 밝아진다
        [SerializeField] private bool     _doubleSidedShell = false;

        // 구매 그래프는 노출 프로퍼티가 Alpha 하나뿐이라 코드로 개별 시스템을 끌 수 없다
        // 대신 원하는 요소만 담고 있는 그래프를 골라 층을 나눈다 - 실측 구성은 아래와 같다
        //   Bubble 1  : Minimal 1000 하나뿐        -> 바닥에 깔리는 발광 판
        //   Bubble 12 : System  900 하나뿐         -> 검정, 보라 연기 본체
        //   Bubble 6  : 위 둘 + Strip 6000 + Trailing Sparks + Heads -> 확대하면 난잡해진다
        [Header("파티클 — 공통")]
        [SerializeField] private int   _patchCount     = 56;                  // 한 행에 배치할 개수
        [SerializeField] private bool  _autoFitWidth   = true;                // 가로 크기를 둘레 / 개수로 역산한다
        [SerializeField] private float _patchWidth     = 14f;                 // _autoFitWidth가 꺼졌을 때만 쓴다
        [SerializeField] private float _patchThickness = 6f;                  // 벽 두께 방향(반경 방향) 크기

        [Header("파티클 — 바닥")]
        [SerializeField] private VisualEffectAsset _groundEffectAsset = null; // Bubble 1.vfx
        [SerializeField] private bool              _showGroundLayer   = true;
        [SerializeField] private float             _groundLayerHeight = 12f;

        [Header("파티클 — 본체")]
        [SerializeField] private VisualEffectAsset _bodyEffectAsset = null;   // Bubble 12.vfx
        [SerializeField] private int               _bodyRows        = 2;
        [SerializeField] private float             _bodyHeight      = 30f;
        // 행 간격. _bodyHeight보다 작게 두면 행이 겹쳐 띠가 얇아 보인다
        // 같게 두면 두 행이 위아래로 딱 붙어 벽이 두 겹으로 두껍게 보인다 (2026-09-09 사용자 지적)
        [SerializeField] private float             _rowSpacing      = 14f;

        // 결계는 런 내내(수십 분) 화면 가장자리를 채우고 있는 표현이다. 원본 밝기 그대로 두면
        // 눈이 먼저 지친다. 구매 그래프가 노출한 Alpha 하나로 전체 발광을 눌러 둔다
        // 절대값이 아니라 배율이다 - Bubble 6의 그래프 기본값은 0.116이고, 절대값으로 넣으면
        // 제작자가 잡아 둔 밝기를 모르고 덮어써 오히려 밝아진다
        [Header("눈 피로 완화")]
        [SerializeField, Range(0f, 1f)] private float _effectAlphaScale = 0.6f;

        // 파티클 시뮬레이션 재생 속도. 끊임없이 흐르는 움직임이 밝기만큼이나 눈을 피곤하게 한다
        // 1이 원속도이고, 낮출수록 같은 그림이 오래 머물러 화면이 잔잔해진다
        [SerializeField, Range(0.01f, 1f)] private float _effectPlayRate = 0.06f;

        [Header("지형")]
        // 지형 높이는 물리 레이캐스트가 아니라 Terrain.SampleHeight로 재는 것이 기본이다
        // Dev_Barrier의 나무, 바위, WeatherMaker가 전부 Default 레이어라, 레이캐스트로 재면
        // 그것들을 바닥으로 오인해 패치가 하늘에 뜬다 (2026-09-09 관찰: 패치 y 117~154, 실제 지형 40~80)
        [SerializeField] private bool      _useTerrainHeight       = true;
        [SerializeField] private LayerMask _groundMask             = ~0;      // 지형이 없을 때만 쓰는 폴백. Ground 레이어로 좁혀 둘 것
        [SerializeField] private float     _groundProbeHeight      = 500f;    // 폴백 레이캐스트를 이 높이에서 아래로 쏜다
        [SerializeField] private float     _groundResampleDistance = 2f;      // 패치가 이만큼 움직여야 높이를 다시 잰다

        private Transform      _wallTransform  = null;
        private Mesh           _wallMesh       = null;
        private Transform[]    _patchTransform = null;
        private VisualEffect[] _patchEffect    = null;

        // 패치별 지형 높이 캐시. 결계가 정지해 있는 동안에는 레이캐스트가 0회다
        private Vector2[] _patchSampleXZ = null;
        private float[]   _patchGroundY  = null;
        private bool[]    _patchSampled  = null;

        private bool    _isVisible      = false;
        private float   _lastRadius     = -1f;
        private Vector3 _lastCenter     = Vector3.zero;
        private bool    _warnedNoGround = false;

        /****************************************
        *              Properties
        ****************************************/

        private bool HasGroundLayer => _showGroundLayer && (_groundEffectAsset != null);

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            BuildWall();
            BuildPatches();
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (_wallMesh != null)
            {
                Destroy(_wallMesh);
                _wallMesh = null;
            }
        }

        private void Update()
        {
            IBarrierState state  = BarrierStateLocator.Current;
            float         radius = state.Radius;

            // 반경 자체가 SyncVar(BarrierTransitionState) 파생값이라 호스트와 클라이언트가 같은 값을 본다
            // 결계가 없는 phase에서는 반경이 0으로 수렴하므로 이것만으로 가시성 판정이 된다
            if (radius <= 0f)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);

            Vector3 center = state.Center;

            bool moved = (Mathf.Abs(radius - _lastRadius) > RADIUS_EPSILON)
                      || ((center - _lastCenter).sqrMagnitude > CENTER_EPSILON_SQR);

            if (!moved)
            {
                return;
            }

            _lastRadius = radius;
            _lastCenter = center;

            UpdateWall(center, radius);
            UpdatePatches(center, radius);
        }

        /****************************************
        *                Methods
        ****************************************/

        private void SetVisible(bool visible)
        {
            if (_isVisible == visible)
            {
                return;
            }

            _isVisible = visible;

            if (_wallTransform != null)
            {
                _wallTransform.gameObject.SetActive(visible);
            }

            if (_patchTransform != null)
            {
                for (int i = 0; i < _patchTransform.Length; ++i)
                {
                    _patchTransform[i].gameObject.SetActive(visible);
                }
            }

            // 다시 켜질 때는 캐시된 반경이 맞더라도 한 번은 재배치해야 한다
            if (visible)
            {
                _lastRadius = -1f;
            }
        }

        // 벽 높이를 결계 중심 한 점으로 정하면, 둘레 지형이 중심보다 높은 쪽에서 벽이 지형에 파묻힌다
        // 실제로 Dev_Barrier에서 중심 지형 44m, 둘레 지형 102m가 나와 벽 꼭대기(134m)를 패치가 넘어섰다
        // 그래서 둘레를 한 바퀴 훑어 최저, 최고를 구하고, 그 높이차만큼 원통을 늘린다
        private void UpdateWall(Vector3 center, float radius)
        {
            if (_wallTransform == null)
            {
                return;
            }

            float minGroundY = float.MaxValue;
            float maxGroundY = float.MinValue;

            for (int i = 0; i < _wallSegments; ++i)
            {
                float   angleRad = ((i / (float) _wallSegments) * Mathf.PI * 2f);
                Vector3 onCircle = center + (new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad)) * radius);
                float   groundY  = SampleGroundY(onCircle);

                if (groundY < minGroundY) { minGroundY = groundY; }
                if (groundY > maxGroundY) { maxGroundY = groundY; }
            }

            float baseY  = minGroundY - _groundSink;
            float height = (maxGroundY - minGroundY) + _wallHeight;

            // 메쉬가 반지름 0.5, 높이 0~1이라 스케일에 지름과 높이를 그대로 넣는다
            _wallTransform.SetPositionAndRotation(new Vector3(center.x, baseY, center.z), Quaternion.identity);
            _wallTransform.localScale = new Vector3(radius * 2f, height, radius * 2f);
        }

        private void UpdatePatches(Vector3 center, float radius)
        {
            if (_patchTransform == null)
            {
                return;
            }

            // 둘레를 개수로 나눠 가로 크기를 정한다. 결계가 좁아지면 패치도 같이 좁아져
            // 서로 겹치는 양이 일정하게 유지된다
            float width = _autoFitWidth ? ((2f * Mathf.PI * radius) / _patchCount) : _patchWidth;

            int   index     = 0;
            float bodyBaseY = 0f;

            if (HasGroundLayer)
            {
                PlaceRing(center, radius, width, index, 0, _groundLayerHeight, _groundLayerHeight * 0.5f);
                index    += _patchCount;
                bodyBaseY = _groundLayerHeight;
            }

            if (_bodyEffectAsset != null)
            {
                for (int row = 0; row < _bodyRows; ++row)
                {
                    // 행 중심을 _rowSpacing 간격으로 쌓는다. 이 값이 _bodyHeight보다 작으면 행이 겹쳐
                    // 띠가 얇아 보이고, 같으면 두 행이 맞닿아 두 겹으로 두껍게 보인다
                    float yOffset = bodyBaseY + (_bodyHeight * 0.5f) + (row * _rowSpacing);
                    PlaceRing(center, radius, width, index, row, _bodyHeight, yOffset);
                    index += _patchCount;
                }
            }
        }

        // 한 바퀴 분량의 패치를 놓는다. yOffset은 그 자리 지형 높이에 더할 값이다
        private void PlaceRing(Vector3 center, float radius, float width, int startIndex, int staggerRow, float height, float yOffset)
        {
            float angleStep = 360f / _patchCount;

            // 행마다 반 칸씩 엇갈리게 놓아 세로 이음매가 한 줄로 서지 않게 한다
            float rowOffsetDeg = ((staggerRow % 2) == 0) ? 0f : (angleStep * 0.5f);

            for (int i = 0; i < _patchCount; ++i)
            {
                int index = startIndex + i;

                float   angleRad = ((i * angleStep) + rowOffsetDeg) * Mathf.Deg2Rad;
                Vector3 outward  = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad));
                Vector3 onCircle = center + (outward * radius);

                float groundY = ResolvePatchGroundY(index, onCircle);

                Transform patch = _patchTransform[index];
                patch.SetPositionAndRotation(new Vector3(onCircle.x, groundY + yOffset, onCircle.z),
                                             Quaternion.LookRotation(outward, Vector3.up));
                patch.localScale = new Vector3(width, height, _patchThickness);
            }
        }

        // 패치가 거의 안 움직였으면 캐시된 높이를 그대로 쓴다
        // BarrierController.ResolveGroundY는 결계 중심 한 점만 재므로 능선에서 벽이 뜬다,
        // 여기서는 패치마다 제 발밑을 따로 잰다
        private float ResolvePatchGroundY(int index, Vector3 worldPosition)
        {
            Vector2 xz = new Vector2(worldPosition.x, worldPosition.z);

            if (_patchSampled[index])
            {
                float movedSqr = (xz - _patchSampleXZ[index]).sqrMagnitude;
                if (movedSqr < (_groundResampleDistance * _groundResampleDistance))
                {
                    return _patchGroundY[index];
                }
            }

            float groundY = SampleGroundY(worldPosition);

            _patchSampleXZ[index] = xz;
            _patchGroundY[index]  = groundY;
            _patchSampled[index]  = true;

            return groundY;
        }

        private float SampleGroundY(Vector3 worldPosition)
        {
            // 지형이 있으면 물리를 거치지 않고 높이맵을 직접 읽는다. 콜라이더를 타지 않으므로
            // 나무, 바위, 날씨 오브젝트를 바닥으로 오인할 여지가 없고, 레이캐스트보다 싸다
            if (_useTerrainHeight)
            {
                Terrain terrain = Terrain.activeTerrain;
                if (terrain != null)
                {
                    return terrain.transform.position.y + terrain.SampleHeight(worldPosition);
                }
            }

            Vector3 origin = new Vector3(worldPosition.x, worldPosition.y + _groundProbeHeight, worldPosition.z);

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, _groundProbeHeight * 2f,
                                _groundMask, QueryTriggerInteraction.Ignore))
            {
                return hit.point.y;
            }

            // 지형을 못 찾으면 결계 평면 높이를 그대로 쓴다. 조용히 넘기지 않되 매 프레임 도배하지도 않는다
            if (!_warnedNoGround)
            {
                _warnedNoGround = true;
                Debug.LogWarning($"[BarrierVisual] 지형을 찾지 못해 결계 평면 높이를 그대로 쓴다. _groundMask 설정을 확인할 것 (probe {origin})", this);
            }

            return worldPosition.y;
        }

        private void BuildWall()
        {
            if (_wallMaterial == null)
            {
                Debug.LogError("[BarrierVisual] _wallMaterial이 비어 있다. M_BarrierWall을 꽂아야 결계 벽이 그려진다", this);
                return;
            }

            GameObject wall = new GameObject("BarrierWall_Shell");
            wall.transform.SetParent(transform, false);
            _wallTransform = wall.transform;

            _wallMesh = BuildOpenCylinderMesh(_wallSegments, _doubleSidedShell);

            MeshFilter filter = wall.AddComponent<MeshFilter>();
            filter.sharedMesh = _wallMesh;

            MeshRenderer meshRenderer = wall.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial       = _wallMaterial;
            meshRenderer.shadowCastingMode    = ShadowCastingMode.Off;
            meshRenderer.receiveShadows       = false;
            meshRenderer.lightProbeUsage      = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private void BuildPatches()
        {
            if ((_groundEffectAsset == null) && (_bodyEffectAsset == null))
            {
                Debug.LogError("[BarrierVisual] 파티클 에셋이 하나도 없다. _groundEffectAsset(Bubble 1) · _bodyEffectAsset(Bubble 12)을 꽂을 것", this);
                return;
            }

            int groundRings = HasGroundLayer ? 1 : 0;
            int bodyRings   = (_bodyEffectAsset != null) ? Mathf.Max(_bodyRows, 0) : 0;
            int ringCount   = groundRings + bodyRings;
            int total       = Mathf.Max(_patchCount, 0) * ringCount;

            if (total <= 0)
            {
                Debug.LogError($"[BarrierVisual] 패치 개수가 0이다 (_patchCount {_patchCount} × 링 {ringCount})", this);
                return;
            }

            _patchTransform = new Transform[total];
            _patchEffect    = new VisualEffect[total];
            _patchSampleXZ  = new Vector2[total];
            _patchGroundY   = new float[total];
            _patchSampled   = new bool[total];

            for (int i = 0; i < total; ++i)
            {
                // 앞의 _patchCount개가 바닥 층이고 그 뒤가 본체 층이다. UpdatePatches의 배치 순서와 같다
                bool               isGround = (groundRings > 0) && (i < _patchCount);
                VisualEffectAsset  asset    = isGround ? _groundEffectAsset : _bodyEffectAsset;
                string             label    = isGround ? "Ground" : "Body";

                GameObject patch = new GameObject($"BarrierPatch_{label}_{i:D3}");
                patch.transform.SetParent(transform, false);

                VisualEffect effect = patch.AddComponent<VisualEffect>();
                effect.visualEffectAsset = asset;

                // 그래프가 Alpha를 노출하지 않는 변형본을 물려도 조용히 넘어가지 않게 한 번만 알린다
                if (effect.HasFloat(EFFECT_ALPHA_PROPERTY))
                {
                    // Set 전에 읽으면 그래프가 저장해 둔 기본값이 나온다. 거기에 배율을 건다
                    float graphDefault = effect.GetFloat(EFFECT_ALPHA_PROPERTY);
                    effect.SetFloat(EFFECT_ALPHA_PROPERTY, graphDefault * _effectAlphaScale);
                }
                else if (i == 0)
                {
                    Debug.LogWarning($"[BarrierVisual] '{asset.name}'이 '{EFFECT_ALPHA_PROPERTY}'를 노출하지 않아 _effectAlphaScale이 적용되지 않는다", this);
                }

                effect.playRate = _effectPlayRate;

                _patchTransform[i] = patch.transform;
                _patchEffect[i]    = effect;
            }
        }

        /// <summary>
        /// 위아래 뚜껑 없는 관 메쉬. 반지름 0.5, 높이 0~1이라 스케일에 (지름, 높이, 지름)을 그대로 넣는다
        /// 자체 셰이더는 Cull Off라 한 벌이면 충분하다. doubleSided를 켜면 안쪽을 향하는 면을
        /// 한 벌 더 만든다 - 뒷면을 자르는 구매 셰이더를 쓸 때 필요하다
        /// 노멀은 축 바깥으로 직접 지정한다 - RecalculateNormals는 이음매에서 평균을 내 방향이 흐려진다
        /// BarrierGizmoUtility 쪽은 #if UNITY_EDITOR 안에 있는 확인용 코드라 런타임 표현이 가져다 쓸 수 없다
        /// </summary>
        private static Mesh BuildOpenCylinderMesh(int segments, bool doubleSided)
        {
            segments = Mathf.Max(segments, 3);

            int       ringCount  = segments + 1; // UV 이음매를 위해 첫 정점을 한 번 더 넣는다
            int       sideCount  = doubleSided ? 2 : 1;
            int       sideVerts  = ringCount * 2;
            Vector3[] vertices   = new Vector3[sideVerts * sideCount];
            Vector3[] normals    = new Vector3[vertices.Length];
            Vector2[] uvs        = new Vector2[vertices.Length];
            int[]     triangles  = new int[segments * 6 * sideCount];

            for (int i = 0; i < ringCount; ++i)
            {
                float angle = (i / (float) segments) * Mathf.PI * 2f;
                float x     = Mathf.Cos(angle) * 0.5f;
                float z     = Mathf.Sin(angle) * 0.5f;

                int bottom = i * 2;
                int top    = bottom + 1;

                vertices[bottom] = new Vector3(x, 0f, z);
                vertices[top]    = new Vector3(x, 1f, z);

                Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                normals[bottom] = outward;
                normals[top]    = outward;

                float u = i / (float) segments;
                uvs[bottom] = new Vector2(u, 0f);
                uvs[top]    = new Vector2(u, 1f);
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

            if (doubleSided)
            {
                // 정점을 한 벌 더 복사하되 노멀만 뒤집고, 인덱스 순서를 반대로 감아
                // 안쪽에서 볼 때 앞면이 되게 한다. 노멀까지 뒤집어야 프레넬이 안쪽에서도 맞는다
                for (int i = 0; i < sideVerts; ++i)
                {
                    vertices[sideVerts + i] = vertices[i];
                    normals[sideVerts + i]  = -normals[i];
                    uvs[sideVerts + i]      = uvs[i];
                }

                int triOffset = segments * 6;
                for (int i = 0; i < segments; ++i)
                {
                    int bottomA = sideVerts + (i * 2);
                    int topA    = bottomA + 1;
                    int bottomB = sideVerts + ((i + 1) * 2);
                    int topB    = bottomB + 1;

                    int triIndex = triOffset + (i * 6);
                    triangles[triIndex]     = bottomB;
                    triangles[triIndex + 1] = topA;
                    triangles[triIndex + 2] = bottomA;

                    triangles[triIndex + 3] = bottomB;
                    triangles[triIndex + 4] = topB;
                    triangles[triIndex + 5] = topA;
                }
            }

            Mesh mesh = new Mesh { name = "BarrierVisual_OpenCylinder" };
            mesh.vertices  = vertices;
            mesh.normals   = normals;
            mesh.uv        = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}
