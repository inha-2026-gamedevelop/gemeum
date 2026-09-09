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
    /// RunPhase별 동시 존재 수 · 리스폰 타이머 · 구성 비율을 관리한다.
    /// 결계 목표 원으로 거점과 기존 잡귀 몰이를 갱신한다. SpawnConfigSO/BarrierPhaseSO 데이터로 구동된다
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

        private SpawnConfigSO.PhaseEntry _currentPhase;
        private bool                     _isPhaseActive     = false; // 현재 phase에 스폰 설정이 있는지
        private int                      _targetConcurrent  = 0;     // [ConcurrentMin, ConcurrentMax]에서 이번 phase 진입 시 한 번 뽑은 목표치
        private int                      _totalAliveCount   = 0;
        private double                   _nextSpawnGameplayTime = double.PositiveInfinity;
        private uint                     _lastHerdingRevision   = 0u;
        private Coroutine                _schedulerWaitRoutine;
        private IBarrierTransitionSource _barrierSource;

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            // 같은 씬에서 StopHost->StartHost로 재시작하면 이전 런의 생존 목록, 카운트가 이
            // 컴포넌트 인스턴스에 그대로 남아 새 런이 이미 죽은 netId를 살아 있다고 본다
            ServerResetState();

            FindClusters();

            GameEventBus.Instance.Subscribe<RunPhaseChangedEvent>(HandlePhaseChanged);
            GameEventBus.Instance.Subscribe<EnemyDiedEvent>(HandleEnemyDied);

            // 같은 세션에서 다음 판을 열면 이전 필드의 생존 개체와 점유가 그대로 남는다
            GameEventBus.Instance.Subscribe<RunResetEvent>(HandleRunReset);

            BarrierStateLocator.Changed += HandleBarrierSourceChanged;
            BindBarrierSource(BarrierStateLocator.TransitionSource);

            if (AuthorityTickScheduler.ServerInstance != null)
            {
                AuthorityTickScheduler.ServerInstance.Register(
                    Constants.Network.TICK_PRIORITY_AI, ServerTickSpawn);
            }
            else
            {
                _schedulerWaitRoutine = StartCoroutine(CoWaitForScheduler());
            }

            ServerApplyCurrentPhase();
        }

        /// <summary>
        /// 지금 phase를 한 번 적용한다. 구독만으로는 부족하다 -
        /// phase 전환 이벤트는 씬 이동보다 먼저 발행되므로, 그 전환 때문에 로드된 이 씬의
        /// SpawnManager는 자기를 존재하게 만든 이벤트를 영영 받지 못한다
        /// </summary>
        [Server]
        private void ServerApplyCurrentPhase()
        {
            RunManager run = RunManager.ServerInstance;
            if (run == null)
            {
                Debug.LogWarning("[SpawnManager] RunManager가 없어 현재 phase로 스폰을 시작하지 못했다", this);
                return;
            }

            HandlePhaseChanged(new RunPhaseChangedEvent(run.Phase, run.Phase));
        }

        /// <summary> 이전 런의 클러스터 점유, 생존 카운트, 다음 스폰 시각을 모두 비운다 </summary>
        [Server]
        private void ServerResetState()
        {
            _aliveNetIdsByCluster.Clear();
            _clusterByNetId.Clear();
            _totalAliveCount  = 0;
            _targetConcurrent = 0;
            _isPhaseActive    = false;
            _currentPhase     = default;
            _nextSpawnGameplayTime = double.PositiveInfinity;
            _lastHerdingRevision   = 0u;
        }

        public override void OnStopServer()
        {
            BarrierStateLocator.Changed -= HandleBarrierSourceChanged;
            BindBarrierSource(null);

            if (_schedulerWaitRoutine != null)
            {
                StopCoroutine(_schedulerWaitRoutine);
                _schedulerWaitRoutine = null;
            }

            if (AuthorityTickScheduler.ServerInstance != null)
            {
                AuthorityTickScheduler.ServerInstance.Unregister(ServerTickSpawn);
            }

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
            _isPhaseActive = GameDB.Spawn.TryGetPhase(evt.Current, out _currentPhase);
            if (!_isPhaseActive)
            {
                _nextSpawnGameplayTime = double.PositiveInfinity;
                return; // 이 구간엔 스폰 설정이 없다 (Lobby, Shrink3, Boss, Result) — 신규 스폰 중단
            }

            _targetConcurrent = UnityEngine.Random.Range(_currentPhase.ConcurrentMin, _currentPhase.ConcurrentMax + 1);
            double now = RunTimeLocator.Current.FieldElapsedSeconds;
            _nextSpawnGameplayTime = SpawnHerdingRules.GetFirstSpawnGameplayTime(
                evt.Current, now, GameDB.Spawn.PostShrinkSpawnGraceSeconds);

            if (_nextSpawnGameplayTime <= now)
            {
                ServerRunSpawnAttempt(now);
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

        private IEnumerator CoWaitForScheduler()
        {
            while (AuthorityTickScheduler.ServerInstance == null)
            {
                yield return null;
            }

            AuthorityTickScheduler.ServerInstance.Register(
                Constants.Network.TICK_PRIORITY_AI, ServerTickSpawn);
            _schedulerWaitRoutine = null;
        }

        [Server]
        private void ServerTickSpawn(float deltaTime)
        {
            if (!_isPhaseActive)
            {
                return;
            }

            double now = RunTimeLocator.Current.FieldElapsedSeconds;
            if (now < _nextSpawnGameplayTime)
            {
                return;
            }

            ServerRunSpawnAttempt(now);
        }

        [Server]
        private void ServerRunSpawnAttempt(double now)
        {
            TryFillClusters();
            _nextSpawnGameplayTime = (_currentPhase.RespawnInterval > 0f)
                ? now + _currentPhase.RespawnInterval
                : double.PositiveInfinity;
        }

        private void HandleBarrierSourceChanged(IBarrierTransitionSource source)
        {
            BindBarrierSource(source);
        }

        private void BindBarrierSource(IBarrierTransitionSource source)
        {
            if (_barrierSource != null)
            {
                _barrierSource.SnapshotChanged -= HandleBarrierSnapshot;
            }

            _barrierSource = source;
            if (_barrierSource == null)
            {
                return;
            }

            _barrierSource.SnapshotChanged += HandleBarrierSnapshot;
            HandleBarrierSnapshot(_barrierSource.Snapshot);
        }

        [Server]
        private void HandleBarrierSnapshot(BarrierTransitionState snapshot)
        {
            if (snapshot.Revision == 0u)
            {
                return;
            }

            RefreshClusterRange(snapshot.TargetCenter, snapshot.TargetRadius);

            bool supportsHerding = (snapshot.TargetPhase == RunPhase.Shrink1) ||
                (snapshot.TargetPhase == RunPhase.Shrink2) ||
                ((snapshot.TargetPhase == RunPhase.Shrink3) &&
                 GameDB.Spawn.EnableShrink3Herding);
            if (!supportsHerding || (snapshot.Revision == _lastHerdingRevision))
            {
                return;
            }

            if (!GameDB.Barrier.TryGetPhase(snapshot.TargetPhase,
                    out BarrierPhaseSO.PhaseEntry targetPhase))
            {
                Debug.LogError($"[SpawnManager] {snapshot.TargetPhase} 결계 데이터를 찾지 못해 몰이를 시작하지 못했다", this);
                return;
            }

            _lastHerdingRevision = snapshot.Revision;
            foreach (KeyValuePair<uint, SpawnCluster> pair in _clusterByNetId)
            {
                if (!NetworkServer.spawned.TryGetValue(pair.Key,
                        out NetworkIdentity identity) ||
                    !identity.TryGetComponent(out EnemyController enemy))
                {
                    continue;
                }

                enemy.ServerBeginHerding(snapshot.TargetCenter, snapshot.TargetRadius,
                    targetPhase.FieldLevel, snapshot.Revision);
            }
        }

        private void RefreshClusterRange(Vector3 center, float radius)
        {
            if (float.IsNaN(radius) || float.IsInfinity(radius) || (radius < 0f) ||
                !IsFinite(center))
            {
                Debug.LogError("[SpawnManager] 목표 결계 snapshot의 중심 또는 반경이 유효하지 않다", this);
                return;
            }

            for (int i = 0; i < _clusters.Count; ++i)
            {
                _clusters[i].IsInRange = BarrierTargetSelector.IsInside(
                    _clusters[i].transform.position, center, radius);
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        // 목표 동시 존재 수(_targetConcurrent)에 닿을 때까지, 비어 있고 범위 안인 클러스터를 순서대로 채운다
        [Server]
        private void TryFillClusters()
        {
            if (!_isPhaseActive)
            {
                return;
            }

            int desiredConcurrent = ResolveDesiredConcurrent();
            for (int i = 0; i < _clusters.Count; ++i)
            {
                if (_totalAliveCount >= desiredConcurrent)
                {
                    return;
                }

                SpawnCluster cluster = _clusters[i];
                if ((!cluster.IsInRange) || IsOccupied(cluster))
                {
                    continue;
                }

                PopulateCluster(cluster, desiredConcurrent);
            }
        }

        private int ResolveDesiredConcurrent()
        {
            int herdingCount = 0;
            foreach (KeyValuePair<uint, SpawnCluster> pair in _clusterByNetId)
            {
                if (NetworkServer.spawned.TryGetValue(pair.Key,
                        out NetworkIdentity identity) &&
                    identity.TryGetComponent(out EnemyController enemy) &&
                    enemy.IsHerding)
                {
                    ++herdingCount;
                }
            }

            return SpawnHerdingRules.GetDesiredConcurrent(_targetConcurrent,
                _currentPhase.ConcurrentMax,
                GameDB.Spawn.HerdingConcurrentCapMultiplier,
                herdingCount);
        }

        private bool IsOccupied(SpawnCluster cluster)
        {
            return _aliveNetIdsByCluster.TryGetValue(cluster, out List<uint> aliveIds) && (aliveIds.Count > 0);
        }

        [Server]
        private void PopulateCluster(SpawnCluster cluster, int desiredConcurrent)
        {
            int remainingBudget = desiredConcurrent - _totalAliveCount;
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
