using System;
using Geumeum.AI;
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;
using Geumeum.Systems;
using Mirror;
using UnityEngine;
using Unity.Profiling;
using Unity.Behavior;
using BehaviourState = Unity.Behavior.Node.Status;

namespace Geumeum.Combat
{
    /// <summary> 공통 서버 BT와 기존 플레이어 실행기를 연결한다. 클라이언트 판단은 없다 </summary>
    public sealed class PlayerBrain : IDisposable
    {
        private static readonly ProfilerMarker Marker = new ProfilerMarker("Geumeum.PlayerBrain");
        private readonly PlayerController _controller;
        private readonly PlayerStatus _status;
        private readonly PlayerAttack _attack;
        private readonly PlayerWeapon _weapon;
        private readonly HealController _heal;
        private readonly ReviveController _revive;
        private readonly PlayerRollController _roll;
        private readonly ParryController _parry;
        private readonly PrecisionShotController _precision;
        private readonly LockOnController _lockOn;
        private readonly InventoryController _inventory;
        private readonly TalentController _talents;
        private readonly InteractionController _interaction;
        private ShopTransactionService _shop;
        private readonly InventorySlotSnapshot[] _slots = new InventorySlotSnapshot[InventorySnapshot.SLOT_COUNT];
        private double _nextInventory;
        private uint _precisionHold;
        private double _precisionStarted;
        private BotThreatTracker _threats;
        private readonly System.Collections.Generic.Dictionary<uint, ReviveController> _rescuers =
            new System.Collections.Generic.Dictionary<uint, ReviveController>();
        private double _nextSupport;
        private uint _rescueHold;
        private readonly ICombatLoadoutProvider _loadout;
        private readonly BehaviorGraphAgent _graphAgent;
        private PlayerBehaviorStep? _activeBranch;
        private bool _graphFaulted;
        public BehaviorGraph BehaviorGraph => _graphAgent != null ? _graphAgent.Graph : null;
        private readonly GameEventBus _events;
        private BotConfigSO _config;
        private BotSkillProfileSO _skill;
        private BotPerception _sensor;
        private BotNavigation _navigation;
        private BotObservation _observation;
        private System.Random _random;
        private BotPerception.Target _target;
        private BotScheduledAction _scheduled;
        private Vector3 _observedPoint;
        private double _lookUntil;
        private Vector2 _move;
        private PlayerStatus _leader;
        private PlayerController _leaderController;
        private BossEntranceTrigger _entrance;
        private bool _following;
        private bool _regrouping;
        private bool _catchingUp;
        public bool IsCatchUpSprinting => _catchingUp && (ActiveNode == "Follow" || ActiveNode == "Regroup");
        private bool _configured;
        private double _nextDecision;
        private double _nextAttack;
        private uint _run;
        private uint _scene;
        private uint _request;
        private uint _moveSequence;
        private double _now;

        public PlayerControlMode Mode { get; private set; }
        public uint Generation { get; private set; } = 1u;
        public string ActiveNode { get; private set; } = "Human";
        public uint TargetNetId => (_target != null && _target.Identity != null) ? _target.Identity.netId : 0u;
        public int AcceptedAttacks { get; private set; }
        public int AcceptedRolls { get; private set; }
        public int ParryAttempts { get; private set; }
        public int AcceptedHeals { get; private set; }
        public int StartedRescues { get; private set; }
        public int AcceptedPrecision { get; private set; }
        public int PathFailures => (_navigation == null) ? 0 : _navigation.Failures;
        public ActionRejection LastRejection { get; private set; }

        public PlayerBrain(PlayerController controller)
        {
            _controller = controller;
            _status = controller.GetComponent<PlayerStatus>();
            if (_status != null) { _status.ServerDamagedBy += OnDamaged; }
            _attack = controller.GetComponent<PlayerAttack>();
            _weapon = controller.GetComponent<PlayerWeapon>();
            _heal = controller.GetComponent<HealController>();
            _revive = controller.GetComponent<ReviveController>();
            _roll = controller.GetComponent<PlayerRollController>();
            _parry = controller.GetComponent<ParryController>();
            _precision = controller.GetComponent<PrecisionShotController>();
            _lockOn = controller.GetComponent<LockOnController>();
            _inventory = controller.GetComponent<InventoryController>();
            _talents = controller.GetComponent<TalentController>();
            _interaction = controller.GetComponent<InteractionController>();
            _loadout = controller.GetComponent<ICombatLoadoutProvider>();
            _graphAgent = controller.gameObject.AddComponent<BehaviorGraphAgent>();
            // The server authority scheduler owns evaluation; MonoBehaviour.Update must never double tick it
            _graphAgent.enabled = false;
            _graphAgent.Graph = Resources.Load<BehaviorGraph>("BT/PlayerBehavior");
            if (_graphAgent.Graph != null) { _graphAgent.Init(); }
            else { Debug.LogError("[PlayerBrain] BT/PlayerBehavior Unity Behavior graph is missing"); _graphFaulted = true; }
            _events = GameEventBus.HasInstance ? GameEventBus.Instance : null;
            if (_events != null)
            {
                _events.Subscribe<RunPausedEvent>(OnPaused);
                _events.Subscribe<RunResumedEvent>(OnResumed);
                _events.Subscribe<RunPhaseChangedEvent>(OnPhaseChanged);
            }
        }

