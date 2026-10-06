using System;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Combat
{
    public partial class PlayerAttack
    {
        /// <summary> 설치 좌표를 보내지 않은 요청. 방울만 좌표를 요구한다 </summary>
        private static readonly Vector3 NO_PLACEMENT =
            new Vector3(float.NaN, float.NaN, float.NaN);

        private static uint G_NextFieldId = 1u;

        [SyncVar] private double _swordSkillReady;
        [SyncVar] private double _bowSkillReady;
        [SyncVar] private double _bellSkillReady;
        [SyncVar] private double _skillLockEnd;
        [SyncVar] private double _fieldEnd;
        [SyncVar] private Vector3 _fieldPoint;
        [SyncVar] private float _fieldRadius;
        [SyncVar] private float _fieldMultiplier = 1f;
        private uint _skillExecution;
        private uint _nextSkillRequest = 1;
        private RequestIdTracker _skillRequests;
        private uint _skillRun, _skillScene;
        [SyncVar] private double _skillImpact;
        [SyncVar] private double _skillSlideEnd;
        private Vector3 _skillPreviousPosition;
        private bool _skillSlideHit, _skillSlideBlocked, _skillSlideFinished;
        private float _skillSlideProgress;
        private bool _skillCommitted;
        private WeaponKind _skillWeapon;
        [SyncVar] private Vector3 _skillForward;
        private Vector3 _skillPoint;
        private float _skillPower, _skillCost;
        private float _skillResonance = 1f;
        private CombatStatsSnapshot _skillStats;
        private CombatConfigSO.WeaponSkillSettings _skillSettings;

        // 활성 진. 시전자당 하나이고, 폐기 방식에 따라 남은 버프를 거두거나 남긴다
        private ResonanceFieldSource _fieldSource;
        private double _fieldLastGrantedExpiry;
        private float _fieldTail;
        private double _fieldNextSample;
        private bool _fieldEchoExtended;
        private bool _fieldAfterglowUsed;

        private readonly HashSet<uint> _skillHitEntities = new HashSet<uint>();
        private readonly List<SkillTarget> _skillTargets = new List<SkillTarget>();

        private struct SkillTarget
        {
            public AbilityTarget Target;
            public Vector3 Point;
            public float Angle, Distance;
        }

        public bool IsWeaponSkillActive => AbilityTime < _skillLockEnd;
        public Vector2 WeaponSkillFacing => new Vector2(_skillForward.x, _skillForward.z);
        public bool IsBowSkillSliding => IsWeaponSkillActive && _weapon != null && _weapon.Weapon == WeaponKind.Bow &&
            AbilityTime >= _skillImpact && AbilityTime < _skillSlideEnd;
        public double BellFieldEnd => _fieldEnd;
        public Vector3 BellFieldPoint => _fieldPoint;
        public float BellFieldRadius => _fieldRadius;
        public float BellFieldMultiplier => _fieldMultiplier;
        public float ReservedSkillStamina => _skillExecution != 0 && !_skillCommitted && _skillWeapon != WeaponKind.Bell ? _skillCost : 0f;
        public float ReservedSkillSpirit => _skillExecution != 0 && !_skillCommitted && _skillWeapon == WeaponKind.Bell ? _skillCost : 0f;
        public float WeaponSkillCooldownRemaining => _weapon == null || (_weapon.Weapon == WeaponKind.Sword && CounterMarks > 0) ? 0f : SkillCooldownRemaining(_weapon.Weapon);
        public float SkillCooldownRemaining(WeaponKind weapon) => (float)Math.Max(0d,
            (weapon == WeaponKind.Sword ? _swordSkillReady : weapon == WeaponKind.Bow ? _bowSkillReady : _bellSkillReady) - AbilityTime);

        /// <summary> 검과 활의 R. 방울은 지면을 먼저 확인해야 해서 이 경로로 들어오지 않는다 </summary>
        public bool RequestWeaponSkill()
        {
            if (_weapon != null && _weapon.HasWeapon && _weapon.Weapon == WeaponKind.Bell) { return false; }
            if (TryBufferCombatAction(2, () => RequestWeaponSkill()))
            {
                return true;
            }

            return SendSkillRequest(NO_PLACEMENT);
        }

        /// <summary> 조준을 마친 방울이 확인한 지면 좌표로 설치를 요청한다 </summary>
        public bool RequestBellFieldPlacement(Vector3 point)
        {
            if (_weapon == null || !_weapon.HasWeapon || _weapon.Weapon != WeaponKind.Bell ||
                !IsFinite(point)) { return false; }
            return SendSkillRequest(point);
        }

        private bool SendSkillRequest(Vector3 point)
        {
            if (!isLocalPlayer || !LocalGameplayInputGate.AllowsGameplayInput) { return false; }
            if (!NetworkClient.active || _status == null || _weapon == null || !_weapon.HasWeapon)
            {
                ActionRejectionChat.Show(ActionRejection.NotReady);
                return false;
            }
            if (_status.IsHitReacting)
            {
                ActionRejectionChat.Show(ActionRejection.State);
                return false;
            }
            RunManager run = RunManager.LocalInstance;
            RunSceneDirector scene = RunSceneDirector.LocalInstance;
            if (run == null || scene == null)
            {
                ActionRejectionChat.Show(ActionRejection.NotReady);
                return false;
            }
            // 설치는 커서가 정하지만 몸이 보는 방향은 진 쪽이어야 눈과 판정이 어긋나지 않는다
            Vector2 aim = IsFinite(point)
                ? ResolvePlanarAim(point - transform.position)
                : CameraFrame.AimDirection;
            _presentation?.ClientPredictFacing(aim);
            _clientMoveLockUntilTime = NetworkTime.time + Constants.Combat.CLIENT_ATTACK_MOVE_LOCK;
            _placementRequest = _nextSkillRequest;
            // Host TargetRpc may run synchronously during CmdWeaponSkill
            if (IsFinite(point)) { _placementAwaitingServer = true; }
            CmdWeaponSkill(_nextSkillRequest++, run.RunId.Value, scene.Revision.Value, aim, point);
            return true;
        }

        private Vector2 ResolvePlanarAim(Vector3 delta)
        {
            Vector2 planar = new Vector2(delta.x, delta.z);
            return planar.sqrMagnitude > .0001f ? planar.normalized : CameraFrame.AimDirection;
        }

        [Command]
        private void CmdWeaponSkill(uint request, uint run, uint scene, Vector2 aim, Vector3 point)
        {
            if (_status != null && _status.Participant.Kind == ParticipantKind.Bot) { return; }
            ActionRejection rejection = ServerRequestWeaponSkill(request, run, scene, aim, point);
            if (IsFinite(point) && connectionToClient != null) { TargetPlacementResult(request, rejection); }
            else if (rejection != ActionRejection.None && connectionToClient != null)
            {
                TargetActionRejected(rejection, _weapon != null && _weapon.Weapon == WeaponKind.Bell,
                    WeaponSkillCooldownRemaining > 0f);
            }
        }

        [Server]
        public ActionRejection ServerRequestWeaponSkill(uint request, uint run, uint scene, Vector2 aim)
        { return ServerRequestWeaponSkill(request, run, scene, aim, NO_PLACEMENT); }

        [Server]
        public ActionRejection ServerRequestWeaponSkill(uint request, uint run, uint scene, Vector2 aim, Vector3 desiredPoint)
        {
            if (!_skillRequests.TryAccept(request)) { return ActionRejection.Duplicate; }
            if (!IsFinite(aim.x) || !IsFinite(aim.y) || aim.sqrMagnitude < .001f) { return ActionRejection.InvalidInput; }
            RunManager manager = RunManager.ServerInstance;
            RunSceneDirector director = RunSceneDirector.ServerInstance;
            if (!GameDB.IsInitialized || _status == null || manager == null || director == null ||
                manager.RunId.Value != run || director.Revision.Value != scene || RunSceneDirector.IsServerTransitioning ||
                !PlayerActionGate.CanAct(manager.Phase, _status.LifeState, manager.IsPaused)) { return ActionRejection.State; }
            if (_weapon == null || !_weapon.HasWeapon) { return ActionRejection.NoWeapon; }
            if (_status.IsHitReacting || IsActionInProgress(NetworkTime.time) ||
                (_roll != null && _roll.IsRolling) || (_parry != null && _parry.IsActionInProgress(NetworkTime.time)) ||
                (_abilityHeal != null && _abilityHeal.IsActionInProgress(NetworkTime.time)) ||
                WeaponSkillCooldownRemaining > 0) { return ActionRejection.Busy; }
            if (_loadoutProvider == null || !_loadoutProvider.TryBuildLoadout(ResolveParticipantId(), out CombatStatsSnapshot stats) ||
                !stats.IsValid) { return ActionRejection.NotReady; }
            WeaponKind kind = _weapon.Weapon;
            if (kind != WeaponKind.Sword && kind != WeaponKind.Bow && kind != WeaponKind.Bell) { return ActionRejection.NoWeapon; }
            var settings = GameDB.Combat.Skill(kind);
            byte counter = kind == WeaponKind.Sword ? CounterMarks : (byte)0;
            if (counter > 0)
            {
                settings.Startup = GameDB.Combat.CounterStartup;
                settings.Range = GameDB.Combat.CounterRange;
                settings.Angle = GameDB.Combat.CounterAngle;
                settings.DamageMultiplier = GameDB.Combat.CounterDamage(counter);
            }
            else if (kind == WeaponKind.Sword && AbilityTime - _lastRoll <= GameDB.Combat.RollSkillWindow)
            {
                settings.DamageMultiplier *= 1f + stats.RollSkillDamagePercent * .01f;
            }

            float cost = kind == WeaponKind.Bell
                ? stats.ResolveSpiritCost(settings.Cost, stats.SkillSpiritCostReduction)
                : Mathf.Max(0f, stats.ResolveStaminaCost(settings.Cost) - (kind == WeaponKind.Bow ? stats.BowSkillStaminaReduction : 0f));
            float power = kind == WeaponKind.Bow ? stats.RangedAttackPower : stats.MeleeAttackPower;
            if (!IsFinite(cost) || !IsFinite(power) || cost < 0 || power <= 0) { return ActionRejection.NotReady; }
            if ((kind == WeaponKind.Bell ? _status.Spirit : _status.Stamina) < cost) { return ActionRejection.Resource; }
            if (kind == WeaponKind.Sword && _presentation != null && _presentation.TryGetLockOnFacing(out Vector2 lockedFacing))
            {
                aim = lockedFacing;
            }

            Vector3 forward = new Vector3(aim.x, 0, aim.y).normalized;
            Vector3 point = transform.position;
            // 클라이언트가 그린 미리보기가 아니라 서버가 다시 잰 지면만 설치 위치가 된다
            if (kind == WeaponKind.Bell && !TryResolveFieldPlacement(desiredPoint, out point)) { return ActionRejection.InvalidInput; }
            _reservedCounter = counter > 0 ? _gauge.ServerReserveCounter() : (byte)0;
            _skillStepRemaining = kind == WeaponKind.Sword && counter > 0 ? GameDB.Combat.SwordStepDistance : 0f;
            _skillStepDirection = aim.normalized;
            if (kind == WeaponKind.Sword && counter == 0 && stats.SideStepSkill > 0 && _presentation != null)
            {
                Vector2 right = new Vector2(aim.y, -aim.x).normalized;
                float side = Vector2.Dot(_presentation.MoveDirection, right);
                if (Mathf.Abs(side) > .1f) { _skillStepDirection = right * Mathf.Sign(side); _skillStepRemaining = GameDB.Combat.SwordStepDistance; }
            }
            _skillWeapon = kind; _skillForward = forward; _skillPoint = point;
            _skillSettings = settings; _skillCost = cost; _skillPower = power; _skillStats = stats;
            _skillRun = run; _skillScene = scene;
            _skillExecution = TakeNextServerActionId(); _skillCommitted = false;
            _skillResonance = 1f;
            _skillSlideHit = _skillSlideBlocked = _skillSlideFinished = false;
            _skillSlideProgress = 0;
            _skillPreviousPosition = transform.position;
            _skillImpact = AbilityTime + stats.ResolveAttackDuration(settings.Startup);
            _skillSlideEnd = _skillImpact + (kind == WeaponKind.Bow ? stats.ResolveAttackDuration(GameDB.Combat.BowSkillSlideDuration) : 0);
            _skillLockEnd = _skillSlideEnd + stats.ResolveAttackDuration(settings.Recovery);
            ServerApplyAim(aim);
            RpcSkillStarted(new PlayerActionCue { ExecutionId = _skillExecution, Kind = PlayerActionCueKind.WeaponSkill,
                Weapon = kind, ComboStage = counter, StartGameplayTime = AbilityTime, ImpactGameplayTime = _skillImpact, EndGameplayTime = _skillLockEnd });
            return ActionRejection.None;
        }

        /// <summary>
        /// 요청한 좌표가 설치 가능한 지면인지 본다. 거리, 높이차, 지면 기울기, 시야를 모두 서버가 다시 잰다 -
        /// 클라이언트 고리만 믿으면 절벽 너머나 벽 뒤에 진을 놓을 수 있다
        /// </summary>
        public bool TryResolveFieldPlacement(Vector3 desired, out Vector3 point)
        {
            point = default;
            if (!GameDB.IsInitialized || GameDB.Arrow == null || GameDB.Combat == null || !IsFinite(desired)) { return false; }
            var config = GameDB.Combat;
            Vector3 origin = transform.position;
            Vector3 planar = new Vector3(desired.x - origin.x, 0, desired.z - origin.z);
            float range = config.Skill(WeaponKind.Bell).Range;
            if (planar.sqrMagnitude > range * range) { return false; }
            if (!Physics.Raycast(new Vector3(desired.x, desired.y + config.SkillGroundProbeHeight, desired.z), Vector3.down,
                out RaycastHit hit, config.SkillGroundProbeHeight + config.SkillGroundProbeDepth,
                GameDB.Arrow.WorldMask, QueryTriggerInteraction.Ignore) || hit.normal.y < .5f) { return false; }
            if (Mathf.Abs(hit.point.y - origin.y) > config.BellFieldHeightTolerance) { return false; }
            if (Physics.Linecast(origin + Vector3.up * config.RangedMuzzleHeight,
                hit.point + Vector3.up * config.BellFieldSightOffset, GameDB.Arrow.WorldMask,
                QueryTriggerInteraction.Ignore)) { return false; }
            point = hit.point;
            return true;
        }

        [Server]
        private void ServerUpdateWeaponSkill()
        {
            if (_skillExecution == 0 && !_fieldSource.IsValid) { return; }
            var run = RunManager.ServerInstance; var scene = RunSceneDirector.ServerInstance;
            if (_status == null || _status.IsDead || _status.IsSwallowed || _weapon == null || !_weapon.HasWeapon ||
                run == null || scene == null || run.RunId.Value != _skillRun || scene.Revision.Value != _skillScene ||
                RunSceneDirector.IsServerTransitioning) { CancelWeaponSkill(true); return; }
            if (run.IsPaused) { return; }
            if (_skillExecution != 0 && !_skillCommitted && AbilityTime >= _skillImpact)
            {
                if (_reservedCounter > 0 && (_gauge == null || !_gauge.CounterTargetAlive))
                { CancelWeaponSkill(false); return; }
                _skillCommitted = true; // Release the reservation before the atomic spend
                if (_status.IsHitReacting || !(_skillWeapon == WeaponKind.Bell ? _status.TrySpendSpirit(_skillCost) : _status.TrySpendStamina(_skillCost)))
                { CancelWeaponSkill(false); return; }
                // 비용이 확정되는 이 순간의 계수를 이 행동 전체가 끝까지 쓴다
                _skillResonance = ResolveResonanceMultiplier();
                double ready = AbilityTime + _skillSettings.Cooldown;
                if (_skillWeapon == WeaponKind.Sword) { if (_reservedCounter == 0)
                    {
                        _swordSkillReady = ready;
                    }
                }
                else if (_skillWeapon == WeaponKind.Bow) { _bowSkillReady = ready; }
                else { _bellSkillReady = ready; }
                if (_skillWeapon == WeaponKind.Bell)
                {
                    // 설치 타격은 새 진을 등록하기 전의 공격력으로 한 번만 계산한다
                    ApplySkillDamage(_skillPoint, _skillStats.FieldRadius, 360f,
                        _skillSettings.DamageMultiplier, _skillPower * _skillResonance, true, false, false);
                    ServerPlaceField();
                    RpcAbilityEvent(WeaponAbilityEvent.BellField, _fieldPoint, 0, AbilityTime);
                }
                else if (_skillWeapon != WeaponKind.Bow)
                {
                    bool hit = ApplySkillDamage(transform.position, _skillSettings.Range, _skillSettings.Angle,
                        _skillSettings.DamageMultiplier, _skillPower * _skillResonance, false, false, true);
                    ResolveSwordSkillOutcome(hit);
                    RpcAbilityEvent(WeaponAbilityEvent.SwordSkill,
                        transform.position + Vector3.up * GameDB.Combat.RangedMuzzleHeight, (byte)(hit ? 1 : 0), AbilityTime);
                }
            }
            // Probe the actual travelled segment, never the intended endpoint through a wall
            if (_skillExecution != 0 && _skillCommitted && _skillWeapon == WeaponKind.Bow && !_skillSlideHit && !_skillSlideFinished)
            {
                Vector3 current = transform.position;
                int samples = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(_skillPreviousPosition, current) /
                    (_skillSettings.Range * .5f)), 1, 16);
                bool dealtDamage = false;
                for (int i = 0; i <= samples && !_skillSlideHit; ++i)
                {
                    dealtDamage |= ApplySkillDamage(Vector3.Lerp(_skillPreviousPosition, current, (float)i / samples),
                        _skillSettings.Range, _skillSettings.Angle, _skillSettings.DamageMultiplier,
                        _skillPower * _skillResonance, false, true, true);
                }

                if (dealtDamage)
                {
                    RpcAbilityEvent(WeaponAbilityEvent.BowStab, current + Vector3.up * GameDB.Combat.RangedMuzzleHeight, 1, AbilityTime);
                }

                _skillPreviousPosition = current;
                _skillSlideFinished = AbilityTime >= _skillSlideEnd;
            }
            if (_skillExecution != 0 && AbilityTime >= _skillLockEnd) { _skillExecution = 0; }
            ServerUpdateField();
        }

        /****************************************
        *              공명진
        ****************************************/

        public bool HasActiveField => _fieldSource.IsValid && AbilityTime < _fieldEnd;
        public bool HasGrantedFieldEffects => HasActiveField ||
            (_fieldSource.IsValid && AbilityTime < _fieldLastGrantedExpiry);

        /// <summary> 진을 만든다. 이 순간의 시전자 값으로 반경, 수명, 이탈 유지를 고정한다 </summary>
        [Server]
        private void ServerPlaceField()
        {
            // 시전자당 하나다. 새로 깔면 앞의 진과 그 잔여 버프는 즉시 회수된다
            ServerDiscardField();
            ParticipantId caster = ResolveParticipantId();
            if (!caster.IsValid)
            {
                Debug.LogError("[PlayerAttack] 시전자 신원이 없어 공명진을 등록하지 못했다", this);
                return;
            }

            _fieldSource = new ResonanceFieldSource { Caster = caster, FieldId = TakeNextFieldId() };
            _fieldPoint = _skillPoint;
            _fieldRadius = _skillStats.FieldRadius;
            _fieldMultiplier = _skillStats.FieldDamageMultiplier;
            _fieldTail = _skillStats.FieldTailDuration;
            _fieldEnd = AbilityTime + _skillStats.FieldDuration;
            _fieldNextSample = AbilityTime;
            _fieldEchoExtended = false;
            _fieldAfterglowUsed = false;
            ServerSampleField();
        }

        [Server]
        private void ServerUpdateField()
        {
            if (!_fieldSource.IsValid) { return; }
            if (AbilityTime >= _fieldEnd)
            {
                // 자연 만료는 새 갱신만 멈춘다. 이미 받은 이탈 유지분은 원래 시각까지 남는다
                ServerEndField();
                if (AbilityTime >= _fieldLastGrantedExpiry) { _fieldSource = default; }
                return;
            }
            if (AbilityTime < _fieldNextSample) { return; }
            ServerSampleField();
        }

        /// <summary> 진 안에 서 있는 살아 있는 참가자에게 버프 만료를 미룬다 </summary>
        [Server]
        private void ServerSampleField()
        {
            var config = GameDB.Combat;
            _fieldNextSample = AbilityTime + Mathf.Max(.01f, config.BellFieldSampleInterval);
            double expiry = AbilityTime + _fieldTail;
            _fieldLastGrantedExpiry = expiry;
            var players = PlayerStatus.ServerPlayers;
            for (int i = 0; i < players.Count; ++i)
            {
                PlayerStatus target = players[i];
                if (target == null || target.IsDead || target.IsSwallowed) { continue; }
                if (!ServerIsInsideField(target.transform.position)) { continue; }
                if (target.TryGetComponent(out ISupportBuffTarget buff))
                { buff.ServerApplyResonance(_fieldSource, _fieldMultiplier, expiry); }
            }
        }

        /// <summary> 내 진의 유효 체류 판정. 같은 기준을 치유, 회수 연계 노드도 쓴다 </summary>
        [Server]
        public bool ServerIsInsideField(Vector3 foot)
        {
            if (!HasActiveField || !GameDB.IsInitialized || GameDB.Combat == null) { return false; }
            var config = GameDB.Combat;
            if (!ResonanceFieldRules.IsWithinField(_fieldPoint, foot, _fieldRadius,
                config.BellFieldHeightTolerance)) { return false; }
            ResonanceFieldRules.BuildSightSegment(_fieldPoint, foot, config.BellFieldSightOffset,
                out Vector3 from, out Vector3 to);
            return GameDB.Arrow == null ||
                !Physics.Linecast(from, to, GameDB.Arrow.WorldMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 「잔울림」. 자기 진 안에서 잔향을 실제 회수했을 때 한 진에 한 번만 수명을 늘린다
        /// 원천 ID를 유지하므로 이미 받은 이탈 유지 시간이 즉시 늘어나지는 않는다
        /// </summary>
        [Server]
        public bool ServerTryExtendField(float seconds)
        {
            if (!HasActiveField || _fieldEchoExtended || seconds <= 0f ||
                AbilityTime >= _fieldEnd || !ServerIsInsideField(transform.position)) { return false; }
            _fieldEchoExtended = true;
            _fieldEnd += seconds;
            return true;
        }

        /// <summary> 「따뜻한 여운」의 한 진 1회 사용 권한을 먼저 확정한다 </summary>
        [Server]
        public bool ServerTryConsumeFieldAfterglow()
        {
            if (!HasActiveField || _fieldAfterglowUsed || AbilityTime >= _fieldEnd) { return false; }
            _fieldAfterglowUsed = true;
            return true;
        }

        /// <summary> 진의 수명이 다했다. 이미 나간 버프는 각자의 만료 시각을 그대로 쓴다 </summary>
        [Server]
        private void ServerEndField()
        {
            _fieldEnd = 0; _fieldRadius = 0; _fieldMultiplier = 1f; _fieldTail = 0;
            _fieldNextSample = 0;
        }

        /// <summary> 시전자 쪽 사유로 진을 버린다. 남은 이탈 유지분까지 즉시 회수한다 </summary>
        [Server]
        public void ServerDiscardField()
        {
            if (!_fieldSource.IsValid) { ServerEndField(); return; }
            ResonanceFieldSource source = _fieldSource;
            var players = PlayerStatus.ServerPlayers;
            for (int i = 0; i < players.Count; ++i)
            {
                PlayerStatus target = players[i];
                if (target != null && target.TryGetComponent(out ISupportBuffTarget buff))
                { buff.ServerRemoveResonance(source); }
            }

            ServerEndField();
            _fieldSource = default;
            _fieldLastGrantedExpiry = 0;
        }

        /// <summary>
        /// 그 자리가 같은 세기 이상의 다른 진으로 이미 충분히 덮여 있는가
        /// 봇이 남의 진 위에 겹쳐 까는 것을 막는다
        /// </summary>
        [Server]
        public static bool ServerIsCoveredByField(Vector3 point, float multiplier,
            double minimumRemaining, PlayerAttack exclude)
        {
            for (int i = 0; i < AbilityOwners.Count; ++i)
            {
                PlayerAttack owner = AbilityOwners[i];
                if (owner == null || owner == exclude || !owner.HasActiveField) { continue; }
                if (owner._fieldMultiplier < multiplier) { continue; }
                if (owner._fieldEnd - AbilityTime < minimumRemaining) { continue; }
                if (owner.ServerIsInsideField(point)) { return true; }
            }

            return false;
        }

        /// <summary> 이 행동의 비용이 확정되는 순간의 공명진 계수. 행동당 한 번만 읽는다 </summary>
        [Server]
        private float ResolveResonanceMultiplier()
        {
            if (_buffProvider == null) { return 1f; }
            ParticipantId participant = ResolveParticipantId();
            if (!participant.IsValid ||
                !_buffProvider.TryGetDamageMultiplier(participant, out float multiplier) ||
                !IsFinite(multiplier)) { return 1f; }
            return Mathf.Max(1f, multiplier);
        }

        private static uint TakeNextFieldId()
        {
            uint id = G_NextFieldId;
            G_NextFieldId = (G_NextFieldId == uint.MaxValue) ? 1u : G_NextFieldId + 1u;
            return id;
        }

        // 설치 타격은 대상 수를 제한하고 잔향 부착, 충전을 발생시키지 않는다
        private bool ApplySkillDamage(Vector3 origin, float range, float angle, float multiplier,
            float power, bool limitTargets, bool stab, bool notifiesEcho)
        {
            _skillTargets.Clear(); _skillHitEntities.Clear();
            Vector3 probe = origin + Vector3.up * GameDB.Combat.RangedMuzzleHeight;
            int count = Physics.OverlapSphereNonAlloc(probe, range, _hitBuffer, _targetMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; ++i)
            {
                Collider hit = _hitBuffer[i];
                if (hit == null || hit.transform.root == transform.root || hit.GetComponentInParent<PlayerStatus>() != null ||
                    !DamageTargetResolver.TryResolve(hit, out IDamageable damage)) { continue; }
                AbilityTarget target = MakeAbilityTarget(damage);
                if (target == null) { continue; }
                Vector3 point = hit.ClosestPoint(probe), offset = point - probe;
                Vector3 flat = new Vector3(offset.x, 0, offset.z);
                float degrees = flat.sqrMagnitude <= .0001f ? 0 : Vector3.Angle(_skillForward, flat);
                if (offset.sqrMagnitude > range * range || degrees > angle * .5f ||
                    Physics.Linecast(probe, point, GameDB.Arrow.WorldMask, QueryTriggerInteraction.Ignore)) { continue; }
                _skillTargets.Add(new SkillTarget { Target = target, Point = point, Angle = degrees, Distance = offset.sqrMagnitude });
            }
            _skillTargets.Sort((a, b) =>
            {
                int value = a.Angle.CompareTo(b.Angle); if (value != 0) { return value; }
                value = a.Distance.CompareTo(b.Distance); if (value != 0) { return value; }
                value = a.Target.Reference.EntityNetId.CompareTo(b.Target.Reference.EntityNetId);
                return value != 0 ? value : a.Target.Reference.PartId.CompareTo(b.Target.Reference.PartId);
            });
            int hits = 0;
            bool dealtDamage = false;
            // 피해 결과의 표현 단계용 종류. 받아베기는 소비한 표식 수를 함께 싣는다
            DamageSourceKind source = _skillWeapon == WeaponKind.Bell ? DamageSourceKind.BellField :
                _skillWeapon == WeaponKind.Bow ? DamageSourceKind.BowStab :
                _reservedCounter > 0 ? DamageSourceKind.CounterSlash : DamageSourceKind.SwordSkill;
            byte detail = source == DamageSourceKind.CounterSlash ? _reservedCounter : (byte)0;
            foreach (SkillTarget candidate in _skillTargets)
            {
                var target = candidate.Target;
                if (!_skillHitEntities.Add(target.Reference.EntityNetId)) { continue; }
                if (stab) { _skillSlideHit = true; } // An immune target still consumes this action's one contact
                float before = ((IDamageHealthSource)target.Damage).Health;
                target.Damage.TakeDamage(new DamageInfo(power * multiplier, netId, ResolveParticipantId(), HitStrength.Heavy)
                    .WithSource(source, _skillExecution, detail).WithHitPoint(candidate.Point));
                if (((IDamageHealthSource)target.Damage).Health < before)
                {
                    dealtDamage = true;
                    if (_skillWeapon == WeaponKind.Sword)
                    {
                        var stagger = target.Component.GetComponentInParent<IWeaponSkillStaggerTarget>();
                        stagger?.TryApplyWeaponSkillStagger(GameDB.Combat.SwordSkillStagger, GameDB.Combat.SwordSkillStaggerResistance);
                    }
                    if (stab) { InstallMeleeWedge(target.Damage, candidate.Point); }
                    if (notifiesEcho)
                    {
                        // A stab is not an arrow; only notify bell owners, never progress its own newly placed wedge
                        foreach (PlayerAttack owner in AbilityOwners)
                        {
                            if (owner != null && owner._weapon != null && owner._weapon.HasWeapon && owner._weapon.Weapon == WeaponKind.Bell &&
                                owner._status != null && !owner._status.IsDead && !owner._status.IsSwallowed)
                            {
                                owner.ChargeEcho(target, new DirectAttackKey(netId, _skillExecution, false), false, candidate.Point);
                            }
                        }
                    }
                }
                ++hits;
                if (stab || (limitTargets && hits >= GameDB.Combat.BellFieldMaxTargets)) { break; }
            }
            return dealtDamage;
        }

        public bool CanRollCancelWeaponSkill => _skillExecution != 0 && IsWeaponSkillActive &&
            (!_skillCommitted || _skillWeapon == WeaponKind.Bow && AbilityTime >= _skillSlideEnd + GameDB.Combat.BowSkillRollDelay);

        /// <summary>Integrate a decelerating slide over this physics tick; animation never moves the network root</summary>
        public bool TryGetWeaponSkillMotion(float deltaTime, out Vector2 direction, out float speed)
        {
            direction = Vector2.zero; speed = 0;
            if (isServer && _skillExecution != 0 && _skillWeapon == WeaponKind.Sword && !_skillSlideBlocked &&
                _skillStepRemaining > 0 && deltaTime > 0 && !RunTimeLocator.Current.IsPaused &&
                _status != null && !_status.IsDead && !_status.IsHitReacting && !_status.IsSwallowed)
            {
                float distance = Mathf.Min(_skillStepRemaining, GameDB.Combat.SwordStepDistance * deltaTime / GameDB.Combat.CounterStartup);
                _skillStepRemaining -= distance; direction = _skillStepDirection; speed = distance / deltaTime;
                return true;
            }
            if (!isServer || _skillExecution == 0 || !_skillCommitted || _skillWeapon != WeaponKind.Bow ||
                _skillSlideBlocked || deltaTime <= 0 || AbilityTime < _skillImpact || AbilityTime >= _skillSlideEnd ||
                RunTimeLocator.Current.IsPaused || _status == null || _status.IsDead || _status.IsSwallowed || _status.IsHitReacting)
            {
                return false;
            }

            float duration = (float)(_skillSlideEnd - _skillImpact);
            float a = _skillSlideProgress;
            float b = Mathf.Clamp01((float)(AbilityTime + deltaTime - _skillImpact) / duration);
            // Integral of 2(1-t): strong push-off, then friction brings the feet to rest
            speed = GameDB.Combat.BowSkillSlideDistance * ((2 * b - b * b) - (2 * a - a * a)) / deltaTime;
            _skillSlideProgress = b;
            direction = new Vector2(_skillForward.x, _skillForward.z);
            return true;
        }

        [Server]
        public void ServerBlockWeaponSkillSlide() { _skillSlideBlocked = true; }

        [Server]
        public void ServerCancelWeaponSkillForRoll() { if (CanRollCancelWeaponSkill) { CancelWeaponSkill(false); } }

        [Server]
        private void CancelWeaponSkill(bool clearField)
        {
            if (_skillExecution != 0) { RpcSkillCancelled(_skillExecution); }
            _reservedCounter = 0; _skillStepRemaining = 0;
            _skillExecution = 0; _skillLockEnd = 0; _skillCost = 0; _skillResonance = 1f;
            if (clearField) { ServerDiscardField(); }
        }

        [Server]
        private void ResetWeaponSkillCooldowns() { _swordSkillReady = _bowSkillReady = _bellSkillReady = 0; }

        [ClientRpc]
        private void RpcSkillStarted(PlayerActionCue cue) { _animation?.PlayActionCue(in cue); }
        [ClientRpc]
        private void RpcSkillRecovery(uint execution, double end) { _animation?.UpdateSkillRecovery(execution, end); }
        [ClientRpc]
        private void RpcSkillCancelled(uint execution)
        { _clientMoveLockUntilTime = 0; _animation?.CancelActionCue(PlayerActionCueKind.WeaponSkill, execution); }
    }
}
