// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Systems
{
    /// <summary>
    /// 적이 죽으면 드랍 여부를 정하고 장비를 만들어 월드에 스폰한다.
    /// 생성기와 난수는 런 하나에 하나만 두어, 씬을 옮겨도 인스턴스 ID가 겹치지 않게 한다
    /// </summary>
    [DisallowMultipleComponent]
    public partial class DropDirector : NetworkBehaviour, IRequiredSpawnPrefabProvider
    {
        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("필드에 떨어질 장비 프리팹. NetworkManager의 스폰 목록에도 올라가 있어야 한다")]
        [SerializeField] private GameObject _droppedItemPrefab;

        [Tooltip("드랍이 바닥에 파묻히지 않도록 사망 위치에서 이만큼 띄운다")]
        [SerializeField] private float _spawnHeightOffset = 0.5f;

        // 서버 전용. 런이 시작될 때 한 번 만들고 씬 전환에도 유지한다
        private EquipmentGenerator _generator;
        private DropRules          _rules;
        private bool               _hasSubscribed;
        private readonly HashSet<TargetRef> _claimedBossParts = new HashSet<TargetRef>();

        /****************************************
        *              Unity Event
        ****************************************/

        public void CollectRequiredSpawnPrefabs(List<GameObject> prefabs)
        {
            if (prefabs == null)
            {
                return;
            }

            // null도 넣어 배선 누락을 부트 검사가 놓치지 않게 한다
            prefabs.Add(_droppedItemPrefab);
        }
    }
}