        public void Tick()
        {
            if (!NetworkServer.active) { return; }
            using (Marker.Auto())
            {
                _now = NetworkTime.time;
                RunManager run = RunManager.ServerInstance;
                uint nextRun = (run == null) ? 0u : run.RunId.Value;
                uint nextScene = (RunSceneDirector.ServerInstance == null) ? 0u : RunSceneDirector.ServerInstance.Revision.Value;
                PlayerControlMode mode = ((_status != null) && (_status.Participant.Kind == ParticipantKind.Bot))
                    ? PlayerControlMode.Bot : PlayerControlMode.Human;
                if ((mode != Mode) || (_run != nextRun) || (_scene != nextScene))
                {
                    Reset(); Mode = mode; _run = nextRun; _scene = nextScene;
                }
                if (!_graphFaulted && BehaviorGraph != null)
                {
                    // This asset is a synchronous decision tree (no Wait/Repeat nodes)
                    // Pump Unity's own scheduler to completion in the same authority tick
                    BehaviorGraph.Restart();
                    for (int step = 0; step < 128 && BehaviorGraph.IsRunning; ++step) { BehaviorGraph.Tick(); }
                    if (BehaviorGraph.IsRunning)
                    {
                        BehaviorGraph.End(); _graphFaulted = true; Reset();
                        Debug.LogError("[PlayerBrain] Decision graph did not finish; check for Wait/Repeat/cycles");
                    }
                }
                if (_graphFaulted) { _move = Vector2.zero; ActiveNode = "Unavailable"; _controller.ServerClearMoveInput(); }
                if (Mode == PlayerControlMode.Bot)
                {
                    if (_moveSequence == uint.MaxValue) { _move = Vector2.zero; _controller.ServerClearMoveInput(); return; }
                    _controller.ServerReceiveMoveInput(new PlayerMoveInput
                    {
                        Sequence = ++_moveSequence, MoveDirection = _move,
                        SprintHeld = ActiveNode == "Barrier" || ActiveNode == "Evade" ||
                            ((ActiveNode == "Follow" || ActiveNode == "Regroup") && _catchingUp),
                        ClientEstimatedServerTime = _now,
                    });
                }
            }
        }

        // Leaf dispatch only: ordering and fallback live in the editable Unity Behavior asset
        public BehaviourState EvaluateBehaviorStep(PlayerBehaviorStep step)
        {
            if (!NetworkServer.active) { return BehaviourState.Failure; }
            if (step == PlayerBehaviorStep.Blocked)
            { return Blocked() == BehaviourState.Failure ? BehaviourState.Failure : BehaviourState.Success; }
            if (step == PlayerBehaviorStep.Human)
            {
                if (Mode != PlayerControlMode.Human) { return BehaviourState.Failure; }
                ActiveNode = "Human"; return BehaviourState.Success;
            }
            if (Mode != PlayerControlMode.Bot) { return BehaviourState.Failure; }
            if (step == PlayerBehaviorStep.PrepareBot) { return PrepareBot(); }
            BehaviourState result;
            switch (step)
            {
                case PlayerBehaviorStep.Safety: result = Safety(); break;
                case PlayerBehaviorStep.Defend: result = Defend(); break;
                case PlayerBehaviorStep.Regroup: result = Regroup(); break;
                case PlayerBehaviorStep.Support: result = Support(); break;
                case PlayerBehaviorStep.Combat: result = Combat(); break;
                case PlayerBehaviorStep.Follow: result = Follow(); break;
                case PlayerBehaviorStep.Idle:
                    ActiveNode = "Idle"; _move = Vector2.zero; result = BehaviourState.Running; break;
                default: return BehaviourState.Failure;
            }
            if (result == BehaviourState.Failure)
            {
                if (_activeBranch == step) { CancelBranch(); }
                return BehaviourState.Failure;
            }
            if (_activeBranch != step) { CancelBranch(); _activeBranch = step; }
            // A selected intent completes this decision; long-lived server actions retain their own state
            return BehaviourState.Success;
        }

        private void CancelBranch()
        {
            switch (_activeBranch)
            {
                case PlayerBehaviorStep.Support: CancelSupport(); break;
                case PlayerBehaviorStep.Combat: CancelCombat(); break;
                case PlayerBehaviorStep.Regroup:
                case PlayerBehaviorStep.Follow: _navigation?.Clear(); break;
            }
            _activeBranch = null;
        }

        private BehaviourState Blocked()
        {
            RunManager run = RunManager.ServerInstance;
            if ((_status != null) && (run != null) &&
                (!RunSceneDirector.IsServerTransitioning) &&
                PlayerActionGate.CanAct(run.Phase, _status.LifeState, run.IsPaused))
            { return BehaviourState.Failure; }
            if (ActiveNode != "Blocked") { Reset(); }
            ActiveNode = "Blocked";
            _controller.ServerClearMoveInput();
            return BehaviourState.Running;
        }

        private bool Configure()
        {
            if (_configured) { return _sensor != null; }
            _configured = true;
            _config = Resources.Load<BotConfigSO>("BotConfig");
            _skill = Resources.Load<BotSkillProfileSO>("BotSkillProfile");
            if ((_config == null) || (_skill == null) || (_events == null) ||
                (_attack == null) || (_weapon == null) || (_loadout == null))
            {
                Debug.LogError("[PlayerBrain] 봇 설정 또는 실행기/이벤트 버스가 없어 판단을 중지한다");
                return false;
            }
            _random = new System.Random(unchecked((int)_status.Participant.Value * 397 ^ (int)_run));
            _observation = new BotObservation(_skill, _random);
            _sensor = new BotPerception(_controller.transform, _config, _skill);
            _navigation = new BotNavigation(_config);
            _threats = new BotThreatTracker(_skill, _random);
            _shop = UnityEngine.Object.FindAnyObjectByType<ShopTransactionService>();
            _entrance = UnityEngine.Object.FindAnyObjectByType<BossEntranceTrigger>();
            return true;
        }

