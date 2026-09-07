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

namespace Geumeum.AI
{
    /// <summary>
    /// RunPhase별 동시 존재 수 · 리스폰 타이머 · 구성 비율을 관리한다.
    /// 결계 축소 시 범위 밖 클러스터를 비활성화한다. SpawnConfigSO/BarrierPhaseSO 데이터로 구동된다
    /// </summary>
    public class SpawnManager : NetworkBehaviour
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

        private readonly List<SpawnCluster>                   _clusters             = new List<SpawnCluster>();
        private readonly Dictionary<SpawnCluster, List<uint>> _aliveNetIdsByCluster = new Dictionary<SpawnCluster, List<uint>>();
        private readonly Dictionary<uint, SpawnCluster>       _clusterByNetId       = new Dictionary<uint, SpawnCluster>();

        // 런 초기화에서 생존 개체를 지울 때 쓰는 복사 버퍼. 순회 중 사전이 바뀌는 것을 막는다
        private readonly List<uint>                          _despawnBuffer        = new List<uint>();

        private Transform                _barrierCenter;
        private SpawnConfigSO.PhaseEntry _currentPhase;
        private bool                     _isPhaseActive     = false; // 현재 phase에 스폰 설정이 있는지
        private int                      _targetConcurrent  = 0;     // [ConcurrentMin, ConcurrentMax]에서 이번 phase 진입 시 한 번 뽑은 목표치
        private int                      _totalAliveCount   = 0;
        private Coroutine                _respawnRoutine    = null;
        private WaitForSeconds           _respawnWait       = null;

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            // 같은 씬에서 StopHost->StartHost로 재시작하면 이전 런의 생존 목록, 카운트가 이
            // 컴포넌트 인스턴스에 그대로 남아 새 런이 이미 죽은 netId를 살아 있다고 본다
            ServerResetState();

            FindClusters();
            FindBarrierCenter();

            GameEventBus.Instance.Subscribe<RunPhaseChangedEvent>(HandlePhaseChanged);
            GameEventBus.Instance.Subscribe<EnemyDiedEvent>(HandleEnemyDied);

