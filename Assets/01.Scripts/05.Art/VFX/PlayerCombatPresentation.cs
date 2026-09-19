// Unity
using UnityEngine;

// Third party
using Mirror;

// Project
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Art
{
    /// <summary>Client-only VFX and sound sink for authoritative player action cues.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCombatPresentation : MonoBehaviour, IPlayerActionCueSink
    {
        private NetworkIdentity _identity;
        private IWeaponBladeSource _blade;
        private PooledVisualLifetime _activeVisual;
        private uint _activeVisualLease;
        private PlayerActionCueKind _activeKind;
        private uint _activeExecutionId;

        // 검기는 플레이어에 붙이지 않고 월드에 남아 스스로 사라진다. 다음 행동 때는 반납하지 않고 기록만 멈춘다
        private PooledVisualLifetime _trailVisual;
        private uint _trailVisualLease;

        private void Awake()
        {
            _identity = GetComponent<NetworkIdentity>();
            _blade = GetComponent<IWeaponBladeSource>();
        }

        private void OnDisable()
        {
            StopActiveVisual();
            _activeKind = PlayerActionCueKind.None;
            _activeExecutionId = 0u;
        }

        public void PlayActionCue(in PlayerActionCue cue, float normalizedProgress)
        {
            StopActiveVisual();
            _activeKind = cue.Kind;
            _activeExecutionId = cue.ExecutionId;

            PresentationConfigSO config = ResolveConfig();
            if (config == null)
            {
                return;
            }

            GameObject prefab = ResolveActionPrefab(config, in cue);
            if (IsWorldTrailCue(in cue))
            {
                _trailVisual = RentVisual(prefab, transform.position, Quaternion.identity,
                    null, 0f, out _trailVisualLease);
                if (_trailVisual != null)
                {
                    _trailVisual.PlayCue(in cue, _blade);
                }
            }
            else
            {
                _activeVisual = RentVisual(prefab, transform.position, transform.rotation,
                    transform, normalizedProgress, out _activeVisualLease);
                if (_activeVisual != null)
                {
                    _activeVisual.PlayCue(in cue, _blade);
                }
            }

            if (!PlayerActionCueRules.IsLateOneShot(in cue,
                    RunTimeLocator.Current.GameplayElapsedSeconds,
                    config.LateOneShotToleranceSeconds))
            {
                SoundKey key = ResolveActionSound(in cue);
                if (key != SoundKey.None)
                {
                    SfxPlayer.Play(key, transform.position, IsLocalPlayer,
                        (_identity == null) ? 0u : _identity.netId);
                }
            }
        }

        public void CancelActionCue(PlayerActionCueKind kind, uint executionId)
        {
            if (!PlayerActionCueRules.CanCancel(kind, executionId,
                    _activeKind, _activeExecutionId))
            {
                return;
            }

            StopActiveVisual();
            _activeKind = PlayerActionCueKind.None;
            _activeExecutionId = 0u;
        }

        public void PlayActionImpact(PlayerActionCueKind kind, uint executionId,
            bool successful, bool critical, double impactGameplayTime)
        {
            PresentationConfigSO config = ResolveConfig();
            if ((config == null) ||
                !PlayerCombatPresentationRules.CanPlayEvent(kind, executionId,
                    _activeKind, _activeExecutionId, impactGameplayTime,
                    RunTimeLocator.Current.GameplayElapsedSeconds,
                    config.LateOneShotToleranceSeconds))
            {
                return;
            }

            SoundKey key = SoundKey.None;
            if (kind == PlayerActionCueKind.Attack)
            {
                key = !successful
                    ? SoundKey.MeleeWhiff
                    : (critical ? SoundKey.MeleeCritical : SoundKey.MeleeHit);
            }
            else if (kind == PlayerActionCueKind.Heal)
            {
                key = successful ? SoundKey.HealResolve : SoundKey.None;
            }
            else if (kind == PlayerActionCueKind.Parry)
            {
                key = successful ? SoundKey.ParrySuccess : SoundKey.ParryFail;
            }

            if (key != SoundKey.None)
            {
                SfxPlayer.Play(key, transform.position, IsLocalPlayer,
                    (_identity == null) ? 0u : _identity.netId);
            }
        }

        public void PlayMarkCue(uint executionId, Vector3 worldPosition,
            double appliedGameplayTime)
        {
            PresentationConfigSO config = ResolveConfig();
            if ((config == null) ||
                !PlayerCombatPresentationRules.CanPlayEvent(
                    PlayerActionCueKind.Attack, executionId,
                    _activeKind, _activeExecutionId, appliedGameplayTime,
                    RunTimeLocator.Current.GameplayElapsedSeconds,
                    config.LateOneShotToleranceSeconds))
            {
                return;
            }

            RentVisual(config.MarkPrefab, worldPosition, Quaternion.identity,
                null, 0f, out _);
            SfxPlayer.Play(SoundKey.MarkApplied, worldPosition, IsLocalPlayer,
                (_identity == null) ? 0u : _identity.netId);
        }

        private bool IsLocalPlayer => (_identity != null) && _identity.isLocalPlayer;

        private static bool IsWorldTrailCue(in PlayerActionCue cue)
        {
            return (cue.Kind == PlayerActionCueKind.Attack) && (cue.Weapon == WeaponKind.Sword);
        }

        private static PresentationConfigSO ResolveConfig()
        {
            return GameDB.IsInitialized ? GameDB.Presentation : null;
        }

        private static GameObject ResolveActionPrefab(PresentationConfigSO config,
            in PlayerActionCue cue)
        {
            switch (cue.Kind)
            {
                case PlayerActionCueKind.Attack:
                    if (cue.Weapon == WeaponKind.Sword)
                    {
                        return config.MeleeSlashPrefab;
                    }

                    if (cue.Weapon == WeaponKind.Bell)
                    {
                        return config.BellPulsePrefab;
                    }

                    return null;

                case PlayerActionCueKind.Heal:
                    return config.HealCastPrefab;

                case PlayerActionCueKind.Parry:
                    return config.ParryPrefab;

                default:
                    return null;
            }
        }

        private static SoundKey ResolveActionSound(in PlayerActionCue cue)
        {
            if (cue.Kind == PlayerActionCueKind.Heal)
            {
                return SoundKey.HealCast;
            }

            if (cue.Kind == PlayerActionCueKind.Parry)
            {
                return SoundKey.ParryStart;
            }

            if (cue.Kind != PlayerActionCueKind.Attack)
            {
                return SoundKey.None;
            }

            switch (cue.Weapon)
            {
                case WeaponKind.Bow:
                    return SoundKey.BowFire;

                case WeaponKind.Bell:
                    return SoundKey.BellSwing;

                case WeaponKind.Sword:
                    if (cue.ComboStage >= 3)
                    {
                        return SoundKey.MeleeSwing3;
                    }

                    return (cue.ComboStage == 2)
                        ? SoundKey.MeleeSwing2
                        : SoundKey.MeleeSwing1;

                default:
                    return SoundKey.None;
            }
        }

        private static PooledVisualLifetime RentVisual(GameObject prefab,
            Vector3 position, Quaternion rotation, Transform parent,
            float normalizedProgress, out uint leaseGeneration)
        {
            leaseGeneration = 0u;
            LocalObjectPoolService pool = LocalObjectPoolService.Instance;
            if ((pool == null) || (prefab == null))
            {
                return null;
            }

            GameObject instance = pool.Rent(prefab, position, rotation);
            if (instance == null)
            {
                return null;
            }

            if (parent != null)
            {
                instance.transform.SetParent(parent, true);
            }

            PooledVisualLifetime lifetime = instance.GetComponent<PooledVisualLifetime>();
            if (lifetime == null)
            {
                pool.TryReturn(instance);
                return null;
            }

            lifetime.BeginAtProgress(normalizedProgress);
            leaseGeneration = lifetime.LeaseGeneration;
            return lifetime;
        }

        private void StopActiveVisual()
        {
            if (_activeVisual != null)
            {
                _activeVisual.TryStop(_activeVisualLease);
                _activeVisual = null;
            }

            _activeVisualLease = 0u;

            if (_trailVisual != null)
            {
                _trailVisual.ReleaseCue(_trailVisualLease);
                _trailVisual = null;
            }

            _trailVisualLease = 0u;
        }
    }

    public static class PlayerCombatPresentationRules
    {
        public static bool CanPlayEvent(PlayerActionCueKind kind,
            uint executionId, PlayerActionCueKind activeKind,
            uint activeExecutionId, double eventGameplayTime,
            double gameplayTime, float toleranceSeconds)
        {
            return PlayerActionCueRules.CanCancel(kind, executionId,
                       activeKind, activeExecutionId) &&
                !PlayerActionCueRules.IsLateOneShot(eventGameplayTime,
                    gameplayTime, toleranceSeconds);
        }
    }
}
