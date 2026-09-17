// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> 손에 든 무기의 손잡이 끝과 칼끝 월드 위치. 검기 같은 표현이 실제 검의 경로를 따라 그릴 때 읽는다 </summary>
    public interface IWeaponBladeSource
    {
        bool TryGetBladeSegment(out Vector3 hiltWorld, out Vector3 tipWorld);
    }
}
