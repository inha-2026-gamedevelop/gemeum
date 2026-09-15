// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary> 화면 중심 조준선과 발사점에서 목표점으로 향하는 방향의 공용 계산 </summary>
    public static class ArrowAimGeometry
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly Vector3 VIEWPORT_CENTER = new Vector3(0.5f, 0.5f, 0f);

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 카메라 화면 중심을 지나는 조준선. 카메라가 없으면 방향이 0인 값을 준다 </summary>
        public static PrecisionAimRay BuildCameraRay(Camera camera)
        {
            if (camera == null)
            {
                return default;
            }

            Ray ray = camera.ViewportPointToRay(VIEWPORT_CENTER);
            return new PrecisionAimRay { Origin = ray.origin, Direction = ray.direction.normalized };
        }

        /// <summary> 발사점에서 조준점으로 향하는 단위 방향. 두 점이 겹치거나 값이 유한하지 않으면 실패한다 </summary>
        public static bool TryDirection(Vector3 origin, Vector3 aimPoint, out Vector3 direction)
        {
            direction = default;

            Vector3 delta  = aimPoint - origin;
            float   length = delta.magnitude;
            if ((!IsFinite(length)) || (length <= Mathf.Epsilon))
            {
                return false;
            }

            direction = delta / length;
            return true;
        }

        /// <summary> 조준선 위에서 발사점을 투영한 거리. 이보다 가까운 충돌은 조준점이 될 수 없다 </summary>
        public static float ResolveMinimumHitDistance(in PrecisionAimRay cameraRay, Vector3 muzzle)
        {
            // 오버숄더 카메라는 캐릭터 뒤에 있어 카메라와 발사점 사이에 벽이나 몸이 끼기 쉽다.
            // 그 충돌을 조준점으로 쓰면 발사점보다 뒤쪽 점을 향해 화살이 뒤로 날아간다
            return Mathf.Max(0f, Vector3.Dot(muzzle - cameraRay.Origin, cameraRay.Direction));
        }

        /// <summary>
        /// 조준선 충돌 거리 중 발사점 투영 거리보다 멀고 사거리 안인 가장 가까운 거리를 고른다.
        /// 고를 것이 없으면 실패하고, 호출부는 사거리 끝점을 조준점으로 쓴다
        /// </summary>
        public static bool TrySelectAimDistance(float[] hitDistances, int count, float minimumDistance,
            float maxDistance, out float aimDistance)
        {
            aimDistance = maxDistance;
            if (hitDistances == null)
            {
                return false;
            }

            bool found = false;
            int  limit = Mathf.Min(count, hitDistances.Length);
            for (int i = 0; i < limit; ++i)
            {
                float distance = hitDistances[i];
                if ((!IsFinite(distance)) || (distance <= minimumDistance) || (distance > aimDistance))
                {
                    continue;
                }

                aimDistance = distance;
                found = true;
            }

            return found;
        }

        /// <summary> 조준점으로 방향을 못 만들 때의 평행 발사 방향. 카메라 방향을 못 쓰면 기존 조준 방향을 쓴다 </summary>
        public static Vector3 ResolveFallbackDirection(Vector3 cameraDirection, Vector3 aimForward)
        {
            return TryDirection(Vector3.zero, cameraDirection, out Vector3 direction) ? direction : aimForward;
        }

        private static bool IsFinite(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }
    }
}
