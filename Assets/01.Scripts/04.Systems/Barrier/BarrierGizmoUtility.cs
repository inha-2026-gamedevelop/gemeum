#if UNITY_EDITOR
// Unity
using UnityEngine;

namespace Geumeum.Systems
{
    /// <summary>
    /// 결계 디버그 시각화가 공유하는 순수 헬퍼. BarrierController와 BarrierJudgeGizmoPreview 양쪽이
    /// 와이어 서클과 뚜껑 없는 관 메쉬를 각자 다시 구현하지 않고 여기 하나로 모은다
    /// 정식 아트 셰이더가 아니라 확인용이라 에디터 빌드에만 존재한다
    /// </summary>
    public static class BarrierGizmoUtility
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> XZ 평면에 반경 원을 그린다. Gizmos.color는 호출부가 미리 정한다 </summary>
        public static void DrawWireCircleXZ(Vector3 center, float radius, int segments = 64)
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

        /// <summary>
        /// 위아래 뚜껑 없이 옆면만 있는 관 메쉬. 반지름 0.5, 높이 -1~1로 만들어
        /// CreatePrimitive(Cylinder)와 같은 단위 크기를 쓴다. 호출부가 transform.localScale로
        /// (반경*2, 원하는높이/2, 반경*2)를 넣으면 원하는 크기가 된다
        /// Sprites/Default처럼 Cull Off인 셰이더를 쓰면 안팎 어디서 봐도 보인다
        /// </summary>
        public static Mesh BuildOpenCylinderMesh(int segments)
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

            Mesh mesh = new Mesh { name = "BarrierGizmo_OpenCylinder" };
            mesh.vertices  = vertices;
            mesh.uv        = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary> 확인용 반투명 머티리얼. 파이프라인(빌트인/URP) 무관하게 항상 반투명으로 그려진다 </summary>
        public static Material BuildFallbackMaterial(Color color)
        {
            Shader shader = Shader.Find("Sprites/Default");
            return new Material(shader) { color = color };
        }
    }
}
#endif
