// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary>
    /// 공명진이 준 공격력 버프를 원천별로 소유한다
    /// 진마다 만료가 독립이고 적용값은 유효한 것 중 가장 큰 하나다 -
    /// 겹친 진이나 반복 입장으로 계수가 누적되면 같은 자리에 두 번 깔기만 하면 되는 게임이 된다
    /// </summary>
    [DisallowMultipleComponent]
    public class SupportBuffController : NetworkBehaviour, ICombatBuffProvider,
        ISupportBuffTarget, IRunResettable
    {
        /****************************************
        *              Constants
        ****************************************/

        // 파티 정원만큼의 진이 동시에 겹칠 수 있다. 그보다 많이 들고 있을 이유가 없다
        private const int SOURCE_CAPACITY = Constants.Player.PARTY_SIZE;

        /****************************************
        *                Types
        ****************************************/

        private struct Entry
        {
            public ResonanceFieldSource Source;
            public float                Multiplier;
            public double               ExpiresAt;
        }

        /****************************************
        *                Fields
        ****************************************/

        // 화면은 서버가 확정한 값만 본다. 소유자 HUD가 실제 효과량과 남은 시간을 그린다
        [SyncVar] private float  _damageMultiplier = 1f;
        [SyncVar] private double _endGameplayTime;
        [SyncVar] private double _refreshedGameplayTime;

        private readonly List<Entry> _serverSources = new List<Entry>(SOURCE_CAPACITY);

        private IGrowthParticipant     _participant;
        private IEquipmentWeaponTarget _weaponTarget;
        private IDamageable            _damageable;
        private IBossSwallowTarget     _swallowTarget;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 지금 적용 중인 공격력 계수. 1이면 버프가 없다 </summary>
        public float ActiveDamageMultiplier => _damageMultiplier;

        /// <summary> 유효한 원천 중 가장 늦은 만료 시각(게임 시계) </summary>
        public double ActiveExpiryGameplayTime => _endGameplayTime;

        /// <summary> 방금 진 안에서 갱신됐는가. HUD의「영역 내 갱신 중」표시가 쓴다 </summary>
        public bool IsRefreshing(double now, float sampleInterval)
        {
            if (_damageMultiplier <= 1f)
            {
                return false;
            }

            double grace = Math.Max(0.05f, sampleInterval) * 2d;
            return (now - _refreshedGameplayTime) <= grace;
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            syncMode = SyncMode.Owner;
            _participant   = GetComponent<IGrowthParticipant>();
            _weaponTarget  = GetComponent<IEquipmentWeaponTarget>();
            _damageable    = GetComponent<IDamageable>();
            _swallowTarget = GetComponent<IBossSwallowTarget>();

            if (_weaponTarget != null)
            {
                _weaponTarget.WeaponChanged += HandleWeaponChanged;
            }
        }

        private void Update()
        {
            if ((!isServer) || (_serverSources.Count == 0))
            {
                return;
            }

            // 쓰러지거나 삼켜지면 받은 버프는 그 자리에서 끝난다
            // 복귀한 뒤 진 안에 다시 서야 새로 받는다
            if (((_damageable != null) && (_damageable.IsDead)) ||
                ((_swallowTarget != null) && (_swallowTarget.IsSwallowed)))
            {
                ServerClearResonance();
                return;
            }

            PruneAndPublish(RunTimeLocator.Current.GameplayElapsedSeconds);
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
            ClearAll();
        }

        public override void OnStopServer()
        {
            ClearAll();
        }

        [Server]
        public bool ServerApplyResonance(ResonanceFieldSource source,
            float damageMultiplier, double expiresAtGameplayTime)
        {
            if ((!source.IsValid) || (!IsFinite(damageMultiplier)) ||
                (damageMultiplier <= 1f) || (!IsFinite(expiresAtGameplayTime)) ||
                (_participant == null) || (!_participant.Participant.IsValid))
            {
                return false;
            }

            double now = RunTimeLocator.Current.GameplayElapsedSeconds;
            if (expiresAtGameplayTime <= now)
            {
                return false;
            }

            for (int i = 0; i < _serverSources.Count; ++i)
            {
                if (!_serverSources[i].Source.Equals(source))
                {
                    continue;
                }

                Entry existing = _serverSources[i];
                existing.Multiplier = damageMultiplier;

                // 진 안에서는 만료를 미루기만 한다. 뒤로 당기면 나갔다 들어오는 것이 이득이 된다
                existing.ExpiresAt = Math.Max(existing.ExpiresAt, expiresAtGameplayTime);
                _serverSources[i] = existing;
                _refreshedGameplayTime = now;
                PruneAndPublish(now);
                return true;
            }

            if (_serverSources.Count >= SOURCE_CAPACITY)
            {
                PruneAndPublish(now);
                if (_serverSources.Count >= SOURCE_CAPACITY)
                {
                    Debug.LogWarning("[SupportBuffController] 동시 공명진 원천이 정원을 넘어 새 진을 받지 못했다", this);
                    return false;
                }
            }

            _serverSources.Add(new Entry
            {
                Source     = source,
                Multiplier = damageMultiplier,
                ExpiresAt  = expiresAtGameplayTime,
            });

            _refreshedGameplayTime = now;
            PruneAndPublish(now);
            return true;
        }

        [Server]
        public bool ServerRemoveResonance(ResonanceFieldSource source)
        {
            if (!source.IsValid)
            {
                return false;
            }

            for (int i = 0; i < _serverSources.Count; ++i)
            {
                if (!_serverSources[i].Source.Equals(source))
                {
                    continue;
                }

                _serverSources.RemoveAt(i);
                PruneAndPublish(RunTimeLocator.Current.GameplayElapsedSeconds);
                return true;
            }

            return false;
        }

        [Server]
        public void ServerClearResonance()
        {
            _serverSources.Clear();
            Publish(1f, 0d);
        }

        [Server]
        public void ResetForNewRun()
        {
            ClearAll();
        }

        /****************************************
        *              Provider API
        ****************************************/

        public bool TryGetDamageMultiplier(ParticipantId participant,
            out float multiplier)
        {
            multiplier = 1f;
            if ((!isServer) || (_participant == null) || (!participant.IsValid) ||
                (_participant.Participant != participant))
            {
                return false;
            }

            // 조회 시점에 이미 끝난 원천은 적용하지 않는다. 다음 Update를 기다리면
            // 비용 확정이 한 틱 빨랐다는 이유로 끝난 버프가 한 번 더 곱해진다
            multiplier = Resolve(RunTimeLocator.Current.GameplayElapsedSeconds);
            return true;
        }

        /****************************************
        *                Methods
        ****************************************/

        // 아래 보조 메서드는 이미 서버 경로에서만 불린다. Mirror 특성을 또 붙이면
        // 서버가 서지 않은 EditMode 초기화에서 경고만 쌓인다
        private void ClearAll()
        {
            _serverSources.Clear();
            _refreshedGameplayTime = 0d;
            Publish(1f, 0d);
        }

        private void PruneAndPublish(double now)
        {
            for (int i = _serverSources.Count - 1; i >= 0; --i)
            {
                if (_serverSources[i].ExpiresAt <= now)
                {
                    _serverSources.RemoveAt(i);
                }
            }

            float  multiplier = 1f;
            double end        = 0d;
            for (int i = 0; i < _serverSources.Count; ++i)
            {
                multiplier = Mathf.Max(multiplier, _serverSources[i].Multiplier);
                end        = Math.Max(end, _serverSources[i].ExpiresAt);
            }

            Publish(multiplier, (multiplier > 1f) ? end : 0d);
        }

        private float Resolve(double now)
        {
            float multiplier = 1f;
            for (int i = 0; i < _serverSources.Count; ++i)
            {
                if (_serverSources[i].ExpiresAt > now)
                {
                    multiplier = Mathf.Max(multiplier, _serverSources[i].Multiplier);
                }
            }

            return IsFinite(multiplier) ? Mathf.Max(1f, multiplier) : 1f;
        }

        private void Publish(float multiplier, double end)
        {
            _damageMultiplier = multiplier;
            _endGameplayTime  = end;
        }

        private void HandleWeaponChanged(WeaponKind _)
        {
            if (isServer)
            {
                ServerClearResonance();
            }
        }

        private static bool IsFinite(float value)
        {
            return ((!float.IsNaN(value)) && (!float.IsInfinity(value)));
        }

        private static bool IsFinite(double value)
        {
            return ((!double.IsNaN(value)) && (!double.IsInfinity(value)));
        }
    }
}