        private Vector3 Forward
        {
            get
            {
                // 이동 방향과 관측 시선은 분리한다. 후퇴한다고 방금 본 적을 즉시 등 뒤로 잊지 않는다
                if (_target != null && _now < _lookUntil)
                {
                    Vector3 look = _observedPoint - _controller.transform.position;
                    look.y = 0f;
                    if (look.sqrMagnitude > 0.001f) { return look.normalized; }
                }
                Vector2 direction = _controller.LastFacingDirection;
                return new Vector3(direction.x, 0f, direction.y);
            }
        }

        private BehaviourState PrepareBot()
        {
            if (!Configure()) { _move = Vector2.zero; ActiveNode = "Unavailable"; return BehaviourState.Failure; }
            bool visible = _sensor.Visible(_target, Forward);
            ExecuteDefense();
            if (_scheduled.TryConsume(_now, _run, _scene, Generation, TargetNetId, visible))
            { ExecuteAttack(); }
            if (_now < _nextDecision) { return BehaviourState.Failure; }
            _nextDecision = _now + Math.Max(0.01f, _config.DecisionInterval);
            UpdateInventory();
            BotPerception.Target nearest = _sensor.Observe(Forward);
            BotPerception.Target observed = visible ? _target : nearest;
            if (BossPhaseSourceLocator.Current != null &&
                BossPhaseSourceLocator.Current.PhaseSnapshot.Stage == BossEncounterStage.FinalSoulWindow)
            { observed = _sensor.ObserveSoul(Forward, _weapon.Weapon); }
            else if (_weapon.Weapon == WeaponKind.Bow)
            { observed = _sensor.ObserveActivePart(Forward) ?? observed; }
            if (!ReferenceEquals(observed, _target))
            {
                CancelCombat(); _observation.Clear(); _target = observed;
            }
            _observation.Observe(TargetNetId, _now);
            if (_target != null) { _observedPoint = _target.Point; _lookUntil = _now + _skill.HitMemorySeconds; }
            _threats.Observe(_sensor, Forward, _controller.transform.position, _status.netId, _now);
            return BehaviourState.Success;
        }

        private BehaviourState Combat()
        {
            if ((_target == null) || _observation.Missed) { return BehaviourState.Failure; }
            ResolveLeader();
            if (_leader != null && PlanarDistance(_leader.transform.position, _controller.transform.position) > _config.RegroupDistance)
            { return BehaviourState.Failure; }
            _move = Vector2.zero;
            if (_now < _observation.ReadyAt) { ActiveNode = "Observe"; return BehaviourState.Running; }
            float distance = PlanarDistance(_observedPoint, _controller.transform.position);
            float reach = AttackDistance();
            if (_weapon.Weapon == WeaponKind.Sword && _target.Health is EnemyStatus ranged &&
                ranged.Kind == EnemyKind.Aekgwi && distance > _config.SwordRangedChaseLimit)
            { return BehaviourState.Failure; }
            if (_target.Enemy != null && _leader != null &&
                PlanarDistance(_target.Point, _leader.transform.position) > _config.AssistDistance &&
                !CombatClock.IsInCombat(_leader.netId, RunTimeLocator.Current.GameplayElapsedSeconds, _config.OutOfCombatSeconds) &&
                !CombatClock.IsInCombat(_status.netId, RunTimeLocator.Current.GameplayElapsedSeconds, _config.OutOfCombatSeconds) &&
                !_sensor.HasVisibleThreat(Forward, _config.RescueSafetyRadius))
            { return BehaviourState.Failure; }
            if (_target.Health is BossStatus && BossPhaseSourceLocator.Current != null &&
                BossPhaseSourceLocator.Current.PhaseSnapshot.Stage == BossEncounterStage.WakeWarning &&
                distance < _config.BossWakeDistance)
            {
                ActiveNode = "BossWakeRetreat"; CancelPrecision();
                Vector3 away = (_controller.transform.position - _observedPoint).normalized;
                _move = _navigation.Direction(_controller.transform.position, _observedPoint + away * _config.BossWakeDistance, _now);
                return BehaviourState.Running;
            }
            if (TryWeaponSkill(distance)) { return BehaviourState.Running; }
            if ((!HasReserve()) || ((_weapon.Weapon == WeaponKind.Bow) && (distance < _config.BowMinimumDistance)))
            {
                ActiveNode = "Retreat";
                _scheduled.Pending = false;
                Vector3 away = _controller.transform.position - _observedPoint;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) { away = -Forward; }
                _move = _navigation.Direction(_controller.transform.position,
                    _controller.transform.position + away.normalized * _config.RetreatDistance, _now);
                return BehaviourState.Running;
            }
            if (distance > reach)
            {
                ActiveNode = "Approach";
                _move = _navigation.Direction(_controller.transform.position, _observedPoint, _now);
                return BehaviourState.Running;
            }
            ActiveNode = "Attack";
            if (TryEchoRecall()) { return BehaviourState.Running; }
            if (TryPrecision()) { return BehaviourState.Running; }
            if ((!_scheduled.Pending) && (_now >= _nextAttack) && HasReserve() &&
                (!_attack.IsBaseActionInProgress(_now)) && (_request != uint.MaxValue) &&
                (_threats.EarliestImpact(_now) - _now >= _skill.AttackCommitSeconds))
            {
                double execute = Math.Max(_observation.ReadyAt, _now);
                _scheduled = new BotScheduledAction
                {
                    Pending = true, Run = _run, Scene = _scene, Generation = Generation,
                    Target = TargetNetId, ExecuteAt = execute,
                    ExpiresAt = execute + Math.Max(0.01f, _config.ScheduledLifetime),
                };
            }
            return BehaviourState.Running;
        }

