// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Network;

namespace Geumeum.AI
{
    /// <summary>
    /// RunPhase가 Boss로 바뀌면 보스 프리팹 1개를 씬의 태그 마커 위치에 스폰한다
    /// 씬에는 보스를 미리 배치하지 않는다 - 마커만 두고 런타임에 만든다
    /// </summary>
    public partial class BossSpawner : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private GameObject _bossPrefab;

        private bool _hasSpawned = false;
    }
}
