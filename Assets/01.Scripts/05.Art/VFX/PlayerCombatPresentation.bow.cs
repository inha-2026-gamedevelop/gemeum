// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// Third party
using Mirror;

// Project
using Geumeum.Common;
using Geumeum.Combat;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Art
{
    public sealed partial class PlayerCombatPresentation
    {
        private sealed class BowLease
        {
            public PooledVisualLifetime Lifetime;
            public uint Generation;
            public BowLaserVfxView View;
            public double Start;
            public float Elapsed;
            public float Distance;
            public uint Run;
            public uint Scene;
        }

        private readonly List<BowLease> _bowLasers = new List<BowLease>();
        private PooledVisualLifetime _bowCharge;
        private uint _bowChargeLease;
        private BowChargeVfxView _bowChargeView;
        private PlayerStatus _bowStatus;
        private bool _bowHasReleasedHold;
        private double _bowLastReleaseTime = double.NegativeInfinity;
        private uint _bowLastAction;
        private int _bowLastShot;
        private uint _bowLastRun;
        private uint _bowLastScene;
        private bool _bowHasShot;
        private double _bowLaserCueTime = double.NegativeInfinity;

        private bool IsReleasedBowHold => _bowHasReleasedHold && _chargeSource != null &&
            BowContextMatches(_bowLastRun, _bowLastScene) &&
            IsBowHoldReleased(_chargeSource.HoldStartGameTime, _bowLastReleaseTime);

        private static bool BowContextMatches(uint runId, uint sceneRevision)
        {
            return (RunManager.LocalInstance == null ? 0u : RunManager.LocalInstance.RunId.Value) == runId &&
                (RunSceneDirector.LocalInstance == null ? 0u : RunSceneDirector.LocalInstance.Revision.Value) == sceneRevision;
        }

        private void PlayBowLaser(Vector3 origin, Vector3 direction, uint actionId, int shotIndex,
            double time, uint runId, uint sceneRevision)
        {
            if (!isActiveAndEnabled || !NetworkClient.active || !GameDB.IsInitialized ||
                double.IsNaN(time) || double.IsInfinity(time) || !BowContextMatches(runId, sceneRevision) ||
                !float.IsFinite(origin.x) || !float.IsFinite(origin.y) || !float.IsFinite(origin.z) ||
                !float.IsFinite(direction.sqrMagnitude) || direction.sqrMagnitude < .0001f ||
                RunTimeLocator.Current.GameplayElapsedSeconds - time > GameDB.Presentation.LateOneShotToleranceSeconds)
            {
                return;
            }
            if (_bowHasShot && _bowLastRun == runId && _bowLastScene == sceneRevision &&
                _bowLastAction == actionId && _bowLastShot == shotIndex)
            {
                return;
            }
            _bowHasShot = true;
            _bowLastRun = runId; _bowLastScene = sceneRevision;
            _bowLastAction = actionId; _bowLastShot = shotIndex;
            _bowHasReleasedHold = true;
            _bowLastReleaseTime = time;
            StopBowCharge();
            if (_vfxController == null || !_vfxController.TryGetPrefab(PlayerVfxController.BOW_LASER_RELEASE_KEY, out _))
            {
                return;
            }
            direction.Normalize();
            PooledVisualLifetime lifetime = _vfxController.Play(PlayerVfxController.BOW_LASER_RELEASE_KEY,
                origin, Quaternion.LookRotation(direction));
            if (lifetime == null)
            {
                return;
            }
            BowLaserVfxView view = lifetime.GetComponent<BowLaserVfxView>();
            if (view == null)
            {
                lifetime.TryStop(lifetime.LeaseGeneration);
                return;
            }
            view.SetVariation(unchecked(actionId * 2654435761u ^ (uint)shotIndex * 2246822519u));
            // Clamp presentation only; the authoritative projectile owns hit resolution.
            float distance = view.MaxDistance;
            if (Physics.Raycast(origin, direction, out RaycastHit hit, distance,
                GameDB.Arrow.WorldMask, QueryTriggerInteraction.Ignore))
            {
                distance = hit.distance;
            }
            lifetime.SetAutomaticReturnSuppressed(true);
            _visualClock.ObserveCue(time);
            _bowLaserCueTime = time;
            BowLease lease = new BowLease { Lifetime = lifetime, Generation = lifetime.LeaseGeneration,
                View = view, Start = time, Elapsed = Mathf.Max(0f, (float)(RunTimeLocator.Current.GameplayElapsedSeconds - time)),
                Distance = distance, Run = runId, Scene = sceneRevision };
            _bowLasers.Add(lease);
            view.Evaluate(lease.Elapsed, distance);
        }

        private void UpdateBowVisuals(double now)
        {
            for (int i = _bowLasers.Count - 1; i >= 0; --i)
            {
                BowLease lease = _bowLasers[i];
                if (lease.Lifetime == null || !lease.Lifetime.IsRented || lease.Lifetime.LeaseGeneration != lease.Generation)
                {
                    _bowLasers.RemoveAt(i);
                    continue;
                }
                if (!BowContextMatches(lease.Run, lease.Scene) ||
                    lease.Elapsed >= lease.View.Duration)
                {
                    lease.Lifetime.TryStop(lease.Generation);
                    _bowLasers.RemoveAt(i);
                    continue;
                }
                lease.View.Evaluate(lease.Elapsed, lease.Distance);
                lease.Elapsed = AdvanceBowVisualTime(lease.Elapsed, Time.unscaledDeltaTime, RunTimeLocator.Current.IsPaused);
            }
            bool holding = _chargeSource != null &&
                (_chargeSource.State == PrecisionShotState.Holding || _chargeSource.State == PrecisionShotState.Ready) &&
                (_skillVisualWeapon == null || _skillVisualWeapon.Weapon == WeaponKind.Bow) &&
                (_bowStatus == null || (!_bowStatus.IsDead && !_bowStatus.IsSwallowed && !_bowStatus.IsHitReacting)) &&
                !IsReleasedBowHold;
            if (!holding)
            {
                StopBowCharge();
                return;
            }
            if (_bowCharge == null || !_bowCharge.IsRented || _bowCharge.LeaseGeneration != _bowChargeLease)
            {
                _bowCharge = null; _bowChargeView = null;
                if (_vfxController == null || !_vfxController.TryGetPrefab(PlayerVfxController.BOW_CHARGE_KEY, out _))
                {
                    return;
                }
                _bowCharge = _vfxController.Play(PlayerVfxController.BOW_CHARGE_KEY, EffectHandPoint(), transform.rotation);
                if (_bowCharge == null)
                {
                    return;
                }
                _bowChargeLease = _bowCharge.LeaseGeneration;
                _bowChargeView = _bowCharge.GetComponent<BowChargeVfxView>();
                if (_bowChargeView == null)
                {
                    StopBowCharge();
                    return;
                }
                _bowCharge.SetAutomaticReturnSuppressed(true);
            }
            _bowCharge.transform.position = EffectHandPoint();
            Vector2 aim = _abilityFacing != null ? _abilityFacing.AimDirection : Vector2.up;
            if (aim.sqrMagnitude > .0001f)
            {
                _bowCharge.transform.rotation = Quaternion.LookRotation(new Vector3(aim.x, 0f, aim.y));
            }
            float elapsed = Mathf.Max(0f, (float)(now - _chargeSource.HoldStartGameTime));
            _bowChargeView.Evaluate(Mathf.Clamp01(elapsed / Mathf.Max(.001f, GameDB.Combat.PrecisionShotHoldDuration)), elapsed);
        }

        // RPC can precede the Holding snapshot; compare authoritative times instead of the local generation.
        internal static bool IsBowHoldReleased(double holdStart, double releaseTime)
        {
            return holdStart <= releaseTime;
        }

        internal static float AdvanceBowVisualTime(float elapsed, float deltaTime, bool paused)
        {
            return paused ? elapsed : elapsed + Mathf.Max(0f, deltaTime);
        }

        private void StopBowCharge()
        {
            if (_bowCharge != null)
            {
                _bowCharge.TryStop(_bowChargeLease);
            }
            _bowCharge = null; _bowChargeView = null; _bowChargeLease = 0u;
        }

        private void StopBowVisuals()
        {
            StopBowCharge();
            foreach (BowLease lease in _bowLasers)
            {
                if (lease.Lifetime != null)
                {
                    lease.Lifetime.TryStop(lease.Generation);
                }
            }
            _bowLasers.Clear();
            _bowHasShot = false; _bowHasReleasedHold = false;
            _bowLaserCueTime = double.NegativeInfinity;
            _bowLastReleaseTime = double.NegativeInfinity;
        }
    }
}
