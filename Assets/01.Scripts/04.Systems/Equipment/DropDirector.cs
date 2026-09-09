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
    public class DropDirector : NetworkBehaviour, IRequiredSpawnPrefabProvider
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

        public override void OnStartServer()
        {
            _generator = null;
            _rules     = null;
            _claimedBossParts.Clear();
            ServerEnsureRuntime();

            if (!GameEventBus.HasInstance)
            {
                Debug.LogError("[DropDirector] GameEventBus가 없어 적 사망을 받지 못한다, 드랍이 나오지 않는다", this);
                return;
            }

            GameEventBus.Instance.Subscribe<EnemyDiedEvent>(HandleEnemyDied);
            GameEventBus.Instance.Subscribe<BossPartBrokenEvent>(HandleBossPartBroken);
            GameEventBus.Instance.Subscribe<RunResetEvent>(HandleRunReset);
            _hasSubscribed = true;
        }

        public override void OnStopServer()
        {
            if (_hasSubscribed && GameEventBus.HasInstance)
            {
                GameEventBus.Instance.Unsubscribe<EnemyDiedEvent>(HandleEnemyDied);
                GameEventBus.Instance.Unsubscribe<BossPartBrokenEvent>(HandleBossPartBroken);
                GameEventBus.Instance.Unsubscribe<RunResetEvent>(HandleRunReset);
            }

            _hasSubscribed = false;
            _generator     = null;
            _rules         = null;
        }

        /****************************************
        *              Server API
        ****************************************/

        [Server]
        private void HandleEnemyDied(EnemyDiedEvent evt)
        {
            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (!run.RunId.IsValid) || run.IsPaused ||
                (run.Phase == RunPhase.Lobby) || (run.Phase == RunPhase.Result) ||
                RunSceneDirector.IsServerTransitioning || (!evt.IsValid))
            {
                return;
            }

            if (!ServerEnsureRuntime())
            {
                return;
            }

            if (!ServerCanSpawnDrop())
            {
                return;
            }

            if (!DropRules.TryResolveFieldLevel(evt.IsBoss, evt.FieldLevelAtSpawn, out int fieldLevel))
            {
                return;
            }

            // 드랍 여부를 포함한 사망 판정을 먼저 소유해, 같은 이벤트가 재전달되어도 난수를 다시 굴리지 않는다
            if (!_rules.TryClaimDeath(run.RunId, evt.EnemyNetId))
            {
                return;
            }

            if (!_rules.ShouldDrop(evt.IsBoss))
            {
                return;
            }

            WeaponCombatSnapshot equipment = _generator.Generate(_rules.RollWeaponKind(), fieldLevel);

            ServerSpawnDrop(equipment, evt.Position);
        }

        /// <summary>
        /// A fixed boss PartId earns exactly one guaranteed reward.  The normal
        /// death claim cannot be reused because all three parts share one netId.
        /// </summary>
        [Server]
        private void HandleBossPartBroken(BossPartBrokenEvent evt)
        {
            RunManager run = RunManager.ServerInstance;
            RunSceneDirector director = RunSceneDirector.ServerInstance;
            if ((run == null) || (!run.RunId.IsValid) || run.IsPaused ||
                (run.Phase != RunPhase.Boss) || RunSceneDirector.IsServerTransitioning ||
                (!evt.IsValid) || (evt.Target.RunIdValue != run.RunId.Value) ||
                ((director != null) &&
                    (evt.Target.SceneRevisionValue != director.Revision.Value)) ||
                (!_claimedBossParts.Add(evt.Target)))
            {
                return;
            }

            if ((!ServerEnsureRuntime()) || (!ServerCanSpawnDrop()))
            {
                // Wiring failure is retryable if the same event is deliberately
                // replayed after repair; no reward was committed yet.
                _claimedBossParts.Remove(evt.Target);
                return;
            }

            WeaponCombatSnapshot equipment = _generator.Generate(
                _rules.RollWeaponKind(), DropRules.BOSS_REWARD_FIELD_LEVEL);
            ServerSpawnDrop(equipment, evt.Position);
        }

        /// <summary> 다음 런이 이전 런의 장비 ID를 이어받지 않게 한다 </summary>
        [Server]
        private void HandleRunReset(RunResetEvent evt)
        {
            _generator = null;
            _rules     = null;
            _claimedBossParts.Clear();
        }

        [Server]
        private void ServerSpawnDrop(in WeaponCombatSnapshot equipment, Vector3 deathPosition)
        {
            if (!ServerCanSpawnDrop())
            {
                return;
            }

            Vector3 position = (deathPosition + (Vector3.up * _spawnHeightOffset));

            GameObject instance = Instantiate(_droppedItemPrefab, position, Quaternion.identity);
            var dropped = instance.GetComponent<DroppedItem>();
            if (dropped == null)
            {
                Debug.LogError("[DropDirector] 드랍 프리팹에 DroppedItem이 없다", this);
                Destroy(instance);
                return;
            }

            // 스폰 전에 값을 심는다. 클라이언트가 빈 장비를 한 프레임이라도 보면 안 된다
            dropped.ServerInitialize(equipment);
            NetworkServer.Spawn(instance);
        }

        [Server]
        private bool ServerCanSpawnDrop()
        {
            if (_droppedItemPrefab == null)
            {
                Debug.LogError("[DropDirector] 드랍 프리팹이 배선되지 않아 장비를 떨어뜨리지 못했다", this);
                return false;
            }

            if (!_droppedItemPrefab.TryGetComponent<NetworkIdentity>(out _))
            {
                Debug.LogError("[DropDirector] 드랍 프리팹에 NetworkIdentity가 없다", this);
                return false;
            }

            if (!_droppedItemPrefab.TryGetComponent<DroppedItem>(out _))
            {
                Debug.LogError("[DropDirector] 드랍 프리팹에 DroppedItem이 없다", this);
                return false;
            }

            return true;
        }

        /****************************************
        *               Internals
        ****************************************/

        /// <summary> 생성기와 규칙을 준비한다. 이미 있으면 그대로 쓴다 </summary>
        [Server]
        private bool ServerEnsureRuntime()
        {
            if ((_generator != null) && (_rules != null))
            {
                return true;
            }

            if (!GameDB.IsInitialized)
            {
                Debug.LogError("[DropDirector] GameDB가 없어 장비 생성기를 만들지 못했다", this);
                return false;
            }

            try
            {
                // 런마다 다른 결과가 나오되, 한 런 안에서는 서버가 시드를 쥔다
                var random = new System.Random(Environment.TickCount);

                _generator = new EquipmentGenerator(GameDB.Equipment, GameDB.DropTable, random);
                _rules     = new DropRules(GameDB.DropTable.DropChance, random);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[DropDirector] 장비 생성기를 만들지 못했다: {exception.Message}", this);
                _generator = null;
                _rules     = null;
                return false;
            }

            return true;
        }
    }
}