        private void ExecuteAttack()
        {
            _nextAttack = _now + Math.Max(0.01f, _config.AttackRetryInterval);
            if ((!HasReserve()) || (_request == uint.MaxValue) ||
                (PlanarDistance(_target.Point, _controller.transform.position) > AttackDistance())) { return; }
            Vector3 origin = _controller.transform.position + Vector3.up * _config.EyeHeight;
            Vector3 aimPoint = _attack.ServerTryGetWedgePoint(TargetNetId, out Vector3 wedgePoint) ? wedgePoint : _target.Point;
            Vector3 direction = (aimPoint - origin).normalized;
            direction = Quaternion.AngleAxis((float)BotSkillProfileSO.Gaussian(_random) * _skill.AimSigmaDegrees,
                Vector3.up) * direction;
            Vector2 aim = new Vector2(direction.x, direction.z).normalized;
            byte part = _target.Soul != null ? _target.Soul.TargetPartId : _target.Part != null ? _target.Part.TargetPartId : (byte)0;
            _lockOn?.ServerSetLockOn(TargetNetId, part);
            LastRejection = _attack.ServerRequestAttack(++_request, aim,
                new PrecisionAimRay { Origin = origin, Direction = direction });
            if (LastRejection == ActionRejection.None) { ++AcceptedAttacks; }
        }

        private bool TryWeaponSkill(float distance)
        {
            if (_attack.IsActionInProgress(_now) || _attack.WeaponSkillCooldownRemaining > 0 ||
                _target.Soul != null || !HasReserve() || !_loadout.TryBuildLoadout(_status.Participant, out CombatStatsSnapshot stats)) { return false; }
            var settings = GameDB.Combat.Skill(_weapon.Weapon);
            if (_weapon.Weapon == WeaponKind.Sword && _attack.CounterMarks > 0)
            {
                settings.Startup = GameDB.Combat.CounterStartup;
                settings.Range = GameDB.Combat.CounterRange;
            }
            if (_weapon.Weapon == WeaponKind.Bow)
            {
                settings.Cost = Mathf.Max(0f, settings.Cost - stats.BowSkillStaminaReduction);
            }

            float cost = _weapon.Weapon == WeaponKind.Bell
                ? stats.ResolveSpiritCost(settings.Cost, stats.SkillSpiritCostReduction)
                : stats.ResolveStaminaCost(settings.Cost);
            if (_weapon.Weapon == WeaponKind.Bell ? _status.Spirit < cost + stats.ResolveSpiritCost(GameDB.Combat.HealSpiritCost, stats.HealSpiritCostReduction) :
                _status.Stamina < cost + GameDB.Combat.RollStaminaCost) { return false; }
            if (_threats.EarliestImpact(_now) - _now < settings.Startup + settings.Recovery) { return false; }
            if (_weapon.Weapon == WeaponKind.Bow)
            {
                if (_attack.ServerTryGetWedgePoint(TargetNetId, out _)) { return false; }
                // Only approach a nearby observed opening; never chase across the arena for a mark
                bool opening = _target.Enemy != null && (_target.Enemy.VisualState == EnemyVisualState.Recovery ||
                    _target.Enemy.VisualState == EnemyVisualState.Stagger || _target.Enemy.VisualState == EnemyVisualState.Knockback);
                if (distance > settings.Range)
                {
                    if (!opening || distance > _config.BowMinimumDistance) { return false; }
                    ActiveNode = "ApproachMark"; _scheduled.Pending = false;
                    _move = _navigation.Direction(_controller.transform.position, _observedPoint, _now);
                    return true;
                }
            }
            else if (_weapon.Weapon != WeaponKind.Bell && distance > settings.Range) { return false; }
            if (_weapon.Weapon == WeaponKind.Bell)
            {
                // 봇은 자유 커서가 없지만 우회 판정도 없다. 동료 위치로 후보를 만들어
                // 사람과 같은 서버 설치 검증에 보낸다
                if (!TryPickFieldPoint(stats.FieldRadius, out Vector3 fieldPoint)) { return false; }
                Vector3 toField = fieldPoint - _controller.transform.position;
                LastRejection = _attack.ServerRequestWeaponSkill(++_request, _run, _scene,
                    toField.sqrMagnitude < .001f ? Vector2.up : new Vector2(toField.x, toField.z).normalized, fieldPoint);
            }
            else
            {
                Vector3 direction = _target.Point - _controller.transform.position;
                LastRejection = _attack.ServerRequestWeaponSkill(++_request, _run, _scene, new Vector2(direction.x, direction.z).normalized);
            }
            if (LastRejection != ActionRejection.None) { return false; }
            ActiveNode = "WeaponSkill"; _scheduled.Pending = false; _move = Vector2.zero;
            return true;
        }

