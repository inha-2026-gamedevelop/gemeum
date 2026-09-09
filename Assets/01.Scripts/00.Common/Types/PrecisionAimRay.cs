// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> 소유 클라이언트가 보낸 카메라 조준 의도 </summary>
    public struct PrecisionAimRay
    {
        public Vector3 Origin;
        public Vector3 Direction;
    }
}
