// System
using System;

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
    /// <summary> 서버 인벤토리. 소유 클라이언트에게만 12칸을 바뀐 칸 단위로 보낸다 </summary>
    [DisallowMultipleComponent]
    public partial class InventoryController : NetworkBehaviour, IRunStateCarrier, IRunResettable,
        ICombatLoadoutProvider, ICombatLoadoutRevisionTarget
    {
        /****************************************
        *                Fields
        ****************************************/

        private WeaponCombatSnapshot _serverEquippedWeapon;
        private bool                 _serverHasEquippedWeapon;

        // 장비/레벨/특성 변화를 전투 소비자가 캐시할 때 쓰는 revision
        private uint _loadoutRevision;
        private uint _lastGrowthRevision;
        private uint _lastTalentRevision;
        private uint _lastBuffRevision;

        /// <summary> 서버 원본. 규칙 계산과 carrier 캡처가 이 값을 본다 </summary>
        private readonly InventorySnapshot _inventory = new InventorySnapshot();

        private IEquipmentWeaponTarget _weaponTarget;
        private ITalentAllocationProvider _talentProvider;
        private ICombatBuffProvider       _buffProvider;
        private IDamageable            _damageable;
        private IGrowthParticipant     _growthParticipant;
        private bool                   _growthChangedSubscribed;
        private bool                   _transactionLocked;

        // 장착 요청은 이 컴포넌트가 받은 마지막 결과를 한 번 재전송할 수 있게 한다
        private bool                  _hasLastEquipRequest;
        private EquipmentRequestKind  _lastEquipRequestKind;
        private uint                  _lastEquipRequestId;
        private int                   _lastEquipSlot;
        private uint                  _lastEquipInstanceId;
        private uint                  _lastEquipExpectedRevision;
        private InventoryResult       _lastEquipResult;
        private WeaponCombatSnapshot  _lastEquipPreviousWeapon;
        private uint                  _nextEquipRequestId = 1u;

        private const uint REQUEST_SERIAL_HALF_RANGE = 0x80000000u;

        private enum EquipmentRequestKind
        {
            Equip,
            Unequip,
        }

        /****************************************
        *              Properties
        ****************************************/

        public uint                 Revision         => isServer ? _inventory.Revision : _revision;
        public uint                 LoadoutRevision  => _loadoutRevision;
        public WeaponCombatSnapshot EquippedWeapon =>
            isServer ? _serverEquippedWeapon : _equippedWeapon;
        public bool HasEquippedWeapon =>
            isServer ? _serverHasEquippedWeapon : _hasEquippedWeapon;
        public int                   CarrierOrder     => 12;

        public int OwnerSlotCapacity => InventorySnapshot.SLOT_COUNT;

        internal bool IsTransactionLocked => _transactionLocked;

        uint ICombatLoadoutProvider.Revision => _loadoutRevision;

        /// <summary> 소유 클라이언트에게 복제된 슬롯을 호출자 버퍼에 복사한다 </summary>
        public bool TryCopyOwnerSlots(InventorySlotSnapshot[] destination,
            out uint revision)
        {
            revision = 0u;
            if ((!isOwned) || (destination == null) ||
                (destination.Length < InventorySnapshot.SLOT_COUNT) ||
                (_slots.Count != InventorySnapshot.SLOT_COUNT))
            {
                return false;
            }

            for (int i = 0; i < InventorySnapshot.SLOT_COUNT; ++i)
            {
                destination[i] = _slots[i];
            }

            revision = _revision;
            return true;
        }

        /// <summary>
        /// 현재 장착 snapshot과 레벨 곡선으로 공격 시 사용할 전투 수치를 만든다.
        /// 이 메서드는 서버가 공격을 수락하는 순간 호출하고, 반환한 값을 행동에 복사해 둔다.
        /// </summary>
        public bool TryBuildLoadout(ParticipantId participant, out CombatStatsSnapshot snapshot)
        {
            snapshot = default;

            if ((!isServer) || (!participant.IsValid) || (!GameDB.IsInitialized))
            {
                return false;
            }

            TryBindGrowthParticipant(participant);

            if (!ServerEnsureEquippedWeapon())
            {
                return false;
            }

            if (!InventoryRules.IsValidEquippedWeapon(_serverEquippedWeapon))
            {
                return false;
            }

            if (_loadoutRevision == 0u)
            {
                return false;
            }

            if ((_talentProvider == null) || (_buffProvider == null) ||
                (GameDB.Combat == null) ||
                (GameDB.Arrow == null) || (GameDB.Talent == null))
            {
                return false;
            }

            if (!TryResolveGrowth(participant, out GrowthState growth))
            {
                return false;
            }

            if ((!_talentProvider.TryGetAllocation(participant,
                    out TalentAllocationSnapshot allocation)) ||
                (allocation.Revision != _lastTalentRevision) ||
                (allocation.Weapon != _serverEquippedWeapon.WeaponKind))
            {
                return false;
            }

            if ((_lastBuffRevision == 0u) && (_buffProvider.Revision != 0u))
            {
                _lastBuffRevision = _buffProvider.Revision;
            }

            if ((!_buffProvider.TryGetDamageMultiplier(participant,
                    out float activeDamageMultiplier)) ||
                (_buffProvider.Revision != _lastBuffRevision))
            {
                return false;
            }

            return CombatStatCalculator.TryBuild(
                GameDB.Combat,
                GameDB.Arrow,
                GameDB.Talent,
                _serverEquippedWeapon,
                growth,
                allocation,
                activeDamageMultiplier,
                _loadoutRevision,
                out snapshot);
        }

        /// <summary> 장착 snapshot이 바뀌었다 </summary>
        public event Action<WeaponCombatSnapshot> EquippedWeaponChanged;

        /// <summary> 장착 요청 결과를 소유 클라이언트에 알린다 </summary>
        public event Action<uint, InventoryResult, uint> EquipResult;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            syncMode = SyncMode.Owner;

            _weaponTarget = GetComponent<IEquipmentWeaponTarget>();
            _talentProvider = GetComponent<ITalentAllocationProvider>();
            _buffProvider = GetComponent<ICombatBuffProvider>();
            _damageable   = GetComponent<IDamageable>();
            _growthParticipant = GetComponent<IGrowthParticipant>();

            if (_buffProvider != null)
            {
                _buffProvider.RevisionChanged += OnBuffRevisionChanged;
            }
        }

        private void OnDestroy()
        {
            UnbindGrowthParticipant();

            if (_buffProvider != null)
            {
                _buffProvider.RevisionChanged -= OnBuffRevisionChanged;
            }
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 소유 클라이언트가 장착 교환을 요청한다 </summary>
        public void RequestEquip(int slot, uint instanceId, uint expectedRevision)
        {
            if (!isLocalPlayer)
            {
                return;
            }

            uint requestId = TakeNextEquipRequestId();
            CmdRequestEquip(slot, instanceId, expectedRevision, requestId);
        }

        /// <summary> 소유 클라이언트가 장착 해제를 요청한다 </summary>
        public void RequestUnequip(uint expectedRevision)
        {
            if (!isLocalPlayer)
            {
                return;
            }

            uint requestId = TakeNextEquipRequestId();
            CmdRequestUnequip(expectedRevision, requestId);
        }

        /****************************************
        *            Capture / Restore
        ****************************************/

        /// <summary>
        /// 역할 배분 전이라 아직 무기가 없는 상태인가.
        ///
        /// Capture가 무기 없는 참가자에 default를 담으므로 같은 값을 여기서 알아본다.
        /// 일부만 비어 있는 값은 손상이므로 정확히 default일 때만 인정한다
        /// </summary>
        private static bool IsUnarmedSnapshot(in WeaponCombatSnapshot weapon)
        {
            return weapon.Equals(default(WeaponCombatSnapshot));
        }

        public bool Validate(RunParticipantSnapshot snapshot, out string failure)
        {
            if ((snapshot == null) || (snapshot.SchemaVersion != RunParticipantSnapshot.CURRENT_SCHEMA_VERSION) ||
                (!InventoryRules.IsValid(snapshot.Inventory)))
            {
                failure = "인벤토리 값이 유효하지 않다";
                return false;
            }

            // 로비에서는 역할이 아직 배분되지 않아 아무도 무기가 없다. 그 상태를 거부하면
            // 로비를 지나는 씬 전환이 참가자마다 실패하고 세션이 통째로 닫힌다
            if (IsUnarmedSnapshot(snapshot.EquippedWeapon))
            {
                failure = null;
                return true;
            }

            if (!InventoryRules.IsValidEquippedWeapon(snapshot.EquippedWeapon))
            {
                failure = "장착 무기 값이 유효하지 않다";
                return false;
            }

            if (InventoryRules.TryFindEquipment(snapshot.Inventory, snapshot.EquippedWeapon.InstanceId, out _))
            {
                failure = "같은 장비가 장착 무기와 인벤토리에 함께 있다";
                return false;
            }

            if ((!Enum.IsDefined(typeof(WeaponKind), snapshot.Weapon)) ||
                (snapshot.Weapon != snapshot.EquippedWeapon.WeaponKind))
            {
                failure = "역할 무기와 장착 snapshot의 무기 종류가 다르다";
                return false;
            }

            failure = null;
            return true;
        }

        /****************************************
        *               Internals
        ****************************************/

        private uint TakeNextEquipRequestId()
        {
            uint requestId = _nextEquipRequestId;
            ++_nextEquipRequestId;

            if (_nextEquipRequestId == 0u)
            {
                _nextEquipRequestId = 1u;
            }

            return (requestId == 0u) ? 1u : requestId;
        }

        private static bool IsFinite(float value)
        {
            return ((!float.IsNaN(value)) && (!float.IsInfinity(value)));
        }

        private bool TryResolveGrowth(ParticipantId participant, out GrowthState growth)
        {
            growth = default;

            if (GrowthProviderLocator.HasProvider)
            {
                if (!GrowthProviderLocator.Current.TryGetState(participant, out growth))
                {
                    return false;
                }
            }
            else if (GrowthParticipantLocator.TryGet(participant, out IGrowthParticipant localParticipant))
            {
                growth = localParticipant.Growth;
            }
            else if ((_growthParticipant != null) &&
                (_growthParticipant.Participant == participant))
            {
                growth = _growthParticipant.Growth;
            }
            else
            {
                if ((GameDB.LevelCurve == null) ||
                    (!GameDB.LevelCurve.TryGetLevel(Constants.Combat.START_LEVEL,
                        out LevelCurveSO.LevelEntry startLevel)) ||
                    (!IsFinite(startLevel.MaxHealth)) || (startLevel.MaxHealth <= 0f))
                {
                    return false;
                }

                growth = new GrowthState(Constants.Combat.START_LEVEL, 0, startLevel.MaxHealth, 1u, 0);
            }

            if ((!growth.IsValid) || (!IsFinite(growth.MaxHealth)) || (growth.MaxHealth <= 0f))
            {
                return false;
            }

            return ObserveGrowthRevision(growth.Revision);
        }

        private void TryBindGrowthParticipant(ParticipantId participant)
        {
            IGrowthParticipant candidate = _growthParticipant;
            if (participant.IsValid && GrowthParticipantLocator.TryGet(participant,
                out IGrowthParticipant locatedParticipant))
            {
                candidate = locatedParticipant;
            }

            BindGrowthParticipant(candidate);
        }

        private void BindGrowthParticipant(IGrowthParticipant participant)
        {
            if (participant == null)
            {
                return;
            }

            if (!object.ReferenceEquals(_growthParticipant, participant))
            {
                UnbindGrowthParticipant();
                _growthParticipant = participant;
            }

            if (!_growthChangedSubscribed)
            {
                _growthParticipant.GrowthChanged += OnGrowthChanged;
                _growthChangedSubscribed = true;
            }

            GrowthState current = _growthParticipant.Growth;
            if (_growthParticipant.Participant.IsValid && GrowthProviderLocator.HasProvider &&
                GrowthProviderLocator.Current.TryGetState(_growthParticipant.Participant,
                    out GrowthState providerGrowth) && providerGrowth.IsValid)
            {
                current = providerGrowth;
            }

            if ((_lastGrowthRevision == 0u) && current.IsValid)
            {
                _lastGrowthRevision = current.Revision;
            }
        }

        private void UnbindGrowthParticipant()
        {
            if (_growthChangedSubscribed && (_growthParticipant != null))
            {
                _growthParticipant.GrowthChanged -= OnGrowthChanged;
            }

            _growthChangedSubscribed = false;
        }

        private void OnGrowthChanged(GrowthState growth)
        {
            if ((!isServer) || (!growth.IsValid))
            {
                return;
            }

            ObserveGrowthRevision(growth.Revision);
        }

        private void OnBuffRevisionChanged(uint revision)
        {
            if ((!isServer) || (revision == 0u) ||
                (_lastBuffRevision == revision))
            {
                return;
            }

            if ((_lastBuffRevision != 0u) && (_loadoutRevision == uint.MaxValue))
            {
                Debug.LogError("[InventoryController] 버프 변경을 반영할 loadout revision이 고갈됐다",
                    this);
                return;
            }

            if (_lastBuffRevision != 0u)
            {
                ++_loadoutRevision;
            }

            _lastBuffRevision = revision;
        }

        private bool ObserveGrowthRevision(uint growthRevision)
        {
            if (growthRevision == 0u)
            {
                return false;
            }

            if (_lastGrowthRevision == growthRevision)
            {
                return true;
            }

            // A new run starts its growth revision at one; do not count that as an equipment change.
            if ((_lastGrowthRevision != 0u) && (growthRevision < _lastGrowthRevision))
            {
                _lastGrowthRevision = growthRevision;
                return true;
            }

            if ((_lastGrowthRevision != 0u) && (_loadoutRevision == uint.MaxValue))
            {
                return false;
            }

            if (_lastGrowthRevision != 0u)
            {
                ++_loadoutRevision;
            }

            _lastGrowthRevision = growthRevision;
            return true;
        }

        /****************************************
        *               TargetRpc
        ****************************************/

        [TargetRpc]
        private void TargetEquipResult(NetworkConnection target, uint requestId, InventoryResult result,
            uint revision, WeaponCombatSnapshot previousWeapon)
        {
            EquipResult?.Invoke(requestId, result, revision);
        }

        /****************************************
        *             SyncVar Hooks
        ****************************************/

        private void OnEquippedWeaponChanged(WeaponCombatSnapshot oldWeapon,
            WeaponCombatSnapshot newWeapon)
        {
            Action<WeaponCombatSnapshot> handlers = EquippedWeaponChanged;
            if (handlers == null)
            {
                return;
            }

            Delegate[] observers = handlers.GetInvocationList();
            for (int i = 0; i < observers.Length; ++i)
            {
                try
                {
                    ((Action<WeaponCombatSnapshot>) observers[i])(newWeapon);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[InventoryController] 장착 장비 observer 실패: {e}", this);
                }
            }
        }
    }
}
