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
    /// <summary>
    /// 플레이어의 기본 공격. 판정은 전부 서버에서 하고 클라이언트는 요청만 한다.
    /// 입력을 그대로 믿으면 사람마다 다른 적이 죽는다
    /// </summary>
    public class PlayerAttack : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("공격 입력 키. 조작 변경은 씬이 아니라 여기서 한다")]
        [SerializeField] private KeyCode _attackKey = KeyCode.Mouse0;

        [Tooltip("판정 대상 레이어. 비워 두면 아무것도 맞지 않는다")]
        [SerializeField] private LayerMask _targetMask = ~0;

        private PlayerStatus         _status;
        private PlayerWeapon         _weapon;
        private PlayerRollController _roll;
        private ParryController      _parry;

        private readonly RequestIdTracker _requestIds = new RequestIdTracker();

        // 판정 시각과 다음 공격이 가능해지는 시각. 서버 시간 기준이다
        private double _hitServerTime      = 0d;
        private double _recoveryServerTime = 0d;
        private bool   _isSwinging         = false;

        private uint _nextRequestId = 1u;

        // 매번 새로 만들면 공격마다 GC 할당이 생긴다
        private readonly Collider[] _hitBuffer = new Collider[16];

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 공격 동작 중인가. 구르기와 패링이 이 값을 보고 겹침을 막는다 </summary>
        public bool IsActionInProgress(double serverTime)
        {
            return (_isSwinging) || (serverTime < _recoveryServerTime);
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _status = GetComponent<PlayerStatus>();
            _weapon = GetComponent<PlayerWeapon>();
            _roll   = GetComponent<PlayerRollController>();
            _parry  = GetComponent<ParryController>();
        }

        private void Update()
        {
            // 입력은 자기 캐릭터에서만 읽는다
            if ((!isLocalPlayer) || (!Input.GetKeyDown(_attackKey)))
            {
                return;
            }

            CmdRequestAttack(_nextRequestId);
            ++_nextRequestId;
        }

        /****************************************
        *              Commands
        ****************************************/

        [Command]
        private void CmdRequestAttack(uint requestId)
        {
            if (!_requestIds.TryAccept(requestId))
            {
                return;
            }

            if ((!GameDB.IsInitialized) || (_status == null) || (_weapon == null))
            {
                return;
            }

            double         now    = NetworkTime.time;
            CombatConfigSO combat = GameDB.Combat;

            if (!CanStartAttack(now, combat))
            {
                return;
            }

            if (!_status.TrySpendStamina(combat.AttackStaminaCost))
            {
                return;
            }

            _isSwinging         = true;
            _hitServerTime      = now + combat.AttackStartupDuration;
            _recoveryServerTime = _hitServerTime + combat.AttackRecoveryDuration;
        }

        /****************************************
        *              Server Side
        ****************************************/

        /// <summary>
        /// 선딜이 끝나는 시점에 한 번만 판정한다. 코루틴을 쓰지 않는 이유는
        /// 사망이나 런 중단에서 취소 지점을 따로 관리해야 하기 때문이다
        /// </summary>
        [Server]
        public void ServerUpdateState(double now)
        {
            if (!_isSwinging)
            {
                return;
            }

            // 휘두르는 도중 죽거나 멈추면 판정 없이 끝낸다
            if ((_status.IsDead) || (IsRunStopped()))
            {
                Cancel();
                return;
            }

            if (now < _hitServerTime)
            {
                return;
            }

            ApplyHit(GameDB.Combat);
            _isSwinging = false;
        }

        /****************************************
        *                Methods
        ****************************************/

        private bool CanStartAttack(double now, CombatConfigSO combat)
        {
            bool isOtherActionInProgress = ((_roll != null) && (_roll.IsRolling))
                || ((_parry != null) && (_parry.IsActionInProgress(now)));

            RunPhase phase    = (RunManager.ServerInstance != null) ? RunManager.ServerInstance.Phase : RunPhase.Lobby;
            bool     isPaused = ((RunManager.ServerInstance != null) && (RunManager.ServerInstance.IsPaused));

            var context = new PlayerActionContext(
                phase,
                _status.LifeState,
                isPaused,
                _weapon.HasWeapon,
                ((_isSwinging) || (isOtherActionInProgress)),
                now,
                _recoveryServerTime,
                _status.Stamina,
                combat.AttackStaminaCost);

            return PlayerActionGate.CanStartAction(context);
        }

        // 정면 부채꼴 안에 있는 대상만 맞춘다. 구를 통째로 쓰면 등 뒤의 적도 맞는다
        [Server]
        private void ApplyHit(CombatConfigSO combat)
        {
            Vector3 origin  = transform.position;
            Vector3 forward = transform.forward;
            float   cosHalf = Mathf.Cos(combat.AttackAngle * 0.5f * Mathf.Deg2Rad);

            int count = Physics.OverlapSphereNonAlloc(origin, combat.AttackRange, _hitBuffer, _targetMask);

            for (int i = 0; i < count; ++i)
            {
                Collider hit = _hitBuffer[i];
                if ((hit == null) || (hit.transform.root == transform.root))
                {
                    continue;
                }

                Vector3 toTarget = (hit.transform.position - origin);
                toTarget.y       = 0f;

                if (toTarget.sqrMagnitude <= Mathf.Epsilon)
                {
                    continue;
                }

                if (Vector3.Dot(forward, toTarget.normalized) < cosHalf)
                {
                    continue;
                }

                // 콜라이더가 자식에 붙어 있는 경우가 있어 root에서 계약을 찾는다
                if (!hit.transform.root.TryGetComponent(out IDamageable damageable))
                {
                    continue;
                }

                if (damageable.IsDead)
                {
                    continue;
                }

                damageable.TakeDamage(new DamageInfo(combat.AttackBasePower, netId, HitStrength.Light));
            }
        }

        [Server]
        private void Cancel()
        {
            _isSwinging         = false;
            _recoveryServerTime = 0d;
        }

        private static bool IsRunStopped()
        {
            RunManager run = RunManager.ServerInstance;
            return ((run == null) || (run.IsPaused));
        }
    }
}