            // 같은 세션에서 다음 판을 열면 이전 필드의 생존 개체와 점유가 그대로 남는다
            GameEventBus.Instance.Subscribe<RunResetEvent>(HandleRunReset);
        }

        /// <summary> 이전 런의 클러스터 점유·생존 카운트·respawn 코루틴을 모두 비운다 </summary>
        [Server]
        private void ServerResetState()
        {
            StopRespawnRoutine();

            _aliveNetIdsByCluster.Clear();
            _clusterByNetId.Clear();
            _totalAliveCount  = 0;
            _targetConcurrent = 0;
            _isPhaseActive    = false;
            _currentPhase     = default;
        }

        public override void OnStopServer()
        {
            StopRespawnRoutine();

            // HasInstance->Instance 순차 조회는 앱 종료 중 경쟁 조건이 있다(_isQuitting을
            // HasInstance는 안 보고 Instance는 본다). Instance 한 번만 받아 그 결과로 null 체크한다
            GameEventBus bus = GameEventBus.Instance;
            if (bus == null)
            {
                return;
            }

            bus.Unsubscribe<RunPhaseChangedEvent>(HandlePhaseChanged);
            bus.Unsubscribe<EnemyDiedEvent>(HandleEnemyDied);
            bus.Unsubscribe<RunResetEvent>(HandleRunReset);
        }

        /****************************************
        *                Methods
        ****************************************/

        private void FindClusters()
        {
            _clusters.Clear();
            _clusters.AddRange(FindObjectsByType<SpawnCluster>(FindObjectsInactive.Exclude));
        }

        private void FindBarrierCenter()
        {
            GameObject marker = GameObject.FindWithTag(Constants.Tag.BARRIER_CENTER);
            if (marker == null)
            {
                Debug.LogWarning("[SpawnManager] BarrierCenter 마커를 찾지 못했다, 결계 반경 판정 없이 전 클러스터를 활성 상태로 둔다", this);
                return;
            }

            _barrierCenter = marker.transform;
        }

        // 런이 끝나면 남은 잡귀를 지우고 점유와 카운트를 되돌린다.
        // 목록만 비우고 개체를 두면 다음 판 필드에 이전 판의 잡귀가 서 있다
        [Server]
        private void HandleRunReset(RunResetEvent evt)
        {
            ServerDespawnAll();
            ServerResetState();
        }

        // 이 매니저가 스폰한 개체만 지운다. 보스처럼 다른 곳이 만든 것은 건드리지 않는다.
        //
        // 키를 복사해 돌린다. 지금은 파괴가 사망 이벤트를 발행하지 않지만, 나중에 종료 경로에
        // 발행이 하나 붙으면 HandleEnemyDied가 순회 중인 사전을 고쳐 그 자리에서 터진다.
        // 런당 한 번뿐이라 이 복사가 비싸지 않다
        [Server]
        private void ServerDespawnAll()
        {
            _despawnBuffer.Clear();
            _despawnBuffer.AddRange(_clusterByNetId.Keys);

            for (int i = 0; i < _despawnBuffer.Count; ++i)
            {
                if (!NetworkServer.spawned.TryGetValue(_despawnBuffer[i], out NetworkIdentity identity))
                {
                    continue; // 이미 사라진 개체
                }

                NetworkServer.Destroy(identity.gameObject);
            }

            _despawnBuffer.Clear();
        }

        [Server]
        private void HandlePhaseChanged(RunPhaseChangedEvent evt)
        {
            RefreshClusterRange(evt.Current);

            StopRespawnRoutine();

            _isPhaseActive = GameDB.Spawn.TryGetPhase(evt.Current, out _currentPhase);
            if (!_isPhaseActive)
            {
                return; // 이 구간엔 스폰 설정이 없다 (Lobby, Shrink3, Boss, Result) — 신규 스폰 중단
            }

            _targetConcurrent = UnityEngine.Random.Range(_currentPhase.ConcurrentMin, _currentPhase.ConcurrentMax + 1);
            TryFillClusters();

            if (_currentPhase.RespawnInterval > 0f)
            {
                _respawnWait    = new WaitForSeconds(_currentPhase.RespawnInterval);
                _respawnRoutine = StartCoroutine(CoRespawnTick());
            }
        }

        [Server]
        private void HandleEnemyDied(EnemyDiedEvent evt)
        {
            if (!_clusterByNetId.TryGetValue(evt.EnemyNetId, out SpawnCluster cluster))
            {
                return; // 이 매니저가 스폰하지 않은 개체(보스 등)
            }

            _clusterByNetId.Remove(evt.EnemyNetId);
            if (_aliveNetIdsByCluster.TryGetValue(cluster, out List<uint> aliveIds))
            {
                aliveIds.Remove(evt.EnemyNetId);
            }

            _totalAliveCount = Mathf.Max(0, _totalAliveCount - 1);
        }

        // TODO(오나): BarrierController는 이미 있고 중심도 매 단계 이동한다. 이 메서드는 아직 고정 마커와
        // BarrierPhaseSO의 "단계별 목표 반경"을 읽는 근사치다 — IBarrierState(Locator)로 실제 중심·반경을
        // 읽도록 바꾸는 작업은 docs/05-implementation-roadmap.md U24
        private void RefreshClusterRange(RunPhase phase)
        {
            if (_barrierCenter == null)
            {
                return; // 마커가 없으면 전 클러스터를 활성 상태로 둔 채 판정을 건너뛴다
            }

            if (!GameDB.Barrier.TryGetPhase(phase, out BarrierPhaseSO.PhaseEntry barrierPhase))
            {
                return;
            }

            float radius = GameDB.Barrier.MaxRadius * barrierPhase.RadiusRatio;

            for (int i = 0; i < _clusters.Count; ++i)
            {
                float distance = Vector3.Distance(_clusters[i].transform.position, _barrierCenter.position);
                _clusters[i].IsInRange = (distance <= radius);
            }
        }

        private IEnumerator CoRespawnTick()
        {
            while (true)
            {
                yield return _respawnWait;
                TryFillClusters();
            }
        }

        private void StopRespawnRoutine()
        {
            if (_respawnRoutine == null)
            {
                return;
            }

            StopCoroutine(_respawnRoutine);
            _respawnRoutine = null;
        }

        // 목표 동시 존재 수(_targetConcurrent)에 닿을 때까지, 비어 있고 범위 안인 클러스터를 순서대로 채운다
        [Server]
        private void TryFillClusters()
        {
            if (!_isPhaseActive)
            {
                return;
            }

            for (int i = 0; i < _clusters.Count; ++i)
            {
                if (_totalAliveCount >= _targetConcurrent)
                {
                    return;
                }

                SpawnCluster cluster = _clusters[i];
                if ((!cluster.IsInRange) || IsOccupied(cluster))
                {
                    continue;
                }

                PopulateCluster(cluster);
            }
        }

        private bool IsOccupied(SpawnCluster cluster)
        {
            return _aliveNetIdsByCluster.TryGetValue(cluster, out List<uint> aliveIds) && (aliveIds.Count > 0);
        }

        [Server]
        private void PopulateCluster(SpawnCluster cluster)
        {
            int remainingBudget = _targetConcurrent - _totalAliveCount;
            int clusterSize     = UnityEngine.Random.Range(GameDB.Spawn.ClusterSizeMin, GameDB.Spawn.ClusterSizeMax + 1);
            int spawnCount      = Mathf.Min(clusterSize, remainingBudget);

            if (spawnCount <= 0)
            {
                return;
            }

            if (!_aliveNetIdsByCluster.TryGetValue(cluster, out List<uint> aliveIds))
            {
                aliveIds = new List<uint>();
                _aliveNetIdsByCluster[cluster] = aliveIds;
            }

            for (int i = 0; i < spawnCount; ++i)
            {
                EnemyKind kind = RollKind();
                uint      netId = SpawnEnemy(kind, cluster.GetRandomPoint());
                if (netId == 0)
                {
                    continue;
                }

                aliveIds.Add(netId);
                _clusterByNetId[netId] = cluster;
                ++_totalAliveCount;
            }
        }

        private EnemyKind RollKind()
        {
            int total = _currentPhase.RatioHeokkaebi + _currentPhase.RatioChanggwi + _currentPhase.RatioAekgwi;
            if (total <= 0)
            {
                return EnemyKind.Heokkaebi;
            }

            int roll = UnityEngine.Random.Range(0, total);
            if (roll < _currentPhase.RatioHeokkaebi)
            {
                return EnemyKind.Heokkaebi;
            }

            if (roll < _currentPhase.RatioHeokkaebi + _currentPhase.RatioChanggwi)
            {
                return EnemyKind.Changgwi;
            }

            return EnemyKind.Aekgwi;
        }

        // 스폰에 성공하면 netId, 실패하면 0(Mirror가 유효한 netId로 발급하지 않는 값)을 반환한다
        [Server]
        private uint SpawnEnemy(EnemyKind kind, Vector3 position)
        {
            if (!TryGetPrefab(kind, out EnemyController prefab))
            {
                Debug.LogError($"[SpawnManager] {kind}의 프리팹이 배정되지 않았다", this);
                return 0;
            }

            EnemyController instance = Instantiate(prefab, position, Quaternion.identity);
            NetworkServer.Spawn(instance.gameObject);
            return instance.netId;
        }

        private bool TryGetPrefab(EnemyKind kind, out EnemyController prefab)
        {
            for (int i = 0; i < _enemyPrefabs.Length; ++i)
            {
                if (_enemyPrefabs[i].Kind == kind)
                {
                    prefab = _enemyPrefabs[i].Prefab;
                    return prefab != null;
                }
            }

            prefab = null;
            return false;
        }
    }
}