        /// <summary>
        /// 많은 동료가 밟을 수 있는 유효 지면을 고른다
        /// 적이 선 자리만 보고 깔면 아무도 안 밟는 진이 된다
        /// </summary>
        private bool TryPickFieldPoint(float radius, out Vector3 best)
        {
            best = default;
            Vector3 self = _controller.transform.position;
            int bestCovered = 0;
            float bestDistance = float.MaxValue;

            foreach (PlayerStatus candidate in PlayerStatus.ServerPlayers)
            {
                if (candidate == null || candidate.IsDead || candidate.IsSwallowed) { continue; }
                if (!_attack.TryResolveFieldPlacement(candidate.transform.position, out Vector3 point)) { continue; }
                if (IsFieldHazard(point)) { continue; }
                // 같은 세기의 다른 진이 아직 충분히 남은 자리는 비워 둔다
                if (PlayerAttack.ServerIsCoveredByField(point, GameDB.Combat.BellFieldDamageMultiplier,
                    GameDB.Combat.BellFieldTailDuration, _attack)) { continue; }

                int covered = 0;
                foreach (PlayerStatus ally in PlayerStatus.ServerPlayers)
                {
                    if (ally == null || ally.IsDead || ally.IsSwallowed) { continue; }
                    var weapon = ally.GetComponent<PlayerWeapon>();
                    if (weapon == null || !weapon.HasWeapon) { continue; }
                    float attackRange = weapon.Weapon == WeaponKind.Bow ? _config.BowDistance :
                        weapon.Weapon == WeaponKind.Bell ? GameDB.Combat.BellRange : GameDB.Combat.MeleeRange;
                    if (PlanarDistance(ally.transform.position, _target.Point) > attackRange + 1f) { continue; }
                    if (ResonanceFieldRules.IsWithinField(point, ally.transform.position, radius,
                        GameDB.Combat.BellFieldHeightTolerance) &&
                        !Physics.Linecast(point + Vector3.up * GameDB.Combat.BellFieldSightOffset,
                            ally.transform.position + Vector3.up * GameDB.Combat.BellFieldSightOffset,
                            GameDB.Arrow.WorldMask, QueryTriggerInteraction.Ignore)) { ++covered; }
                }

                float distance = PlanarDistance(point, self);
                if (covered > bestCovered || (covered == bestCovered && distance < bestDistance))
                { best = point; bestCovered = covered; bestDistance = distance; }
            }

            return bestCovered > 0;
        }

        /// <summary> 예고된 보스 장판 위로 동료를 불러들이지 않는다 </summary>
        private bool IsFieldHazard(Vector3 point)
        {
            if (_target.Telegraph == null) { return false; }
            float radius = _config.BellFieldHazardRadius;
            if (radius <= 0f) { return false; }
            double now = Mirror.NetworkTime.time;
            foreach (var cue in _target.Telegraph.TelegraphCues)
            {
                if (!cue.IsPendingAt(now)) { continue; }
                if (PlanarDistance(cue.Origin, point) <= radius) { return true; }
            }

            return false;
        }

        private bool TryEchoRecall()
        {
            if (_weapon.Weapon != WeaponKind.Bell || _attack.IsActionInProgress(_now) || _attack.EchoCooldownRemaining > 0f) { return false; }
            double now = RunTimeLocator.Current.GameplayElapsedSeconds;
            WeaponAbilityMark a = _attack.EchoFirst, b = _attack.EchoSecond;
            bool ready = a.IsActive(now) && (a.Count >= 3 || a.Count >= 2 && a.ExpiresAt - now < GameDB.Combat.EchoCooldown) ||
                b.IsActive(now) && (b.Count >= 3 || b.Count >= 2 && b.ExpiresAt - now < GameDB.Combat.EchoCooldown);
            if (!ready || !_loadout.TryBuildLoadout(_status.Participant, out CombatStatsSnapshot stats)) { return false; }
            // Support branch runs first; retain enough spirit for the next heal regardless of own HP
            float reserve = stats.ResolveSpiritCost(GameDB.Combat.HealSpiritCost, stats.HealSpiritCostReduction) +
                stats.ResolveSpiritCost(GameDB.Combat.EchoSpiritCost, stats.EchoSpiritCostReduction);
            if (_status.Spirit < reserve || _threats.EarliestImpact(_now) - _now < GameDB.Combat.EchoStartupDuration) { return false; }
            LastRejection = _attack.ServerRequestEchoRecall(++_request, _run, _scene);
            if (LastRejection != ActionRejection.None) { return false; }
            ActiveNode = "EchoRecall";
            _scheduled.Pending = false;
            return true;
        }

