// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Network;

namespace Geumeum.Combat
{
    /// <summary> 로비와 런 관리자가 플레이어에게 말을 거는 단일 창구 </summary>
    [RequireComponent(typeof(PlayerWeapon))]
    [RequireComponent(typeof(PlayerCharacter))]
    public class PlayerRunAgent : NetworkBehaviour, IPlayerRoleAssignment, IPlayerRunLifecycle,
        IRunResultParticipantSource
    {
        /****************************************
        *                Fields
        ****************************************/

        private PlayerWeapon     _weapon     = null;
        private PlayerCharacter _character = null;
        private PlayerStatus     _status     = null;
        private PlayerController _controller = null;

        // PlayerStatus가 먼저 OnStopServer를 받으면 참가자 신원을 지운다.
        // 결과 원본은 컴포넌트 종료 순서와 무관하게 마지막 상태를 기록해야 한다.
        private ParticipantId _resultParticipant = ParticipantId.None;

        // 상태를 담고 붓는 컴포넌트들
        private readonly List<IRunStateCarrier> _carriers = new List<IRunStateCarrier>();
        private readonly List<IRunResettable> _resettable = new List<IRunResettable>();

        /****************************************
        *              Properties
        ****************************************/

        public bool HasRole => ((_weapon != null) && (_character != null) && (_weapon.HasWeapon) && (_character.HasCharacter));

        public Vector3 Position => transform.position;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _weapon     = GetComponent<PlayerWeapon>();
            _character = GetComponent<PlayerCharacter>();
            _status     = GetComponent<PlayerStatus>();
            _controller = GetComponent<PlayerController>();

            CollectCarriers();
        }

        private void CollectCarriers()
        {
            _carriers.Clear();
            GetComponents(_carriers);
            GetComponents(_resettable);
            _carriers.Sort((left, right) => left.CarrierOrder.CompareTo(right.CarrierOrder));
        }

        public override void OnStartServer()
        {
            CacheResultParticipant();
            RunResultParticipantSourceLocator.Register(this);
        }

        public override void OnStopServer()
        {
            RunManager run = RunManager.ServerInstance;
            if ((run != null) && TryCaptureResultState(run.RunId,
                    out RunParticipantStateFact state))
            {
                RunStatisticsSinkLocator.Current.RecordParticipantState(state);
            }

            RunResultParticipantSourceLocator.Unregister(this);
            _resultParticipant = ParticipantId.None;
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 무기와 캐릭터를 한 번에 확정한다 </summary>
        [Server]
        public bool TryCommitRole(WeaponKind weapon, CharacterKind character)
        {
            if ((_weapon == null) || (_character == null))
            {
                Debug.LogError("[PlayerRunAgent] 무기나 캐릭터 컴포넌트가 없어 역할을 확정할 수 없다", this);
                return false;
            }

            if ((_weapon.HasWeapon) || (_character.HasCharacter))
            {
                Debug.LogError("[PlayerRunAgent] 이미 역할이 확정되어 있다", this);
                return false;
            }

            if (!_weapon.ServerSetInitialWeapon(weapon))
            {
                return false;
            }

            if (!_character.ServerSetCharacter(character))
            {
                _weapon.ServerClearForNewRun();
                Debug.LogError("[PlayerRunAgent] 캐릭터 적용에 실패해 무기 확정을 되돌렸다", this);
                return false;
            }

            return true;
        }

        [Server]
        public void ClearRole()
        {
            if (_weapon != null)
            {
                _weapon.ServerClearForNewRun();
            }

            if (_character != null)
            {
                _character.ServerClearForNewRun();
            }
        }

        [Server]
        public void ResetForNewRun()
        {
            if (_status == null)
            {
                return;
            }

            _status.ServerResetForNewRun();
            for (int i = 0; i < _resettable.Count; ++i)
            {
                _resettable[i].ResetForNewRun();
            }
        }

        /// <summary> 자기 컴포넌트들의 값을 그릇에 담는다 </summary>
        [Server]
        public bool CaptureRunState(RunParticipantSnapshot snapshot)
        {
            if ((snapshot == null) || (_carriers.Count == 0))
            {
                return false;
            }

            for (int i = 0; i < _carriers.Count; ++i)
            {
                _carriers[i].Capture(snapshot);
            }

            snapshot.Position   = transform.position;
            snapshot.YawDegrees = transform.eulerAngles.y;
            return true;
        }

        /// <summary> 그릇의 값을 되붓는다 </summary>
        [Server]
        public bool RestoreRunState(RunParticipantSnapshot snapshot)
        {
            if ((snapshot == null) || (_carriers.Count == 0))
            {
                return false;
            }

            for (int i = 0; i < _carriers.Count; ++i)
            {
                if (_carriers[i].Validate(snapshot, out string failure))
                {
                    continue;
                }

                Debug.LogError($"[PlayerRunAgent] 상태를 되부을 수 없다: {failure}", this);
                return false;
            }

            bool restored = true;
            for (int i = 0; i < _carriers.Count; ++i)
            {
                restored &= _carriers[i].Restore(snapshot);
            }

            return restored;
        }

        public bool TryCaptureResultState(RunId run, out RunParticipantStateFact state)
        {
            state = default;
            if ((!isServer) || (!run.IsValid) || (_status == null) ||
                (!CacheResultParticipant()) || float.IsNaN(_status.Health) ||
                float.IsInfinity(_status.Health) || (_status.Health < 0f))
            {
                return false;
            }

            state = ((_character != null) && _character.HasCharacter)
                ? new RunParticipantStateFact(run, _resultParticipant,
                    _character.Character, _status.Health)
                : new RunParticipantStateFact(run, _resultParticipant, _status.Health);
            return state.IsValid;
        }

        private bool CacheResultParticipant()
        {
            if ((_status != null) && _status.Participant.IsValid)
            {
                _resultParticipant = _status.Participant;
            }

            return _resultParticipant.IsValid;
        }

        [Server]
        public void Teleport(Vector3 position)
        {
            if (_controller == null)
            {
                transform.position = position;
                return;
            }

            _controller.ServerTeleport(position);
        }
    }
}
