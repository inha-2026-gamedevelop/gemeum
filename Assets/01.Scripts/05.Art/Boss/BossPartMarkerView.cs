// Unity
using UnityEngine;
using UnityEngine.Rendering;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Art
{
    /// <summary>
    /// 그로기 중 파괴 대상으로 지정된 꼬리(ActivePartId) 위에 빨간 마름모 표식을 띄운다
    /// 서버가 확정한 BossPartSnapshot만 읽어 표현만 한다, 판정에는 관여하지 않는다
    /// 꼬리 본이 애니메이션으로 계속 움직이므로 항상 카메라를 바라본다(빌보드)
    /// 이 스크립트는 BossPartHitbox와 같은 오브젝트에 붙는다 - 자기 transform을 직접 옮기거나
    /// 돌리면 그 오브젝트의 판정 콜라이더까지 같이 움직인다, 그래서 표식 전용 자식 오브젝트를
    /// 따로 만들어 그쪽만 옮기고 돌린다
    /// </summary>
    public sealed class BossPartMarkerView : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string SHADER_NAME    = "Universal Render Pipeline/Unlit";
        private const string BASE_COLOR_ID  = "_BaseColor";
        private const float  DIAGONAL_RATIO = 2f; // 세로 대각선 : 가로 대각선 = 2 : 1
        private static readonly Color MARKER_COLOR = Color.red;

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private byte    _partId             = BossTargetPartIds.Tail1;
        [SerializeField] private float   _horizontalDiagonal = 0.3f; // 가로 대각선 길이(m), 세로는 이 값의 2배
        [SerializeField] private Vector3 _localOffset        = new Vector3(0f, 0.4f, 0f);

        private IBossPartSource _partSource;
        private Transform       _markerTransform; // 표식 전용 자식, 이 오브젝트만 옮기고 돌린다
        private MeshRenderer    _meshRenderer;
        private Transform       _cameraTransform;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _partSource = GetComponentInParent<IBossPartSource>();

            var markerObject = new GameObject("PartMarker");
            markerObject.transform.SetParent(transform, worldPositionStays: false);
            markerObject.transform.localPosition = _localOffset;
            _markerTransform = markerObject.transform;

            var filter = markerObject.AddComponent<MeshFilter>();
            filter.mesh = BuildDiamondMesh(_horizontalDiagonal, _horizontalDiagonal * DIAGONAL_RATIO);

            _meshRenderer = markerObject.AddComponent<MeshRenderer>();

            Shader shader = Shader.Find(SHADER_NAME);
            if (shader == null)
            {
                Debug.LogError($"[BossPartMarkerView] 셰이더 '{SHADER_NAME}'을 못 찾았다", this);
                return;
            }

            var material = new Material(shader) { name = "BossPartMarker (Instance)" };
            material.SetColor(BASE_COLOR_ID, MARKER_COLOR);
            _meshRenderer.sharedMaterial    = material;
            _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _meshRenderer.receiveShadows    = false;
            _meshRenderer.enabled           = false;
        }

        private void OnEnable()
        {
            if (_partSource != null)
            {
                _partSource.PartChanged += HandlePartChanged;
                HandlePartChanged(_partSource.PartSnapshot);
            }
        }

        private void OnDisable()
        {
            if (_partSource != null)
            {
                _partSource.PartChanged -= HandlePartChanged;
            }
        }

        private void LateUpdate()
        {
            if (!_meshRenderer.enabled)
            {
                return;
            }

            if (_cameraTransform == null)
            {
                Camera main = Camera.main;
                if (main == null)
                {
                    return;
                }

                _cameraTransform = main.transform;
            }

            _markerTransform.rotation =
                Quaternion.LookRotation(_markerTransform.position - _cameraTransform.position, Vector3.up);
        }

        /****************************************
        *                Methods
        ****************************************/

        private void HandlePartChanged(BossPartSnapshot snapshot)
        {
            _meshRenderer.enabled = (!snapshot.IsBroken(_partId)) && (snapshot.ActivePartId == _partId);
        }

        // 마름모(연) 형태 - 위/오른쪽/아래/왼쪽 꼭짓점 넷을 부채꼴 둘로 채운다
        private static Mesh BuildDiamondMesh(float horizontalDiagonal, float verticalDiagonal)
        {
            float halfWidth  = horizontalDiagonal * 0.5f;
            float halfHeight = verticalDiagonal * 0.5f;

            var mesh = new Mesh { name = "BossPartMarkerDiamond" };
            mesh.vertices = new[]
            {
                new Vector3(0f, halfHeight, 0f),
                new Vector3(halfWidth, 0f, 0f),
                new Vector3(0f, -halfHeight, 0f),
                new Vector3(-halfWidth, 0f, 0f),
            };
            mesh.uv = new[]
            {
                new Vector2(0.5f, 1f),
                new Vector2(1f, 0.5f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 0.5f),
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