        private bool TryPrecision()
        {
            if (_weapon.Weapon != WeaponKind.Bow || _precision == null) { return false; }
            bool opportunity = _attack.ServerTryGetWedgePoint(TargetNetId, out _) || _target.Soul != null || (_target.Enemy != null &&
                _target.Enemy.VisualState == EnemyVisualState.Charge);
            if (_target.Telegraph != null)
            { foreach (var cue in _target.Telegraph.TelegraphCues) { opportunity |= cue.IsActive && (cue.PatternId == 5 || cue.PatternId == 9); } }
            bool holding = _precision.State == PrecisionShotState.Holding || _precision.State == PrecisionShotState.Ready;
            if (!holding)
            {
                if (!opportunity || !HasReserve() || _precisionHold == uint.MaxValue ||
                    _threats.EarliestImpact(_now) - _now < GameDB.Combat.PrecisionShotHoldDuration) { return false; }
                if (_precision.ServerBeginPrecisionShot(_run, _scene, ++_request, ++_precisionHold) != ActionRejection.None) { return false; }
                _precisionStarted = _now;
            }
            else if (!opportunity || _now - _precisionStarted > _config.PrecisionTimeout)
            { CancelPrecision(); return false; }
            ActiveNode = "Precision";
            _scheduled.Pending = false;
            if (_precision.State == PrecisionShotState.Ready)
            {
                byte part = _target.Soul == null ? (byte)0 : _target.Soul.TargetPartId;
                _lockOn?.ServerSetLockOn(TargetNetId, part);
                Vector3 origin = _controller.transform.position + Vector3.up * _config.EyeHeight;
                Vector3 precisionPoint = _attack.ServerTryGetWedgePoint(TargetNetId, out Vector3 weakPoint) ? weakPoint : _target.Point;
                Vector3 direction = (precisionPoint - origin).normalized;
                direction = Quaternion.AngleAxis((float)BotSkillProfileSO.Gaussian(_random) * _skill.AimSigmaDegrees, Vector3.up) * direction;
                _precision.ServerReleasePrecisionShot(_run, _scene, ++_request, _precisionHold,
                    new PrecisionAimRay { Origin = origin, Direction = direction }, out ActionRejection rejection);
                LastRejection = rejection;
                if (rejection == ActionRejection.None) { ++AcceptedAttacks; ++AcceptedPrecision; }
            }
            return true;
        }

        private void CancelPrecision()
        {
            if (_precision != null && (_precision.State == PrecisionShotState.Holding || _precision.State == PrecisionShotState.Ready))
            { _precision.ServerCancelPrecisionShot(_run, _scene, ++_request, _precisionHold); }
        }

        private void UpdateInventory()
        {
            if (_now < _nextInventory) { return; }
            _nextInventory = _now + _config.SupportRetrySeconds;
            if (_talents != null && _talents.AvailablePoints > 0)
            {
                int[] order = _weapon.Weapon == WeaponKind.Sword ? _config.SwordTalentOrder :
                    _weapon.Weapon == WeaponKind.Bow ? _config.BowTalentOrder : _config.BellTalentOrder;
                if (order != null) { foreach (int node in order) { _talents.ServerTryAllocate(node, _talents.Revision); } }
            }
            if (_inventory == null || !_inventory.ServerCopySlots(_slots)) { return; }
            bool inCombat = CombatClock.IsInCombat(_status.netId, RunTimeLocator.Current.GameplayElapsedSeconds, _config.OutOfCombatSeconds);
            if (!inCombat && _interaction != null)
            {
                uint drop = _sensor.ObserveUpgrade(Forward, _weapon.Weapon, _inventory.EquippedWeapon.AttackPower, _status.netId);
                if (drop != 0u) { _interaction.ServerRequestInteract(drop); }
            }
            for (int i = 0; i < _slots.Length; ++i)
            {
                InventorySlotSnapshot slot = _slots[i];
                if (!inCombat && slot.Kind == InventoryItemKind.Equipment && slot.Equipment.WeaponKind == _weapon.Weapon &&
                    slot.Equipment.AttackPower > _inventory.EquippedWeapon.AttackPower)
                { _inventory.ServerTryEquip(i, slot.Equipment.InstanceId, _inventory.Revision); }
                bool use = slot.Kind == InventoryItemKind.Consumable &&
                    ((slot.ItemId == ShopItemKind.HealthPotion && _status.Health < _status.MaxHealth * _config.PotionHealthRatio) ||
                     (slot.ItemId == ShopItemKind.SpiritPotion && _weapon.Weapon == WeaponKind.Bell && _status.Spirit < _config.PotionSpiritThreshold));
                if (!use || _shop == null) { continue; }
                var header = new TransactionRequestHeader
                { RunIdValue = _run, SceneRevisionValue = _scene, NetworkObjectGenerationValue = _status.netId,
                    RequestId = ++_request, Operation = TransactionOperation.UsePotion };
                _shop.ServerRequestPotion(_status.netId, header, i, slot.ItemId, _inventory.Revision);
                break;
            }
        }

        private float AttackDistance()
        {
            if (!GameDB.IsInitialized) { return 0f; }
            if (_weapon.Weapon == WeaponKind.Bow) { return _config.BowDistance; }
            return ((_weapon.Weapon == WeaponKind.Bell) ? GameDB.Combat.BellRange : GameDB.Combat.MeleeRange)
                * _config.MeleeRangeRatio;
        }

        private bool HasReserve()
        {
            if ((!GameDB.IsInitialized) || (!_weapon.HasWeapon) ||
                (!_loadout.TryBuildLoadout(_status.Participant, out CombatStatsSnapshot stats))) { return false; }
            float cost = (_weapon.Weapon == WeaponKind.Bow) ? GameDB.Combat.RangedStaminaCost :
                (_weapon.Weapon == WeaponKind.Bell) ? GameDB.Combat.BellStaminaCost : GameDB.Combat.MeleeStaminaCost;
            return _status.Stamina >= _skill.StaminaReserve + stats.ResolveStaminaCost(cost);
        }

