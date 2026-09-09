// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary> 치유로 받은 일시 피해 버프를 서버 시계와 revision으로 소유한다 </summary>
    [DisallowMultipleComponent]
    public class SupportBuffController : NetworkBehaviour, ICombatBuffProvider,
        ISupportBuffTarget, IRunResettable
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar] private float  _damageMultiplier = 1f;
        [SyncVar] private double _endGameplayTime;
        [SyncVar(hook = nameof(OnRevisionChanged))] private uint _revision;

        private IGrowthParticipant     _participant;
        private IEquipmentWeaponTarget _weaponTarget;

        /****************************************
        *              Properties
        ****************************************/

        public uint Revision => _revision;

        /****************************************
        *                Events
        ****************************************/

        public event Action<uint> RevisionChanged;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            syncMode = SyncMode.Owner;
            _participant  = GetComponent<IGrowthParticipant>();
            _weaponTarget = GetComponent<IEquipmentWeaponTarget>();

            if (_weaponTarget != null)
            {
                _weaponTarget.WeaponChanged += HandleWeaponChanged;
            }
        }

        private void Update()
        {
            if ((!isServer) || (_damageMultiplier <= 1f) ||
                (RunTimeLocator.Current.GameplayElapsedSeconds < _endGameplayTime))
            {
                return;
            }

            ServerClearBuff();
        }

        private void OnDestroy()
        {
            if (_weaponTarget != null)
            {
                _weaponTarget.WeaponChanged -= HandleWeaponChanged;
            }
        }

        /****************************************
        *              Server API
        ****************************************/

        public override void OnStartServer()
        {
            _damageMultiplier = 1f;
            _endGameplayTime  = 0.0;
            _revision         = 1u;
        }

        public override void OnStopServer()
        {
            _damageMultiplier = 1f;
            _endGameplayTime  = 0.0;
        }

        [Server]
        public bool ServerApplySupportBuff(float damageMultiplier, float duration)
        {
            if ((_revision == 0u) || (_revision == uint.MaxValue) ||
                (!IsFinite(damageMultiplier)) || (damageMultiplier <= 1f) ||
                (!IsFinite(duration)) || (duration <= 0f) ||
                (_participant == null) || (!_participant.Participant.IsValid))
            {
                return false;
            }

            double now = RunTimeLocator.Current.GameplayElapsedSeconds;
            float nextMultiplier = Mathf.Max(_damageMultiplier, damageMultiplier);
            double nextEnd = Math.Max(_endGameplayTime, now + duration);
            bool multiplierChanged = !nextMultiplier.Equals(_damageMultiplier);

            _damageMultiplier = nextMultiplier;
            _endGameplayTime  = nextEnd;

            if (multiplierChanged)
            {
                ++_revision;
                NotifyRevisionChanged(_revision);
            }

            return true;
        }

        [Server]
        public void ResetForNewRun()
        {
            _damageMultiplier = 1f;
            _endGameplayTime  = 0.0;
            _revision         = 1u;
            NotifyRevisionChanged(_revision);
        }

        /****************************************
        *              Provider API
        ****************************************/

        public bool TryGetDamageMultiplier(ParticipantId participant,
            out float multiplier)
        {
            multiplier = 1f;
            if ((!isServer) || (_revision == 0u) || (_participant == null) ||
                (!participant.IsValid) || (_participant.Participant != participant))
            {
                return false;
            }

            multiplier = _damageMultiplier;
            return IsFinite(multiplier) && (multiplier >= 1f);
        }

        /****************************************
        *                Methods
        ****************************************/

        [Server]
        private void ServerClearBuff()
        {
            if (_damageMultiplier <= 1f)
            {
                _endGameplayTime = 0.0;
                return;
            }

            if (_revision == uint.MaxValue)
            {
                Debug.LogError("[SupportBuffController] 버프 revision이 고갈됐다", this);
                return;
            }

            _damageMultiplier = 1f;
            _endGameplayTime  = 0.0;
            ++_revision;
            NotifyRevisionChanged(_revision);
        }

        private void HandleWeaponChanged(WeaponKind _)
        {
            if (isServer)
            {
                ServerClearBuff();
            }
        }

        private void OnRevisionChanged(uint _, uint next)
        {
            if (!isServer)
            {
                NotifyRevisionChanged(next);
            }
        }

        private void NotifyRevisionChanged(uint revision)
        {
            Action<uint> handlers = RevisionChanged;
            if (handlers == null)
            {
                return;
            }

            Delegate[] observers = handlers.GetInvocationList();
            for (int i = 0; i < observers.Length; ++i)
            {
                try
                {
                    ((Action<uint>)observers[i])(revision);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[SupportBuffController] 버프 observer 실패: {e}", this);
                }
            }
        }

        private static bool IsFinite(float value)
        {
            return ((!float.IsNaN(value)) && (!float.IsInfinity(value)));
        }
    }
}
