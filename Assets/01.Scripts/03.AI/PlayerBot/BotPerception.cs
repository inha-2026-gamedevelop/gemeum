using System.Collections.Generic;
using Geumeum.Common;
using Geumeum.Data;
using Mirror;
using UnityEngine;

namespace Geumeum.AI
{
    /// <summary> 실행기 내부의 패턴, 창 시각을 읽지 않는 시야 센서 </summary>
    public sealed class BotPerception
    {
        public sealed class Target
        {
            public NetworkIdentity Identity;
            public IDamageable Health;
            public Collider Body;
            public EnemyController Enemy;
            public IBossTelegraphSource Telegraph;
            public BossSoulHitbox Soul;
            public BossPartHitbox Part;
            public Target[] Souls;
            public Target[] Parts;
            public Geumeum.Systems.DroppedItem Drop;
            public Vector3 Point => (Body != null) ? Body.bounds.center : Identity.transform.position;
            public bool Alive => (Identity != null) && (Health != null) && (!Health.IsDead) &&
                (Soul == null || Soul.IsTargetable) && (Part == null || Part.IsTargetable);
        }

        private readonly Dictionary<uint, Target> _known = new Dictionary<uint, Target>();
        private readonly List<uint> _removed = new List<uint>();
        public IEnumerable<Target> KnownTargets => _known.Values;
        private readonly RaycastHit[] _hits = new RaycastHit[64];
        private readonly Transform _self;
        private readonly BotConfigSO _config;
        private readonly BotSkillProfileSO _skill;
        private uint _lastAttacker;
        private double _hitMemoryUntil;
        public void RememberHit(uint attacker, double now)
        { _lastAttacker = attacker; _hitMemoryUntil = now + _skill.HitMemorySeconds; }

        public BotPerception(Transform self, BotConfigSO config, BotSkillProfileSO skill)
        {
            _self = self; _config = config; _skill = skill;
        }

        public Target Observe(Vector3 forward)
        {
            // 컴포넌트 조회는 새로 발견한 네트워크 객체마다 한 번만 한다
            foreach (var pair in NetworkServer.spawned)
            {
                if (_known.ContainsKey(pair.Key) || (pair.Value == null)) { continue; }
                var identity = pair.Value;
                IDamageable health = identity.GetComponent<EnemyStatus>();
                if (health == null) { health = identity.GetComponent<BossStatus>(); }
                var target = new Target
                {
                    Identity = identity, Health = health,
                    Body = (health == null) ? null : identity.GetComponentInChildren<Collider>(),
                    Enemy = (health == null) ? null : identity.GetComponent<EnemyController>(),
                    Telegraph = (health == null) ? null : identity.GetComponent<IBossTelegraphSource>(),
                    Drop = identity.GetComponent<Geumeum.Systems.DroppedItem>(),
                };
                if (health is BossStatus)
                {
                    BossSoulHitbox[] souls = identity.GetComponentsInChildren<BossSoulHitbox>(true);
                    target.Souls = new Target[souls.Length];
                    for (int i = 0; i < souls.Length; ++i)
                    { target.Souls[i] = new Target { Identity = identity, Health = souls[i], Soul = souls[i], Body = souls[i].GetComponent<Collider>() }; }
                    BossPartHitbox[] parts = identity.GetComponentsInChildren<BossPartHitbox>(true);
                    target.Parts = new Target[parts.Length];
                    for (int i = 0; i < parts.Length; ++i)
                    { target.Parts[i] = new Target { Identity = identity, Health = parts[i], Part = parts[i], Body = parts[i].GetComponent<Collider>() }; }
                }
                _known.Add(pair.Key, target);
            }

            _removed.Clear();
            Target nearest = null;
            float nearestDistance = float.PositiveInfinity;
            foreach (var pair in _known)
            {
                Target target = pair.Value;
                if (target.Identity == null) { _removed.Add(pair.Key); continue; }
                if (!Visible(target, forward)) { continue; }
                float distance = (target.Point - _self.position).sqrMagnitude;
                if ((distance < nearestDistance) || ((distance == nearestDistance) &&
                    ((nearest == null) || (target.Identity.netId < nearest.Identity.netId))))
                {
                    nearest = target; nearestDistance = distance;
                }
            }
            for (int i = 0; i < _removed.Count; ++i) { _known.Remove(_removed[i]); }
            return nearest;
        }