        private BehaviourState Regroup()
        {
            ResolveLeader();
            if (_leader == null) { _regrouping = false; return BehaviourState.Failure; }
            float distance = PlanarDistance(_leader.transform.position, _controller.transform.position);
            if (distance > _config.RegroupDistance) { _regrouping = true; }
            if (distance <= _config.RegroupStopDistance) { _regrouping = false; }
            if (!_regrouping) { return BehaviourState.Failure; }
            _scheduled.Pending = false;
            BehaviourState result = Follow();
            if (result != BehaviourState.Failure) { ActiveNode = "Regroup"; }
            return result;
        }

        private BehaviourState Follow()
        {
            ResolveLeader();
            if (_leader == null) { _catchingUp = false; return BehaviourState.Failure; }
            float leaderDistance = PlanarDistance(_leader.transform.position, _controller.transform.position);
            if (leaderDistance >= _config.FollowSprintDistance) { _catchingUp = true; }
            if (leaderDistance <= _config.FollowSprintStopDistance) { _catchingUp = false; }
            Vector2 facing = _leaderController == null ? Vector2.up : _leaderController.LastFacingDirection;
            Vector3 forward = new Vector3(facing.x, 0f, facing.y);
            float formation = _weapon.Weapon == WeaponKind.Sword ? _config.SwordFormationDistance :
                _weapon.Weapon == WeaponKind.Bow ? -_config.BowFormationDistance : -_config.BellFormationDistance;
            Vector3 goal = _leader.transform.position + forward * formation;
            IBarrierState barrier = BarrierStateLocator.Current;
            if (barrier != null && barrier.IsActive && barrier.IsOutside(goal))
            { goal = barrier.Center + (goal - barrier.Center).normalized * Mathf.Max(0f, barrier.Radius - _config.FollowDistance); }
            float distance = PlanarDistance(goal, _controller.transform.position);
            if (distance > _config.FormationTolerance + _config.FollowHysteresis) { _following = true; }
            if (distance <= _config.FormationTolerance) { _following = false; }
            if (!_following) { _catchingUp = false; return BehaviourState.Failure; }
            ActiveNode = "Follow";
            _move = _navigation.Direction(_controller.transform.position, goal, _now);
            return BehaviourState.Running;
        }

        private void ResolveLeader()
        {
            if ((_leader == null) || _leader.IsDead || _leader.IsSwallowed)
            {
                _leader = null;
                foreach (PlayerStatus player in PlayerStatus.ServerPlayers)
                {
                    if ((player == null) || (!player.Participant.IsValid) ||
                        (player.Participant.Kind != ParticipantKind.Human) || player.IsDead || player.IsSwallowed) { continue; }
                    if ((_leader == null) || (player.Participant.CompareTo(_leader.Participant) < 0)) { _leader = player; }
                }
                _leaderController = _leader == null ? null : _leader.GetComponent<PlayerController>();
            }
        }

