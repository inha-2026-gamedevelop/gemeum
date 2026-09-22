// System
using System;
using System.Collections;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;
using Geumeum.Systems;

namespace Geumeum.AI
{
    /// <summary>
    /// RunPhase별 동시 존재 수, 리스폰 타이머, 구성 비율을 관리한다
    /// 결계 목표 원으로 거점과 기존 잡귀 몰이를 갱신한다. SpawnConfigSO/BarrierPhaseSO 데이터로 구동된다
    /// </summary>
    public partial class SpawnManager : NetworkBehaviour
    {
        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        private struct EnemyPrefabEntry
        {
            public EnemyKind       Kind;
            public EnemyController Prefab;
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private EnemyPrefabEntry[] _enemyPrefabs = new EnemyPrefabEntry[Constants.Combat.ENEMY_KIND_COUNT];
    }
}
