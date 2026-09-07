// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Combat
{
    /// <summary> 방울의 범위 치유. 신력을 써서 주변 파티원의 HP를 채운다 </summary>
    public class HealController : NetworkBehaviour, IPlayerAction
    {
        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("치유 입력 키. 조작 변경은 씬이 아니라 여기서 한다")]
        [SerializeField] private KeyCode _healKey = KeyCode.Mouse1;

        private PlayerStatus         _status;
        private PlayerWeapon         _weapon;
        private PlayerRollController _roll;
        private ParryController      _parry;
        private PlayerAttack         _attack;

        private RequestIdTracker _requestIds;
        private uint             _nextRequestId = 1u;

        private bool   _isCasting               = false;
        private double _castEndServerTime       = 0d;
        private double _actionLockEndServerTime = 0d;
        private double _cooldownEndServerTime   = 0d;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _status = GetComponent<PlayerStatus>();
            _weapon = GetComponent<PlayerWeapon>();
            _roll   = GetComponent<PlayerRollController>();
            _parry  = GetComponent<ParryController>();
            _attack = GetComponent<PlayerAttack>();
        }

        private void Update()
        {
            if ((!isLocalPlayer) || (!Input.GetKeyDown(_healKey)))
            {
                return;
            }

            CmdRequestHeal(_nextRequestId);
            ++_nextRequestId;
        }

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 시전이나 후딜이 진행 중인가. 다른 행동이 이 값을 보고 겹치기를 막는다 </summary>
        public bool IsActionInProgress(double serverTime)
        {
            return serverTime < _actionLockEndServerTime;
        }

        /****************************************
        *              Commands
        ****************************************/

        [Command]
        private void CmdRequestHeal(uint requestId)
        {
            ActionRejection rejection = PlayerActionPipeline.TryExecute(
                ref _requestIds, requestId, this, NetworkTime.time);

            if (rejection == ActionRejection.None)
            {
                return;
            }

            TargetHealRejected(rejection);
        }

        /****************************************
        *              Player Action
        ****************************************/

        /// <summary> 이번 치유의 게이트 입력을 만든다 </summary>
        public bool TryBuildContext(double serverTime, out PlayerActionContext context)
        {
            context = default;

            if ((!GameDB.IsInitialized) || (_status == null) || (_weapon == null))
            {
                return false;
            }

            bool isOtherActionInProgress = ((_roll != null) && (_roll.IsRolling))
                || ((_parry != null) && (_parry.IsActionInProgress(serverTime)))
                || ((_attack != null) && (_attack.IsActionInProgress(serverTime)))
                || (_isCasting);

            RunPhase phase    = (RunManager.ServerInstance != null) ? RunManager.ServerInstance.Phase : RunPhase.Lobby;
            bool     isPaused = ((RunManager.ServerInstance != null) && (RunManager.ServerInstance.IsPaused));

            context = new PlayerActionContext(
                phase,
                _status.LifeState,
                isPaused,
                ((_weapon.HasWeapon) && (_weapon.Weapon == WeaponKind.Bell)),
                isOtherActionInProgress,
                serverTime,
                _cooldownEndServerTime,
                _status.Spirit,
                GameDB.Combat.HealSpiritCost);

            return true;
        }

        public bool TrySpendResource(in PlayerActionContext context)
        {
            return _status.TrySpendSpirit(context.ResourceCost);
        }

        /// <summary> 시전을 시작한다. 회복은 시전이 끝나는 시점에 한 번 일어난다 </summary>
        public void CommitAction(in PlayerActionContext context)
        {
            CombatConfigSO combat = GameDB.Combat;

            _isCasting               = true;
            _castEndServerTime       = context.ServerTime + combat.HealCastDuration;
            _actionLockEndServerTime = _castEndServerTime + combat.HealRecoveryDuration;

            // 쿨다운은 수락 시점부터 잰다. 시전이 끊겨도 신력과 쿨다운은 돌려주지 않는다
            _cooldownEndServerTime = context.ServerTime + combat.HealCooldownDuration;
        }

        /****************************************
        *              Server Side
        ****************************************/

        /// <summary> 시전이 끝나는 시점에 한 번만 회복시킨다 </summary>
        [Server]
        public void ServerUpdateState(double now)
        {
            if (!_isCasting)
            {
                return;
            }

            // 공격과 같은 규칙이다. 쓰러지거나 런이 멈추면 시전을 버린다
            if ((_status.IsDead) || (IsRunStopped()))
            {
                _isCasting = false;
                return;
            }

            if (now < _castEndServerTime)
            {
                return;
            }

            _isCasting = false;
            ApplyHeal(GameDB.Combat);
        }

        [Server]
        private void ApplyHeal(CombatConfigSO combat)
        {
            Vector3 origin    = transform.position;
            float   radiusSqr = combat.HealRadius * combat.HealRadius;

            // 파티가 세 명이라 연결 목록을 도는 편이 물리 쿼리보다 싸고 레이어 설정에 기대지 않는다
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
            {
                if ((conn == null) || (conn.identity == null))
                {
                    continue;
                }

                if (!conn.identity.TryGetComponent(out PlayerStatus target))
                {
                    continue;
                }

                Vector3 offset = (target.transform.position - origin);
                if (offset.sqrMagnitude > radiusSqr)
                {
                    continue;
                }

                // 결계 회복 배수와 최대 HP는 TryRestoreHealth가 이미 적용한다. 여기서 또 곱하면 두 번 걸린다.
                // 쓰러진 사람도 그 안에서 거부되므로 치유가 부활을 대신하지 않는다
                target.TryRestoreHealth(combat.HealAmount);
            }
        }

        private static bool IsRunStopped()
        {
            RunManager run = RunManager.ServerInstance;
            return ((run == null) || (run.IsPaused));
        }

        /****************************************
        *              TargetRpc
        ****************************************/

        [TargetRpc]
        private void TargetHealRejected(ActionRejection rejection)
        {
            LocalCombatState.RaiseActionRejected(rejection);
        }
    }
}
