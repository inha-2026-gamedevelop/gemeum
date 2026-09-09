// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> 부트 검증에 필요한 네트워크 스폰 프리팹을 제공하는 창구 </summary>
    public interface IRequiredSpawnPrefabProvider
    {
        /// <summary> 런타임 등록 없이 부트 검사 목록에 필요한 프리팹을 보탠다 </summary>
        void CollectRequiredSpawnPrefabs(List<GameObject> prefabs);
    }
}
