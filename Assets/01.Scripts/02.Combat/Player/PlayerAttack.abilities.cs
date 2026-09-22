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
        private sealed class AbilityTarget
        {
            public Component Component;
            public IDamageable Damage;
            public TargetRef Reference;
            public readonly HashSet<DirectAttackKey> Hits = new HashSet<DirectAttackKey>();
            public double LastCharge;
            public bool Valid => Component != null && !Damage.IsDead &&
                (!(Damage is ITargetPartIdSource part) || part.IsTargetable);
        }

        private static readonly List<PlayerAttack> AbilityOwners = new List<PlayerAttack>();
        [SerializeField] private KeyCode _echoRecallKey = KeyCode.Q;
        [SyncVar] private WeaponAbilityMark _wedge;
        [SyncVar] private WeaponAbilityMark _echoFirst;
        [SyncVar] private WeaponAbilityMark _echoSecond;
        [SyncVar] private double _echoLockEnd;
        [SyncVar] private double _echoCooldownEnd;
        private AbilityTarget _wedgeTarget;
        private readonly AbilityTarget[] _echoTargets = new AbilityTarget[2];
        private uint _abilityGeneration;
        private uint _echoExecution;
        private uint _nextEchoRequest = 1u;
        private RequestIdTracker _echoRequests;
        private double _echoImpact;
        private float _echoPower;
        private float _echoResonance = 1f;
        private float _echoFieldDamage = 1f;
        private float _echoFieldExtend;

        // 「지키는 길」이 준 Q 소모 감소 권한. 같은 종류는 중첩하지 않고 최신 하나로 바뀐다
        private float _echoDiscount;
        private double _echoDiscountEnd;
        private HealController _abilityHeal;
        public event Action<WeaponAbilityEvent, Vector3, byte, double> AbilityPresented;
        public WeaponAbilityMark Wedge => _wedge;
        public WeaponAbilityMark EchoFirst => _echoFirst;
        public WeaponAbilityMark EchoSecond => _echoSecond;
        public bool IsRecalling => RunTimeLocator.Current.GameplayElapsedSeconds < _echoLockEnd;
        public float EchoCooldownRemaining => (float)Math.Max(0d,
            _echoCooldownEnd - RunTimeLocator.Current.GameplayElapsedSeconds);

        private static double AbilityTime => RunTimeLocator.Current.GameplayElapsedSeconds;
        private WeaponAbilityMark GetEcho(int index) => index == 0 ? _echoFirst : _echoSecond;
        private void SetEcho(int index, WeaponAbilityMark value)
        {
            if (index == 0) { _echoFirst = value; } else { _echoSecond = value; }
        }

        private static AbilityTarget MakeAbilityTarget(IDamageable damage)
        {
            Component component = damage as Component;
            if (component == null || !(damage is IDamageHealthSource) || damage.IsDead ||
                (damage is ITargetPartIdSource part && !part.IsTargetable)) { return null; }
            NetworkIdentity identity = component.GetComponentInParent<NetworkIdentity>();
            RunManager run = RunManager.ServerInstance;
            RunSceneDirector scene = RunSceneDirector.ServerInstance;
            if (identity == null || identity.netId == 0 || run == null || scene == null) { return null; }
            return new AbilityTarget { Component = component, Damage = damage,
                Reference = TargetRef.Create(run.RunId, scene.Revision, identity.netId,
                    damage is ITargetPartIdSource source ? source.TargetPartId : (byte)0) };
        }

        private static bool IsCurrent(AbilityTarget target)
        {
            return target != null && target.Valid && RunManager.ServerInstance != null &&
                RunSceneDirector.ServerInstance != null && target.Reference.IsCurrent(
                    RunManager.ServerInstance.RunId, RunSceneDirector.ServerInstance.Revision);
        }

        /// <summary> 실제 HP를 깎은 직접 공격만 이 경로를 호출한다. 파생 피해는 재진입하지 않는다 </summary>
        [Server]
        public static bool ServerNotifyDirectHit(uint attacker, uint action, bool precision,
            bool bell, IDamageable damage, Vector3 point, Vector3 rayOrigin,
            Vector3 rayDirection, float rayDistance, float basePower, in CombatStatsSnapshot talents = default, uint ownerGeneration = 0)
        {
            if (action == 0 || !GameDB.IsInitialized) { return false; }
            AbilityTarget target = MakeAbilityTarget(damage);
            if (target == null) { return false; }
            bool burst = false;
            DirectAttackKey key = new DirectAttackKey(attacker, action, precision);
            for (int i = AbilityOwners.Count - 1; i >= 0; --i)
            {
                PlayerAttack owner = AbilityOwners[i];
                if (owner == null) { AbilityOwners.RemoveAt(i); continue; }
                if (owner._status == null || owner._status.IsDead || owner._status.IsSwallowed ||
                    owner._weapon == null || !owner._weapon.HasWeapon) { continue; }
                if (owner._weapon.Weapon == WeaponKind.Bell)
                {
                    owner.ChargeEcho(target, key, bell && owner.netId == attacker, point);
                }
                if (owner.netId == attacker && owner._weapon.Weapon == WeaponKind.Bow &&
                    (ownerGeneration == 0 || ownerGeneration == owner.CombatGeneration))
                {
                    var hit = new WedgeHitContext { Key = key, Precision = precision, Origin = rayOrigin,
                        Direction = rayDirection, Distance = rayDistance, Power = basePower,
                        FirstHitCooldown = talents.WedgeFirstHitCooldown,
                        BurstPercent = precision ? talents.WedgePrecisionBurstPercent : talents.WedgeNormalBurstPercent };
                    burst |= owner.HitWedge(target, in hit);
                }
            }
            return burst;
        }

        private struct WedgeHitContext
        {
            public DirectAttackKey Key;
            public bool Precision;
            public Vector3 Origin, Direction;
            public float Distance, Power, FirstHitCooldown, BurstPercent;
        }

        private bool HitWedge(AbilityTarget target, in WedgeHitContext hit)
        {
            CombatConfigSO config = GameDB.Combat;
            bool same = _wedge.IsActive(AbilityTime) && IsCurrent(_wedgeTarget) &&
                _wedge.Target.Equals(target.Reference);
            if (same)
            {
                if (!_wedgeTarget.Hits.Add(hit.Key)) { return false; }
                Vector3 wedgePoint = _wedgeTarget.Component.transform.TransformPoint(_wedge.LocalPoint);
                if (!WeaponAbilityRules.IntersectsWedge(hit.Origin, hit.Direction, hit.Distance, wedgePoint, config.WedgeRadius)) { return false; }
                if (_wedge.Count == 0)
                {
                    _bowSkillReady = System.Math.Max(AbilityTime, _bowSkillReady - hit.FirstHitCooldown);
                }

                WeaponAbilityMark mark = _wedge;
                mark.Count += (byte)(hit.Precision ? config.WedgeHits : 1);
                _wedge = mark;
                if (mark.Count < config.WedgeHits)
                {
                    RpcAbilityEvent(WeaponAbilityEvent.WedgeCracked, wedgePoint, mark.Count, AbilityTime);
                    return false;
                }
                _wedge = default;
                _wedgeTarget = null;
                target.Damage.TakeDamage(new DamageInfo(hit.Power * config.WedgeBurstMultiplier * (1f + hit.BurstPercent * .01f),
                    netId, ResolveParticipantId(), HitStrength.Heavy));
                RpcAbilityEvent(WeaponAbilityEvent.WedgeBurst, wedgePoint, mark.Count, AbilityTime);
                return true;
            }
            return false;
        }

        [Server]
        private void InstallMeleeWedge(IDamageable damage, Vector3 point)
        {
            AbilityTarget target = MakeAbilityTarget(damage);
            if (target == null) { return; }
            _wedgeTarget = target;
            _wedge = NewMark(target, point, GameDB.Combat.WedgeDuration + _skillStats.WedgeDurationBonus, 0);
            if (_skillStats.WedgeRollReduction > 0)
            {
                _wedgeRollDiscount = _skillStats.WedgeRollReduction;
                _wedgeRollEnd = AbilityTime + GameDB.Combat.CombatTokenDuration;
            }
            RpcAbilityEvent(WeaponAbilityEvent.WedgePlaced, point, 0, AbilityTime);
        }

        private WeaponAbilityMark NewMark(AbilityTarget target, Vector3 point, float duration, byte count)
        {
            if (++_abilityGeneration == 0) { ++_abilityGeneration; }
            return new WeaponAbilityMark { Target = target.Reference, Generation = _abilityGeneration,
                LocalPoint = target.Component.transform.InverseTransformPoint(point), Count = count,
                ExpiresAt = AbilityTime + duration };
        }

        private void ChargeEcho(AbilityTarget target, DirectAttackKey key, bool canInstall, Vector3 point)
        {
            int free = -1;
            for (int i = 0; i < 2; ++i)
            {
                WeaponAbilityMark mark = GetEcho(i);
                if (!mark.IsActive(AbilityTime) || !IsCurrent(_echoTargets[i]))
                { if (free < 0) { free = i; } continue; }
                if (!mark.Target.Equals(target.Reference)) { continue; }
                AbilityTarget existing = _echoTargets[i];
                if (!existing.Hits.Add(key) || mark.Reservation != 0 || mark.Count >= 3 ||
                    AbilityTime - existing.LastCharge < GameDB.Combat.EchoChargeInterval) { return; }
                ++mark.Count;
                existing.LastCharge = AbilityTime;
                SetEcho(i, mark);
                RpcAbilityEvent(WeaponAbilityEvent.EchoCharged, point, mark.Count, AbilityTime);
                return;
            }
            if (!canInstall || free < 0) { return; }
            target = MakeAbilityTarget(target.Damage);
            if (target == null) { return; }
            target.Hits.Add(key);
            target.LastCharge = AbilityTime;
            _echoTargets[free] = target;
            // 유지 시간은 붙이는 순간의 특성으로 정한다. 충전마다 다시 조회하지 않는다
            float duration = GameDB.Combat.EchoDuration;
            if (_loadoutProvider != null &&
                _loadoutProvider.TryBuildLoadout(ResolveParticipantId(), out CombatStatsSnapshot stats) && stats.IsValid)
            { duration += stats.EchoDurationBonus; }
            SetEcho(free, NewMark(target, point, duration, 1));
            RpcAbilityEvent(WeaponAbilityEvent.EchoCharged, point, 1, AbilityTime);
        }

        public bool RequestEchoRecall()
        {
            if (TryBufferCombatAction(2, () => RequestEchoRecall()))
            {
                return true;
            }

            if (!isLocalPlayer || !NetworkClient.active || !LocalGameplayInputGate.AllowsGameplayInput ||
                _weapon == null || !_weapon.HasWeapon || _weapon.Weapon != WeaponKind.Bell ||
                (_status != null && _status.IsHitReacting)) { return false; }
            // 소유자의 최신 런/씬 참조를 함께 보내 지연된 이전 씬 입력을 거부한다
            WeaponAbilityMark mark = _echoFirst.IsActive(AbilityTime) ? _echoFirst : _echoSecond;
            _clientMoveLockUntilTime = NetworkTime.time + Constants.Combat.CLIENT_ATTACK_MOVE_LOCK;
            CmdEchoRecall(_nextEchoRequest++, mark.Target.RunIdValue, mark.Target.SceneRevisionValue);
            return true;
        }

        [Command]
        private void CmdEchoRecall(uint request, uint run, uint scene)
        {
            if (_status != null && _status.Participant.Kind == ParticipantKind.Bot) { return; }
            ActionRejection rejection = ServerRequestEchoRecall(request, run, scene);
            if (rejection != ActionRejection.None && connectionToClient != null) { TargetActionRejected(rejection); }
        }

        [Server]
        public ActionRejection ServerRequestEchoRecall(uint request, uint run, uint scene)
        {
            if (!_echoRequests.TryAccept(request)) { return ActionRejection.Duplicate; }
            RunManager manager = RunManager.ServerInstance;
            RunSceneDirector director = RunSceneDirector.ServerInstance;
            if (!GameDB.IsInitialized || _status == null || manager == null || director == null ||
                manager.RunId.Value != run || director.Revision.Value != scene || RunSceneDirector.IsServerTransitioning ||
                !PlayerActionGate.CanAct(manager.Phase, _status.LifeState, manager.IsPaused)) { return ActionRejection.State; }
            if (_weapon == null || !_weapon.HasWeapon || _weapon.Weapon != WeaponKind.Bell) { return ActionRejection.NoWeapon; }
            if (_status.IsHitReacting || IsActionInProgress(NetworkTime.time) ||
                (_roll != null && _roll.IsRolling) || (_parry != null && _parry.IsActionInProgress(NetworkTime.time)) ||
                (_abilityHeal != null && _abilityHeal.IsActionInProgress(NetworkTime.time)) ||
                AbilityTime < _echoCooldownEnd) { return ActionRejection.Busy; }
            if (!CanRecall(0) && !CanRecall(1)) { return ActionRejection.InvalidInput; }
            if (_loadoutProvider == null || !_loadoutProvider.TryBuildLoadout(ResolveParticipantId(), out CombatStatsSnapshot loadout) ||
                !loadout.IsValid) { return ActionRejection.NotReady; }
            CombatConfigSO config = GameDB.Combat;
            float discount = loadout.EchoSpiritCostReduction + PeekEchoDiscount();
            if (!_status.TrySpendSpirit(loadout.ResolveSpiritCost(config.EchoSpiritCost, discount)))
            { return ActionRejection.Resource; }
            ConsumeEchoDiscount();
            // 비용이 확정된 이 순간의 계수와 연계 노드 값을 이번 회수에 고정한다
            _echoResonance = ResolveResonanceMultiplier();
            _echoFieldDamage = loadout.EchoFieldDamageMultiplier;
            _echoFieldExtend = loadout.FieldEchoExtendSeconds;
            _echoExecution = TakeNextServerActionId();
            _echoPower = loadout.MeleeAttackPower;
            _echoImpact = AbilityTime + loadout.ResolveAttackDuration(config.EchoStartupDuration);
            _echoLockEnd = _echoImpact + loadout.ResolveAttackDuration(config.EchoRecoveryDuration);
            _echoCooldownEnd = AbilityTime + config.EchoCooldown;
            for (int i = 0; i < 2; ++i)
            {
                if (!CanRecall(i)) { continue; }
                WeaponAbilityMark mark = GetEcho(i);
                mark.Reservation = _echoExecution;
                SetEcho(i, mark);
            }
            RpcEchoStarted(new PlayerActionCue { ExecutionId = _echoExecution,
                Kind = PlayerActionCueKind.EchoRecall, Weapon = WeaponKind.Bell,
                StartGameplayTime = AbilityTime, ImpactGameplayTime = _echoImpact, EndGameplayTime = _echoLockEnd });
            RpcAbilityEvent(WeaponAbilityEvent.EchoRecall, transform.position, 0, AbilityTime);
            return ActionRejection.None;
        }

        /// <summary>
        /// 진 안 부상자를 실제로 치유했을 때 생기는 Q 소모 감소 권한
        /// 같은 종류는 중첩하지 않고 최신 하나로 바뀌며, 다음 회수 한 번에만 쓴다
        /// </summary>
        [Server]
        public void ServerGrantEchoDiscount(float amount, float window)
        {
            if (amount <= 0f || window <= 0f || float.IsNaN(amount) || float.IsInfinity(amount))
            { return; }
            _echoDiscount = amount;
            _echoDiscountEnd = AbilityTime + window;
        }

        /// <summary> 아직 살아 있는 할인 권한의 크기. 소비하지 않고 보기만 한다 </summary>
        public float EchoDiscountRemaining => AbilityTime < _echoDiscountEnd ? _echoDiscount : 0f;

        private float PeekEchoDiscount() => EchoDiscountRemaining;

        [Server]
        private void ConsumeEchoDiscount() { _echoDiscount = 0f; _echoDiscountEnd = 0d; }

        private bool CanRecall(int index)
        {
            WeaponAbilityMark mark = GetEcho(index);
            AbilityTarget target = _echoTargets[index];
            if (!mark.IsActive(AbilityTime) || !IsCurrent(target)) { return false; }
            Vector3 point = target.Component.transform.TransformPoint(mark.LocalPoint);
            Vector3 origin = transform.position + Vector3.up * GameDB.Combat.RangedMuzzleHeight;
            return Vector3.Distance(origin, point) <= GameDB.Combat.EchoRecallRange &&
                !Physics.Linecast(origin, point, GameDB.Arrow.WorldMask, QueryTriggerInteraction.Ignore);
        }

        [Server]
        private void ServerUpdateAbilities()
        {
            if (_status == null || _status.IsDead || _status.IsSwallowed ||
                (RunSceneDirector.ServerInstance != null && RunSceneDirector.IsServerTransitioning) || RunManager.ServerInstance == null)
            { ClearWeaponAbilities(); return; }
            if (RunManager.ServerInstance.IsPaused) { return; }
            if (!_wedge.IsActive(AbilityTime) || !IsCurrent(_wedgeTarget)) { _wedge = default; _wedgeTarget = null; }
            for (int i = 0; i < 2; ++i)
            {
                if (!GetEcho(i).IsActive(AbilityTime) || !IsCurrent(_echoTargets[i]))
                { SetEcho(i, default); _echoTargets[i] = null; }
            }
            if (_echoExecution == 0 || AbilityTime < _echoImpact) { return; }
            for (int i = 0; i < 2; ++i)
            {
                WeaponAbilityMark mark = GetEcho(i);
                if (mark.Reservation != _echoExecution) { continue; }
                if (CanRecall(i))
                {
                    AbilityTarget target = _echoTargets[i];
                    Vector3 point = target.Component.transform.TransformPoint(mark.LocalPoint);
                    float before = ((IDamageHealthSource)target.Damage).Health;
                    // 「거두는 길」의 회수 피해 노드는 그 적이 자기 진 안에 있을 때만 붙는다
                    // 시전자가 진 안인지와는 별개 조건이다
                    float bonus = ServerIsInsideField(point) ? _echoFieldDamage : 1f;
                    target.Damage.TakeDamage(new DamageInfo(
                        _echoPower * _echoResonance * bonus * GameDB.Combat.EchoDamageMultiplier(mark.Count),
                        netId, ResolveParticipantId(), HitStrength.Heavy));
                    if (((IDamageHealthSource)target.Damage).Health < before)
                    {
                        SetEcho(i, default); _echoTargets[i] = null;
                        RpcAbilityEvent(WeaponAbilityEvent.EchoBurst, point, mark.Count, AbilityTime);
                        // 「잔울림」: 실제로 거둔 회수만 진을 한 번 늘린다
                        ServerTryExtendField(_echoFieldExtend);
                        continue;
                    }
                }
                mark.Reservation = 0;
                SetEcho(i, mark);
            }
            _echoExecution = 0;
        }

        [Server]
        private void CancelEchoRecall()
        {
            if (_echoExecution != 0) { RpcEchoCancelled(_echoExecution); }
            for (int i = 0; i < 2; ++i)
            {
                WeaponAbilityMark mark = GetEcho(i);
                mark.Reservation = 0;
                SetEcho(i, mark);
            }
            _echoExecution = 0;
            _echoLockEnd = 0;
        }

        [Server]
        public void ResetForNewRun()
        {
            ClearWeaponAbilities();
            ResetWeaponSkillCooldowns();
        }

        [Server]
        private void ClearWeaponAbilities()
        {
            if (++_combatGeneration == 0)
            {
                ++_combatGeneration;
            }

            ClearCombatTalentTokens();
            _gauge?.ServerReset();
            _serverPlacementAiming = false;
            CancelWeaponSkill(true);
            CancelEchoRecall();
            _wedge = default; _wedgeTarget = null;
            _echoFirst = default; _echoSecond = default;
            _echoTargets[0] = null; _echoTargets[1] = null;
            _echoCooldownEnd = 0;
            _echoResonance = 1f; _echoFieldDamage = 1f; _echoFieldExtend = 0f;
            ConsumeEchoDiscount();
        }

        [ClientRpc]
        private void RpcEchoStarted(PlayerActionCue cue) { _animation?.PlayActionCue(in cue); }
        [ClientRpc]
        private void RpcEchoCancelled(uint execution) { _animation?.CancelActionCue(PlayerActionCueKind.EchoRecall, execution); }
        [ClientRpc]
        private void RpcAbilityEvent(WeaponAbilityEvent kind, Vector3 point, byte count, double time)
        { AbilityPresented?.Invoke(kind, point, count, time); }

        [Server]
        public bool ServerTryGetWedgePoint(uint targetNetId, out Vector3 point)
        {
            point = default;
            if (!_wedge.IsActive(AbilityTime) || !IsCurrent(_wedgeTarget) || _wedge.Target.EntityNetId != targetNetId) { return false; }
            point = _wedgeTarget.Component.transform.TransformPoint(_wedge.LocalPoint);
            return true;
        }

        [Server]
        public void ServerPresentArrowHit(Vector3 point)
        { RpcAbilityEvent(WeaponAbilityEvent.ArrowContact, point, 0, AbilityTime); }

        // 물리 쿼리의 비결정적 반환 순서로 잔향 대상이 바뀌지 않도록 정렬한다
        private void SortBellTargets(int count, Vector3 origin, Vector3 forward)
        {
            Array.Sort(_hitBuffer, 0, count, Comparer<Collider>.Create((a, b) =>
            {
                if (a == null || b == null) { return a == b ? 0 : a == null ? 1 : -1; }
                Vector3 av = a.bounds.center - origin, bv = b.bounds.center - origin;
                int angle = Vector3.Angle(forward, av).CompareTo(Vector3.Angle(forward, bv));
                if (angle != 0) { return angle; }
                int distance = av.sqrMagnitude.CompareTo(bv.sqrMagnitude);
                if (distance != 0) { return distance; }
                DamageTargetResolver.TryResolve(a, out IDamageable ad);
                DamageTargetResolver.TryResolve(b, out IDamageable bd);
                AbilityTarget at = MakeAbilityTarget(ad), bt = MakeAbilityTarget(bd);
                int id = (at?.Reference.EntityNetId ?? 0).CompareTo(bt?.Reference.EntityNetId ?? 0);
                return id != 0 ? id : (at?.Reference.PartId ?? 0).CompareTo(bt?.Reference.PartId ?? 0);
            }));
        }
    }
}