        private static float PlanarDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
        private BehaviourState Support()
        {
            if (_sensor.HasVisibleThreat(Forward, _config.RescueSafetyRadius))
            { CancelSupport(); return BehaviourState.Failure; }
            if (_revive != null && _revive.IsRescuing)
            { ActiveNode = "Rescue"; _move = Vector2.zero; return BehaviourState.Running; }
            if (_now < _nextSupport) { return BehaviourState.Failure; }
            int injured = 0;
            bool critical = false;
            PlayerStatus rescue = null;
            float closest = float.PositiveInfinity;
            foreach (PlayerStatus ally in PlayerStatus.ServerPlayers)
            {
                if (ally == null) { continue; }
                float distance = PlanarDistance(ally.transform.position, _controller.transform.position);
                if (!ally.IsDead && !ally.IsSwallowed && distance <= _config.HealRadius)
                {
                    float ratio = ally.Health / Mathf.Max(1f, ally.MaxHealth);
                    if (ratio < _config.HealGroupRatio) { ++injured; }
                    critical |= ratio < _config.HealCriticalRatio;
                }
                if (ally != _status && (ally.IsDead || ally.IsSwallowed) && distance < closest)
                { rescue = ally; closest = distance; }
            }
            if (_weapon.Weapon == WeaponKind.Bell && _heal != null && (critical || injured >= 2) &&
                _threats.EarliestImpact(_now) - _now >= _config.HealCommitSeconds &&
                BarrierStateLocator.Current.GetHealingMultiplier(_controller.transform.position) > 0f)
            {
                _nextSupport = _now + _config.SupportRetrySeconds;
                if (_heal.ServerRequestHeal(++_request) == ActionRejection.None)
                { ++AcceptedHeals; _move = Vector2.zero; ActiveNode = "Heal"; return BehaviourState.Running; }
            }
            if (rescue == null || _revive == null || _status.Stamina < _skill.StaminaReserve) { return BehaviourState.Failure; }
            ActiveNode = "Rescue";
            if (closest > _config.RescueRange)
            { _move = _navigation.Direction(_controller.transform.position, rescue.transform.position, _now); return BehaviourState.Running; }
            _move = Vector2.zero;
            if (!_rescuers.TryGetValue(rescue.netId, out ReviveController target))
            { target = rescue.GetComponent<ReviveController>(); _rescuers[rescue.netId] = target; }
            _nextSupport = _now + _config.SupportRetrySeconds;
            if (target == null || _rescueHold == uint.MaxValue) { return BehaviourState.Failure; }
            bool started = _revive.ServerBeginRescue(rescue.netId,
                rescue.IsSwallowed ? PlayerRescueTargetKind.Swallowed : PlayerRescueTargetKind.Spectating,
                rescue.IsSwallowed ? rescue.SwallowGeneration : target.DeathGeneration, ++_rescueHold);
            if (started) { ++StartedRescues; }
            return started ? BehaviourState.Running : BehaviourState.Failure;
        }
        private void CancelSupport()
        {
            if (_revive != null && _revive.IsRescuing && _rescueHold != 0u)
            { _revive.ServerCancelRescueRequest(_rescueHold); _nextSupport = _now + _config.SupportRetrySeconds; }
        }
        private BehaviourState Safety()
        {
            IBarrierState barrier = BarrierStateLocator.Current;
            if (barrier == null || !barrier.IsActive) { return GateGather(); }
            Vector3 center = barrier.Center;
            float radius = barrier.Radius;
            if (barrier is IBarrierTransitionSource source && source.Snapshot.IsActive)
            { center = source.Snapshot.TargetCenter; radius = source.Snapshot.TargetRadius; }
            Vector3 offset = _controller.transform.position - center;
            offset.y = 0f;
            if (offset.magnitude < radius - _config.FollowDistance) { return GateGather(); }
            ActiveNode = "Barrier";
            _scheduled.Pending = false;
            Vector3 safe = center + offset.normalized * Mathf.Max(0f, radius - _config.FollowDistance);
            _move = _navigation.Direction(_controller.transform.position, safe, _now);
            return BehaviourState.Running;
        }
        private BehaviourState GateGather()
        {
            if (_entrance == null || RunManager.ServerInstance == null ||
                RunManager.ServerInstance.EntranceState != BossEntranceState.CountingDown) { return BehaviourState.Failure; }
            ActiveNode = "GateGather"; _scheduled.Pending = false;
            _move = _navigation.Direction(_controller.transform.position, _entrance.transform.position, _now);
            return BehaviourState.Running;
        }
        private BehaviourState Defend()
        {
            foreach (var threat in _threats.Threats)
            {
                if (threat.Missed || _now < threat.ReadyAt || threat.Impact < _now ||
                    threat.Impact - _now > _skill.AttackCommitSeconds) { continue; }
                ActiveNode = "Evade"; _scheduled.Pending = false;
                Vector3 side = threat.EscapeDirection;
                _move = _navigation.Direction(_controller.transform.position,
                    _controller.transform.position + side * _config.RetreatDistance, _now);
                return BehaviourState.Running;
            }
            return BehaviourState.Failure;
        }
        private void ExecuteDefense()
        {
            if (_threats == null) { return; }
            foreach (var threat in _threats.Threats)
            {
                if (threat.Missed || threat.Consumed || _now < threat.ExecuteAt ||
                    !_threats.Valid(threat, _sensor, Forward, _now)) { continue; }
                threat.Consumed = true;
                if (_now > threat.Impact + _config.ScheduledLifetime) { continue; }
                if (_weapon.Weapon == WeaponKind.Sword && _parry != null &&
                    _status.Stamina >= _skill.StaminaReserve + _config.ParryReserveCost &&
                    (threat.Parryable || _random.NextDouble() < _skill.FalseParryChance))
                { ++ParryAttempts; _parry.ServerRequestParry(++_request); }
                else if (_roll != null)
                {
                    Vector3 side = threat.EscapeDirection;
                    if (_random.NextDouble() < _skill.WrongDirectionChance) { side = -side; }
                    if (_roll.ServerRequestRoll(++_request, new Vector2(side.x, side.z)) == ActionRejection.None) { ++AcceptedRolls; }
                }
            }
        }
        private void CancelCombat() { _scheduled.Pending = false; _navigation?.Clear(); CancelPrecision(); }
        private void OnPaused(RunPausedEvent fact) { Reset(); ActiveNode = "Blocked"; }
        private void OnDamaged(uint attacker) { _sensor?.RememberHit(attacker, NetworkTime.time); }
        private void OnResumed(RunResumedEvent fact) => Reset();
        private void OnPhaseChanged(RunPhaseChangedEvent fact)
        {
            if ((fact.Current == RunPhase.Result) || (fact.Current == RunPhase.Lobby))
            { Reset(); ActiveNode = "Blocked"; }
        }
        private void Reset()
        {
            CancelBranch(); CancelCombat(); CancelSupport(); _target = null; _leader = null;
            _observation?.Clear();
            _threats?.Clear();
            _following = false; _regrouping = false; _catchingUp = false; _move = Vector2.zero; _nextDecision = 0d;
            _controller.ServerClearMoveInput();
            if (Generation < uint.MaxValue) { ++Generation; }
        }
        public void Dispose()
        {
            if (_status != null) { _status.ServerDamagedBy -= OnDamaged; }
            _graphAgent?.End();
            Reset(); _sensor?.Clear();
            if (_graphAgent != null)
            {
                if (Application.isPlaying) { UnityEngine.Object.Destroy(_graphAgent); }
                else { UnityEngine.Object.DestroyImmediate(_graphAgent); }
            }
            if (_events != null)
            {
                _events.Unsubscribe<RunPausedEvent>(OnPaused);
                _events.Unsubscribe<RunResumedEvent>(OnResumed);
                _events.Unsubscribe<RunPhaseChangedEvent>(OnPhaseChanged);
            }
        }
    }
}
