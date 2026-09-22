using UnityEngine;
using Mirror;
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Combat
{
    public partial class PlayerAttack
    {
        [SyncVar] private byte _reservedCounter;
        [SyncVar] private double _combatTokenEnd;
        private byte _comboToken;
        private bool _rollBasicToken;
        private double _lastRoll = double.NegativeInfinity;
        private float _wedgeRollDiscount;
        private double _wedgeRollEnd;
        private Vector2 _skillStepDirection;
        private float _skillStepRemaining;
        private uint _combatGeneration = 1;
        public uint CombatGeneration => _combatGeneration;
        public bool CanApplyArrowTalent => _status != null && !_status.IsDead && !_status.IsSwallowed && _weapon != null && _weapon.HasWeapon && _weapon.Weapon == WeaponKind.Bow;
        public byte CounterMarks => _gauge != null ? _gauge.CounterMarks : (byte)0;
        public bool HasCombatTalentToken => AbilityTime < _combatTokenEnd || AbilityTime < _wedgeRollEnd;
        [SyncVar] private double _actionFacingEnd;
        [SyncVar] private float _basicBufferBonus;
        private System.Action _bufferedAction;
        private int _bufferPriority;
        private uint _bufferRun, _bufferScene;
        private WeaponKind _bufferWeapon;
        private double _queuedBasicDeadline;
        public bool UsesActionFacing => IsWeaponSkillActive || AbilityTime < _actionFacingEnd;
        public Vector2 ActionFacing => IsWeaponSkillActive ? WeaponSkillFacing :
            (_hasRequestedAim ? _requestedAim : Vector2.up);

        [Server]
        public float ResolveRollCost(float cost) => Mathf.Max(0f, cost -
            (AbilityTime < _wedgeRollEnd ? _wedgeRollDiscount : 0f));

        [Server]
        public void ServerDidRoll()
        {
            _gauge?.ServerReset();
            _comboToken = 0;
            CombatStatsSnapshot stats = default;
            _loadoutProvider?.TryBuildLoadout(ResolveParticipantId(), out stats);
            _rollBasicToken = stats.RollBasicStaminaReduction > 0 || stats.BowRollBasicReduction > 0 || stats.RollShotRecoveryReduction > 0;
            _lastRoll = AbilityTime;
            _combatTokenEnd = _rollBasicToken ? AbilityTime + GameDB.Combat.CombatTokenDuration :
                stats.RollSkillDamagePercent > 0 ? AbilityTime + GameDB.Combat.RollSkillWindow : 0;
            _wedgeRollDiscount = 0; _wedgeRollEnd = 0;
        }

        [Server]
        public void ServerDiscardCombatRewardsOnDamage()
        {
            _gauge?.ServerReset();
            ClearCombatTalentTokens();
            if (_reservedCounter > 0)
            {
                CancelWeaponSkill(false);
            }
        }

        private void ClearCombatTalentTokens()
        {
            _bufferedAction = null; _actionFacingEnd = 0;
            _comboToken = 0; _rollBasicToken = false; _combatTokenEnd = 0;
            _wedgeRollDiscount = 0; _wedgeRollEnd = 0;
            _lastRoll = double.NegativeInfinity;
        }

        public bool TryBufferCombatAction(int priority, System.Action action)
        {
            if (!isLocalPlayer || !GameDB.IsInitialized || LocalPlacementAim.SuppressesCombatInput)
            {
                return false;
            }

            if (RunTimeLocator.Current.IsPaused) { _bufferedAction = null; return false; }
            double end = System.Math.Max(_animation != null ? _animation.ActionCueEndGameplayTime : 0d, System.Math.Max(_actionFacingEnd, System.Math.Max(_skillLockEnd, _echoLockEnd)));
            double remaining = end - AbilityTime;
            float window = GameDB.Combat.AttackBufferWindow + (priority == 1 ? _basicBufferBonus : 0f);
            if (remaining <= 0)
            {
                var pending = _bufferedAction;
                bool keepPriority = pending != null && priority < _bufferPriority && AbilityTime <= _queuedBasicDeadline;
                _bufferedAction = null;
                if (keepPriority) { pending(); return true; }
                return false;
            }
            if (remaining > window) { _bufferedAction = null; return false; }
            if (_bufferedAction == null || priority >= _bufferPriority)
            {
                _bufferedAction = action; _bufferPriority = priority;
                _queuedBasicDeadline = end + GameDB.Combat.AttackBufferWindow;
                _bufferRun = RunManager.LocalInstance != null ? RunManager.LocalInstance.RunId.Value : 0;
                _bufferScene = RunSceneDirector.LocalInstance != null ? RunSceneDirector.LocalInstance.Revision.Value : 0;
                _bufferWeapon = _weapon != null ? _weapon.Weapon : default;
            }
            return true;
        }

        private bool UpdateBasicBuffer(bool allowsInput)
        {
            if (RunTimeLocator.Current.IsPaused ||
                (RunManager.LocalInstance != null && RunManager.LocalInstance.RunId.Value != _bufferRun) ||
                (RunSceneDirector.LocalInstance != null && (RunSceneDirector.LocalInstance.Revision.Value != _bufferScene || RunSceneDirector.LocalInstance.State != RunTransitionState.Idle)) ||
                (_weapon != null && _weapon.Weapon != _bufferWeapon) ||
                !allowsInput || _status == null || _status.IsDead || _status.IsSwallowed || _status.IsHitReacting ||
                AbilityTime > _queuedBasicDeadline)
            { _bufferedAction = null; return false; }
            // 같은 프레임의 더 높은 우선순위 입력은 각 입력 소유자가 먼저 등록한다
            if (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Q) || Input.GetMouseButtonDown(1) ||
                Input.GetKeyDown(Constants.Player.KEY_ROLL) || Input.GetKeyDown(_attackKey))
            {
                return false;
            }

            double end = System.Math.Max(_animation != null ? _animation.ActionCueEndGameplayTime : 0d, System.Math.Max(_actionFacingEnd, System.Math.Max(_skillLockEnd, _echoLockEnd)));
            if (_bufferedAction != null && AbilityTime >= end)
            { var action = _bufferedAction; _bufferedAction = null; action(); return true; }
            return false;
        }

        private AttackProfile ApplyBasicTalents(AttackProfile profile, in CombatStatsSnapshot stats)
        {
            bool bow = _weapon.Weapon == WeaponKind.Bow;
            bool sword = _weapon.Weapon == WeaponKind.Sword;
            if (!bow && !sword)
            {
                return profile;
            }

            bool rolled = _rollBasicToken && AbilityTime < _combatTokenEnd;
            float reduction = bow ? stats.BowBasicStaminaReduction : stats.BasicStaminaReduction;
            if (rolled)
            {
                reduction += bow ? stats.BowRollBasicReduction : stats.RollBasicStaminaReduction;
            }

            float recovery = profile.RecoveryDuration;
            if (bow && rolled)
            {
                recovery = Mathf.Max(0f, recovery - stats.RollShotRecoveryReduction);
            }

            return new AttackProfile(profile.Reach, profile.StartupDuration, recovery,
                Mathf.Max(0f, profile.StaminaCost - reduction));
        }

        private void ResolveSwordSkillOutcome(bool hit)
        {
            double originalEnd = _skillLockEnd;
            if (hit && _reservedCounter >= 2)
            {
                _skillLockEnd = System.Math.Max(AbilityTime, _skillLockEnd - _skillStats.CounterRecoveryReduction);
            }

            if (!hit && _reservedCounter == 0)
            {
                _skillLockEnd = System.Math.Max(AbilityTime, _skillLockEnd - _skillStats.MissSkillRecoveryReduction);
            }

            byte next = hit && _reservedCounter == 3 && _skillStats.CounterThirdCombo > 0 ? (byte)3 :
                hit && _reservedCounter == 0 && _skillStats.SkillSecondCombo > 0 ? (byte)2 : (byte)0;
            if (next > 0)
            {
                _comboToken = next;
                _combatTokenEnd = AbilityTime + GameDB.Combat.CombatTokenDuration;
            }
            if (_skillLockEnd != originalEnd)
            {
                RpcSkillRecovery(_skillExecution, _skillLockEnd);
            }
        }
    }
}
