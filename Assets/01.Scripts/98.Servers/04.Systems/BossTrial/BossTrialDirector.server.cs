// System
using System.Collections;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Combat;
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Systems
{
    public partial class BossTrialDirector
    {
        /****************************************
        *                Fields
        ****************************************/

        // 지급은 언제나 첫 칸에 넣고 바로 장착한다. 요청 번호는 중복 차단용이라 한 번만 쓰면 1이면 된다
        private const int  FIRST_INVENTORY_SLOT = 0;
        private const uint GRANT_REQUEST_ID     = 1u;

        [Header("같은 프리팹의 런 매니저들. 씬에서 찾지 않고 직접 연결한다")]
        [SerializeField] private GrowthManager   _growth;
        [SerializeField] private CurrencyManager _wallet;
        [SerializeField] private DropDirector    _drops;

        // 서버 전용. 연결 번호로 기억한다
        private readonly Dictionary<int, BossTrialLoadout> _submissions  = new Dictionary<int, BossTrialLoadout>();
        private readonly List<NetworkConnectionToClient>   _playerBuffer = new List<NetworkConnectionToClient>();

        private double    _collectDeadlineServerTime;
        private Coroutine _applyRoutine;
        private bool      _isApplied;

        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            BossTrialGateLocator.Register(this);
            ServerResetCollection();

            if (GameEventBus.Instance != null)
            {
                GameEventBus.Instance.Subscribe<RunPhaseChangedEvent>(HandlePhaseChanged);
                GameEventBus.Instance.Subscribe<RunResetEvent>(HandleRunReset);
            }
        }

        public override void OnStopServer()
        {
            BossTrialGateLocator.Unregister(this);

            if (GameEventBus.Instance != null)
            {
                GameEventBus.Instance.Unsubscribe<RunPhaseChangedEvent>(HandlePhaseChanged);
                GameEventBus.Instance.Unsubscribe<RunResetEvent>(HandleRunReset);
            }

            ServerResetCollection();
        }

        /****************************************
        *              Server API
        ****************************************/

        // 편성을 모으는 동안에만 돈다. 제한 시간이 지나면 남은 사람을 빈 편성으로 시작시킨다
        private void Update()
        {
            if ((!isServer) || (!_isCollecting))
            {
                return;
            }

            ServerUpdateCollection(NetworkTime.time);
        }

        [Server]
        private void ServerUpdateCollection(double now)
        {
            if ((!_isCollecting) || (_submittedCount >= _requiredCount))
            {
                return;
            }

            ServerRefreshRequiredCount();

            if (now < _collectDeadlineServerTime)
            {
                return;
            }

            Debug.LogWarning("[BossTrialDirector] 편성 제한 시간이 지나 남은 사람은 빈 편성으로 시작한다", this);
            ServerCompleteCollection();
        }

        [Server]
        private void ServerHandleSubmit(NetworkConnectionToClient sender, BossTrialLoadout loadout)
        {
            if (sender == null)
            {
                return;
            }

            BossTrialLoadoutResult result = ServerResolveSubmission(sender, in loadout);
            TargetSubmissionResolved(sender, result);

            if (result != BossTrialLoadoutResult.Success)
            {
                Debug.LogWarning($"[BossTrialDirector] 편성 거절: conn={sender.connectionId}, 사유={result}", this);
                return;
            }

            _submissions[sender.connectionId] = loadout;
            _submittedCount = _submissions.Count;

            if (_submittedCount >= _requiredCount)
            {
                ServerCompleteCollection();
            }
        }

        [Server]
        private BossTrialLoadoutResult ServerResolveSubmission(NetworkConnectionToClient sender,
            in BossTrialLoadout loadout)
        {
            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (run.Mode != RunMode.BossTrial))
            {
                return BossTrialLoadoutResult.NotTrialMode;
            }

            if (!_isCollecting)
            {
                return BossTrialLoadoutResult.WrongPhase;
            }

            if ((sender.identity == null) || (!sender.identity.TryGetComponent(out InventoryController _)))
            {
                return BossTrialLoadoutResult.UnknownPlayer;
            }

            return BossTrialRules.Validate(ResolveConfig(), in loadout, out _);
        }

        [Server]
        private void ServerHandleTrialModeRequest(NetworkConnectionToClient sender, bool enabled)
        {
            if ((sender == null) || (sender != NetworkServer.localConnection))
            {
                Debug.LogWarning("[BossTrialDirector] 호스트가 아닌 연결이 모드 변경을 요청해 거절했다", this);
                return;
            }

            RunManager run = RunManager.ServerInstance;
            if ((run != null) && (run.Phase != RunPhase.Lobby))
            {
                Debug.LogWarning("[BossTrialDirector] 런이 도는 중에는 모드를 바꾸지 않는다", this);
                return;
            }

            _isTrialRequested = enabled;
        }

        [Server]
        private void ServerHandleRetryRequest(NetworkConnectionToClient sender)
        {
            // 판을 다시 여는 것은 방을 연 사람만 한다. 정산 화면에서 서로 누르면 판이 겹친다
            if ((sender == null) || (sender != NetworkServer.localConnection))
            {
                Debug.LogWarning("[BossTrialDirector] 호스트가 아닌 연결이 재시도를 요청해 거절했다", this);
                return;
            }

            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (!run.ServerRequestBossTrialRetry()))
            {
                Debug.LogWarning("[BossTrialDirector] 지금은 재시도를 시작할 수 없다", this);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        [Server]
        private void HandlePhaseChanged(RunPhaseChangedEvent evt)
        {
            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (run.Mode != RunMode.BossTrial))
            {
                return;
            }

            // 보스 구간에 들어가 씬과 참가자 복구가 끝난 다음에 편성을 실제 자원으로 바꾼다
            // 그 전에 주면 씬 전환이 참가자 상태를 다시 부으면서 준 것이 사라진다
            if (evt.Current == RunPhase.Boss)
            {
                ServerScheduleApply();
                return;
            }

            if ((evt.Current == RunPhase.Result) || (evt.Current == RunPhase.Lobby))
            {
                return;
            }

            ServerBeginCollection();
        }

        [Server]
        private void HandleRunReset(RunResetEvent evt)
        {
            ServerResetCollection();
        }

        [Server]
        private void ServerBeginCollection()
        {
            if (_isCollecting)
            {
                return;
            }

            _submissions.Clear();
            _submittedCount            = 0;
            _isApplied                 = false;
            _isCollecting              = true;
            _collectDeadlineServerTime = NetworkTime.time + Constants.Network.BOSS_TRIAL_LOADOUT_TIMEOUT;
            ServerRefreshRequiredCount();
        }

        [Server]
        private void ServerRefreshRequiredCount()
        {
            _requiredCount = Mathf.Max(1, CollectPlayers().Count);
        }

        /// <summary> 다 모였다. 보스 구간이 열릴 때까지 값만 들고 있는다 </summary>
        [Server]
        private void ServerCompleteCollection()
        {
            _isCollecting = false;
        }

        /// <summary> 보스 씬 전환과 참가자 복구가 끝나면 편성을 적용한다 </summary>
        [Server]
        private void ServerScheduleApply()
        {
            if ((_isApplied) || (_applyRoutine != null))
            {
                return;
            }

            _applyRoutine = StartCoroutine(CoApplyWhenReady());
        }

        private IEnumerator CoApplyWhenReady()
        {
            while (RunSceneDirector.IsServerTransitioning)
            {
                yield return null;
            }

            // 복구가 마지막으로 값을 부은 다음 프레임에 준다
            yield return null;

            _applyRoutine = null;
            _isApplied    = true;
            ServerApplyCollection();
        }

        [Server]
        private void ServerApplyCollection()
        {
            BossTrialLoadoutSO config = ResolveConfig();
            if (config == null)
            {
                Debug.LogError("[BossTrialDirector] BossTrialLoadoutSO가 없어 시작 자원을 주지 못했다", this);
                _isCollecting = false;
                return;
            }

            ServerApplyPartyState(config, ResolveStartLevel(config));

            List<NetworkConnectionToClient> players = CollectPlayers();
            for (int i = 0; i < players.Count; ++i)
            {
                NetworkConnectionToClient player = players[i];
                if (!_submissions.TryGetValue(player.connectionId, out BossTrialLoadout loadout))
                {
                    loadout = BossTrialRules.CreateEmpty();
                }

                ServerApplyLoadout(player.identity, in loadout);
            }
            foreach (var pair in Geumeum.Network.ParticipantDirectory.Actors)
            {
                if (pair.Key.Kind != ParticipantKind.Bot) { continue; }
                BossTrialLoadout loadout = BossTrialRules.CreateEmpty();
                ServerApplyLoadout(pair.Value, in loadout);
            }
        }

        /// <summary>
        /// 레벨은 파티 하나가 공유하므로 제출된 값 중 가장 높은 것을 쓴다
        /// 사람마다 다른 레벨을 줄 수 없고, 낮은 쪽을 고르면 높게 적은 사람이 왜 안 되는지 알 수 없다
        /// </summary>
        [Server]
        private int ResolveStartLevel(BossTrialLoadoutSO config)
        {
            int level = config.StartLevel;

            foreach (KeyValuePair<int, BossTrialLoadout> pair in _submissions)
            {
                if (pair.Value.StartLevel > level)
                {
                    level = pair.Value.StartLevel;
                }
            }

            return Mathf.Clamp(level, 1, Constants.Combat.MAX_LEVEL);
        }

        [Server]
        private void ServerApplyPartyState(BossTrialLoadoutSO config, int startLevel)
        {
            if ((_growth != null) && (!_growth.ServerTrySetPartyLevel(startLevel)))
            {
                Debug.LogError($"[BossTrialDirector] 시작 레벨 {startLevel}을 적용하지 못했다", this);
            }

            if ((_wallet != null) && (config.StartingCurrency > 0) &&
                (!_wallet.TryCredit(config.StartingCurrency)))
            {
                Debug.LogError("[BossTrialDirector] 시작 재화를 넣지 못했다", this);
            }
        }

        [Server]
        private void ServerApplyLoadout(NetworkIdentity player, in BossTrialLoadout loadout)
        {
            if ((player == null) ||
                (!player.TryGetComponent(out InventoryController inventory)))
            {
                return;
            }

            if (!player.TryGetComponent(out PlayerWeapon weapon))
            {
                return;
            }

            ServerApplyWeapon(weapon, in loadout);

            if ((loadout.EquipmentTier >= 0) && (_drops != null) && (weapon.HasWeapon) &&
                (_drops.ServerTryGenerateEquipment(weapon.Weapon, loadout.EquipmentTier,
                    out WeaponCombatSnapshot equipment)))
            {
                ApplyCustomEquipment(ref equipment, in loadout);
                ServerGrantEquipment(inventory, player.netId, in equipment);
            }

            if (loadout.PotionCount > 0)
            {
                ServerGrantPotions(inventory, loadout.PotionCount);
            }

            ServerApplyTalents(player, loadout.TalentNodeIds);
        }

        /// <summary>
        /// 편성에서 고른 무기로 바꾼다. 로비 배정과 달리 중복을 막지 않는다 -
        /// 트라이는 같은 무기 셋으로 보스를 두들겨 보는 것도 밸런싱 도구다
        /// </summary>
        [Server]
        private void ServerApplyWeapon(PlayerWeapon weapon, in BossTrialLoadout loadout)
        {
            if ((!loadout.OverrideWeapon) || (weapon.Weapon == loadout.Weapon))
            {
                return;
            }

            if (!weapon.ServerSetInitialWeapon(loadout.Weapon))
            {
                Debug.LogWarning($"[BossTrialDirector] 무기를 {loadout.Weapon}로 바꾸지 못했다", this);
            }
        }

        /// <summary> 상세 지정한 값만 덮어쓴다. 0 이하와 None은 서버가 굴린 값을 그대로 둔다 </summary>
        private static void ApplyCustomEquipment(ref WeaponCombatSnapshot equipment,
            in BossTrialLoadout loadout)
        {
            if (!loadout.UseCustomEquipment)
            {
                return;
            }

            equipment.Grade = loadout.EquipmentGrade;

            if (loadout.AttackPower > 0f)
            {
                equipment.AttackPower = loadout.AttackPower;
            }

            EquipmentSO catalog = (GameDB.IsInitialized) ? GameDB.Equipment : null;

            if (loadout.Option0Kind != EquipmentOptionKind.None)
            {
                equipment.Option0Kind  = loadout.Option0Kind;
                equipment.Option0Value = BossTrialRules.ResolveOptionValue(catalog,
                    equipment.WeaponKind, loadout.Option0Kind, loadout.Option0Value);
            }

            if (loadout.Option1Kind != EquipmentOptionKind.None)
            {
                equipment.Option1Kind  = loadout.Option1Kind;
                equipment.Option1Value = BossTrialRules.ResolveOptionValue(catalog,
                    equipment.WeaponKind, loadout.Option1Kind, loadout.Option1Value);
            }
        }

        /// <summary> 고른 순서대로 찍는다. 포인트와 선행 조건은 특성 규칙이 다시 본다 </summary>
        [Server]
        private void ServerApplyTalents(NetworkIdentity identity, int[] nodeIds)
        {
            if ((nodeIds == null) || (nodeIds.Length == 0) ||
                (!identity.TryGetComponent(out TalentController talents)))
            {
                return;
            }

            for (int i = 0; i < nodeIds.Length; ++i)
            {
                TalentAllocationResult result = talents.ServerTryAllocate(nodeIds[i], talents.Revision);
                if (result != TalentAllocationResult.Success)
                {
                    Debug.LogWarning(
                        $"[BossTrialDirector] 특성 {nodeIds[i]}을 찍지 못했다: {result}", this);
                }
            }
        }

        [Server]
        private void ServerGrantEquipment(InventoryController inventory, uint ownerNetId,
            in WeaponCombatSnapshot equipment)
        {
            InventoryResult stored = inventory.ServerTryStoreEquipment(in equipment, inventory.Revision);
            if (stored != InventoryResult.Success)
            {
                Debug.LogError($"[BossTrialDirector] 시작 장비를 넣지 못했다: {stored}", this);
                return;
            }

            // 전실에서 바로 들고 나가야 하므로 칸에 두지 않고 장착까지 해 준다
            InventoryResult equipped = inventory.ServerTryEquip(ownerNetId, FIRST_INVENTORY_SLOT,
                equipment.InstanceId, inventory.Revision, GRANT_REQUEST_ID, out WeaponCombatSnapshot _);
            if (equipped != InventoryResult.Success)
            {
                Debug.LogWarning($"[BossTrialDirector] 시작 장비를 장착하지 못해 칸에 남겼다: {equipped}", this);
            }
        }

        [Server]
        private void ServerGrantPotions(InventoryController inventory, int count)
        {
            if ((!GameDB.IsInitialized) || (GameDB.Shop == null) ||
                (!GameDB.Shop.TryGetItem(ShopItemKind.HealthPotion, out ShopSO.ItemEntry entry)))
            {
                Debug.LogError("[BossTrialDirector] 상점 데이터가 없어 물약을 주지 못했다", this);
                return;
            }

            InventoryResult stored = inventory.ServerTryStoreConsumable(ShopItemKind.HealthPotion,
                count, entry.CarryLimit, entry.MaxStack, inventory.Revision);
            if (stored != InventoryResult.Success)
            {
                Debug.LogError($"[BossTrialDirector] 시작 물약을 넣지 못했다: {stored}", this);
            }
        }

        [Server]
        private void ServerResetCollection()
        {
            if (_applyRoutine != null)
            {
                StopCoroutine(_applyRoutine);
                _applyRoutine = null;
            }

            _submissions.Clear();
            _isCollecting              = false;
            _isApplied                 = false;
            _submittedCount            = 0;
            _requiredCount             = 0;
            _collectDeadlineServerTime = 0d;
        }

        [Server]
        private List<NetworkConnectionToClient> CollectPlayers()
        {
            _playerBuffer.Clear();

            foreach (KeyValuePair<int, NetworkConnectionToClient> pair in NetworkServer.connections)
            {
                NetworkConnectionToClient connection = pair.Value;
                if ((connection == null) || (connection.identity == null))
                {
                    continue;
                }

                if (!connection.identity.TryGetComponent(out InventoryController _))
                {
                    continue;
                }

                _playerBuffer.Add(connection);
            }

            return _playerBuffer;
        }

        private static BossTrialLoadoutSO ResolveConfig()
        {
            return GameDB.IsInitialized ? GameDB.BossTrial : null;
        }
    }
}