        public bool Visible(Target target, Vector3 forward)
        {
            if ((target == null) || (!target.Alive)) { return false; }
            return InView(target, forward);
        }

        private bool InView(Target target, Vector3 forward)
        {
            Vector3 origin = _self.position + Vector3.up * _config.EyeHeight;
            Vector3 offset = target.Point - origin;
            if ((offset.sqrMagnitude > _skill.VisionRange * _skill.VisionRange) ||
                ((_lastAttacker == 0u || target.Identity.netId != _lastAttacker || NetworkTime.time > _hitMemoryUntil) &&
                 (Vector3.Angle(forward, new Vector3(offset.x, 0f, offset.z)) > _skill.VisionAngle * 0.5f)))
            { return false; }
            int count = Physics.RaycastNonAlloc(origin, offset.normalized, _hits,
                offset.magnitude, _config.ObstacleMask, QueryTriggerInteraction.Ignore);
            if (count == _hits.Length) { return false; }
            for (int i = 0; i < count; ++i)
            {
                Transform hit = _hits[i].transform;
                if (hit.IsChildOf(_self) || hit.IsChildOf(target.Identity.transform)) { continue; }
                return false;
            }
            return true;
        }

        public uint ObserveUpgrade(Vector3 forward, WeaponKind weapon, float attackPower, uint actor)
        {
            foreach (Target target in _known.Values)
            {
                var drop = target.Drop;
                if (drop == null || drop.IsClaimed || drop.Equipment.WeaponKind != weapon ||
                    drop.Equipment.AttackPower <= attackPower || !InView(target, forward) || !drop.CanInteract(actor)) { continue; }
                return drop.netId;
            }
            return 0u;
        }

        public void Clear() { _known.Clear(); _removed.Clear(); _lastAttacker = 0u; _hitMemoryUntil = 0d; }

        public Target ObserveSoul(Vector3 forward, WeaponKind weapon)
        {
            Target fallback = null;
            byte role = weapon == WeaponKind.Sword ? (byte)1 : weapon == WeaponKind.Bow ? (byte)2 : (byte)3;
            foreach (Target target in _known.Values)
            {
                if (target.Souls == null) { continue; }
                foreach (Target soul in target.Souls)
                {
                    if (!Visible(soul, forward)) { continue; }
                    if (soul.Soul.TargetPartId == BossTargetPartIds.SoulTargetPartId(role)) { return soul; }
                    if (fallback == null || (soul.Point - _self.position).sqrMagnitude < (fallback.Point - _self.position).sqrMagnitude) { fallback = soul; }
                }
            }
            return fallback;
        }

        public Target ObserveActivePart(Vector3 forward)
        {
            foreach (Target target in _known.Values)
            {
                if (target.Parts == null) { continue; }
                foreach (Target part in target.Parts) { if (Visible(part, forward)) { return part; } }
            }
            return null;
        }

        public bool HasVisibleThreat(Vector3 forward, float radius)
        {
            foreach (Target target in _known.Values)
            {
                if ((!Visible(target, forward)) || ((target.Point - _self.position).sqrMagnitude > radius * radius)) { continue; }
                if (target.Enemy != null && (target.Enemy.VisualState == EnemyVisualState.Telegraph ||
                    target.Enemy.VisualState == EnemyVisualState.Charge || target.Enemy.VisualState == EnemyVisualState.Attack)) { return true; }
                if (target.Telegraph != null)
                { foreach (BossTelegraphCue cue in target.Telegraph.TelegraphCues) { if (cue.IsActive) { return true; } } }
            }
            return false;
        }
    }
}
