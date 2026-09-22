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

namespace Geumeum.Systems
{
    /// <summary>
    /// 한 런의 파티 성장 상태를 서버에서 소유한다
    /// 적 사망 이벤트만 구독하므로 AI와 성장 시스템 사이에 직접 참조가 생기지 않는다
    /// </summary>
    [DisallowMultipleComponent]
    public partial class GrowthManager : NetworkBehaviour, IGrowthProvider
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly HashSet<uint> _awardedEnemyNetIds = new HashSet<uint>();
        private readonly List<IGrowthParticipant> _participants = new List<IGrowthParticipant>();

        private GrowthState _state;
        private bool       _hasState;
        private bool       _isRunActive;
        private bool       _hasEventBusSubscription;
        private Coroutine  _eventBusWaitRoutine;

        /****************************************
        *              Properties
        ****************************************/

        public GrowthState State             => _hasState
            ? _state
            : new GrowthState(_networkLevel, _networkExperience, _networkMaxHealth,
                _networkRevision, _networkTalentPoints);
        public bool        HasState          => (_hasState || (_networkRevision > 0u));
        public bool        IsRunActive       => _isRunActive;
        public int         Level             => State.Level;
        public int         Experience        => State.Experience;
        public float       MaxHealth         => State.MaxHealth;
        public int         AwardedEnemyCount => _awardedEnemyNetIds.Count;

        /****************************************
        *                Events
        ****************************************/

        /// <summary> 공용 성장 상태가 바뀔 때 서버와 UI 중계자가 받는다 </summary>
        public event Action<GrowthState> GrowthChanged;

        /****************************************
        *              Provider API
        ****************************************/

        public bool TryGetState(ParticipantId participant, out GrowthState state)
        {
            state = default;

            if ((!participant.IsValid) || (!HasState))
            {
                return false;
            }

            state = State;
            return true;
        }

        /****************************************
        *           Client Callbacks
        ****************************************/

        public override void OnStartClient()
        {
            GrowthProviderLocator.Register(this);
        }

        public override void OnStopClient()
        {
            if (!isServer)
            {
                GrowthProviderLocator.Unregister(this);
            }
        }

        /****************************************
        *             SyncVar Hooks
        ****************************************/

        private void OnNetworkGrowthRevisionChanged(uint _, uint __)
        {
            if (!isServer)
            {
                GrowthChanged?.Invoke(State);
            }
        }
    }
}
