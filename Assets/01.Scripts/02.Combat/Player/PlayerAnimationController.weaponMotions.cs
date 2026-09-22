using UnityEngine;
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Combat
{
    public partial class PlayerAnimationController
    {
        private PrecisionShotController _precisionMotion;
        private bool _precisionMotionActive;
        private uint _precisionMotionGeneration;
        private int _timedMotionState;
        private PlayerActionCue _timedMotionCue;
        private double _precisionReleaseTime = double.NegativeInfinity;
        private static readonly int BowDrawState = Animator.StringToHash("Base Layer.BowDraw");
        private static readonly int BowHoldState = Animator.StringToHash("Base Layer.BowHold");
        private static readonly int BowReleaseState = Animator.StringToHash("Base Layer.BowRelease");

        private void UpdatePrecisionMotion()
        {
            if (_precisionMotion == null || !GameDB.IsInitialized || _weapon == null) { return; }
            if (_status != null && (_status.IsDead || _status.IsSwallowed)) { _precisionMotionActive = false; return; }
            if (RunTimeLocator.Current.IsPaused)
            {
                double now = RunTimeLocator.Current.GameplayElapsedSeconds;
                if (_timedMotionCue.ExecutionId == _currentCueExecutionId && _currentCueExecutionId != 0u &&
                    now < _timedMotionCue.EndGameplayTime)
                {
                    float phase = Mathf.Clamp01((float)((now - _timedMotionCue.StartGameplayTime) /
                        (_timedMotionCue.EndGameplayTime - _timedMotionCue.StartGameplayTime)));
                    _animator.Play(_timedMotionState, 0, phase);
                    _animator.Update(0f);
                }
                else if (now >= _precisionReleaseTime && now - _precisionReleaseTime < GameDB.Combat.RangedRecoveryDuration)
                {
                    _animator.Play(BowReleaseState, 0, (float)(now - _precisionReleaseTime) / GameDB.Combat.RangedRecoveryDuration);
                    _animator.Update(0f);
                    return;
                }
            }
            if (_precisionMotion.State == PrecisionShotState.Idle) { _precisionReleaseTime = double.NegativeInfinity; }
            bool holding = _weapon.Weapon == WeaponKind.Bow &&
                (_precisionMotion.State == PrecisionShotState.Holding || _precisionMotion.State == PrecisionShotState.Ready) &&
                _precisionMotion.HoldStartGameTime > _precisionReleaseTime;
            if (_status != null && (_status.IsDead || _status.IsSwallowed || _status.IsHitReacting)) { holding = false; }
            if (holding && _animator.HasState(0, BowDrawState))
            {
                float progress = Mathf.Clamp01((float)((RunTimeLocator.Current.GameplayElapsedSeconds -
                    _precisionMotion.HoldStartGameTime) / GameDB.Combat.PrecisionShotHoldDuration));
                if (!_precisionMotionActive || _precisionMotionGeneration != _precisionMotion.HoldGeneration)
                {
                    _precisionMotionGeneration = _precisionMotion.HoldGeneration;
                    _precisionMotionActive = true;
                    _animator.SetFloat("BowDrawSpeed", .6f / GameDB.Combat.PrecisionShotHoldDuration);
                    _animator.CrossFadeInFixedTime(BowDrawState, .05f, 0, progress * .6f);
                }
                if ((progress >= 1f || _precisionMotion.State == PrecisionShotState.Ready) &&
                    _animator.GetCurrentAnimatorStateInfo(0).fullPathHash != BowHoldState)
                {
                    if (RunTimeLocator.Current.IsPaused) { _animator.Play(BowHoldState, 0, 0f); _animator.Update(0f); }
                    else if (!_animator.IsInTransition(0)) { _animator.CrossFadeInFixedTime(BowHoldState, .025f); }
                }
            }
            else if (_precisionMotionActive)
            {
                _precisionMotionActive = false;
                bool released = _precisionMotion.State == PrecisionShotState.Released || _precisionMotion.State == PrecisionShotState.Recovery;
                // 구르기로 당기기를 끊은 프레임이면 Roll 트리거가 이미 세팅돼 있다
                if (!released && _currentCueExecutionId == 0u &&
                    AllowsLocomotionReturn(IsDeadNow, IsHitReactingNow, IsRollingNow))
                {
                    _animator.CrossFadeInFixedTime(LOCOMOTION_STATE, .06f);
                }
            }
        }

        /// <summary>Use the actual authoritative arrow release event, including remote clients</summary>
        public void PlayPrecisionRelease(double gameplayTime)
        {
            if (GameDB.IsInitialized && RunTimeLocator.Current.GameplayElapsedSeconds - gameplayTime >= GameDB.Combat.RangedRecoveryDuration) { return; }
            _precisionReleaseTime = gameplayTime;
            _precisionMotionActive = false;
            if (_animator == null || !_animator.HasState(0, BowReleaseState) || !GameDB.IsInitialized ||
                (_status != null && (_status.IsDead || _status.IsHitReacting))) { return; }
            float progress = Mathf.Max(0f, (float)(RunTimeLocator.Current.GameplayElapsedSeconds - gameplayTime));
            _animator.SetFloat("BowReleaseSpeed", .35f / GameDB.Combat.RangedRecoveryDuration);
            _animator.CrossFadeInFixedTime(BowReleaseState, .015f, 0,
                progress * .35f / GameDB.Combat.RangedRecoveryDuration);
        }
    }
}
