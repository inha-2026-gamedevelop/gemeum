using System;
using System.Collections.Generic;
using Geumeum.Common;
using Geumeum.Data;
using UnityEngine;

namespace Geumeum.AI
{
    /// <summary> 보이는 표현만 읽고 최대 두 공격의 최초 관측, 반응 지연을 보관한다 </summary>
    public sealed class BotThreatTracker
    {
        public sealed class Threat
        {
            public BotPerception.Target Source;
            public uint Attack;
            public byte Repeat;
            public double ObservedAt, ReadyAt, Impact, ExecuteAt;
            public bool Missed, Consumed, Parryable;
            public Vector3 Direction;
            public Vector3 EscapeDirection;
        }
        private readonly List<Threat> _tracked = new List<Threat>(2);
        private readonly List<BotPerception.Target> _visible = new List<BotPerception.Target>();
        private readonly BotSkillProfileSO _skill;
        private readonly System.Random _random;
        private readonly Dictionary<uint, (uint Attack, double Start)> _bossRhythms = new Dictionary<uint, (uint, double)>();
        public IReadOnlyList<Threat> Threats => _tracked;
        public BotThreatTracker(BotSkillProfileSO skill, System.Random random) { _skill = skill; _random = random; }

        public void Observe(BotPerception sensor, Vector3 forward, Vector3 self, uint selfId, double now)
        {
            for (int i = _tracked.Count - 1; i >= 0; --i)
            { if (!Valid(_tracked[i], sensor, forward, now)) { _tracked.RemoveAt(i); } }
            _visible.Clear();
            foreach (var target in sensor.KnownTargets)
            { if (sensor.Visible(target, forward)) { _visible.Add(target); } }
            _visible.Sort((a, b) =>
            {
                int priority = TargetsSelf(b, selfId).CompareTo(TargetsSelf(a, selfId));
                return priority != 0 ? priority : (a.Point - self).sqrMagnitude.CompareTo((b.Point - self).sqrMagnitude);
            });
            foreach (var target in _visible)
            {
                if (_tracked.Count >= Math.Min(2, _skill.MaxTrackedThreats)) { break; }
                if (target.Telegraph != null)
                {
                    foreach (BossTelegraphCue cue in target.Telegraph.TelegraphCues)
                    {
                        if (_tracked.Count >= Math.Min(2, _skill.MaxTrackedThreats)) { break; }
                        if (!cue.IsPendingAt(now)) { continue; }
                        // 뒤늦게 관측해도 ReadyAt은 최초 관측 시각부터 계산한다
                        Threat observed = Add(target, cue.AttackInstanceId, cue.RepeatIndex, now, EstimateImpact(target, cue),
                            cue.Direction, cue.TargetNetId == selfId,
                            (cue.PatternId == 1 || cue.PatternId == 6) && cue.RepeatIndex == 2);
                        if (GameDB.IsInitialized && GameDB.Boss.TryGetPattern(cue.PatternId, out var pattern) &&
                            pattern.Template == BossPatternSO.PatternTemplate.MarkedCircle)
                        {
                            Vector3 away = self - cue.Origin; away.y = 0f;
                            observed.EscapeDirection = away.sqrMagnitude > 0.001f ? away.normalized : -forward;
                        }
                    }
                }
                else if (IsEnemyAttack(target))
                {
                    EnemyStatus enemy = target.Health as EnemyStatus;
                    double impact = now + (enemy == null ? _skill.ReactionMean : enemy.WindupDuration);
                    Vector3 direction = target.Identity.transform.forward;
                    bool aimed = Vector3.Dot(direction, (self - target.Point).normalized) > 0f;
                    Add(target, target.Enemy.ObservedAttackExecutionId, 0, now, impact, direction, aimed,
                        enemy != null && enemy.Kind == EnemyKind.Changgwi);
                }
            }
        }

        private static bool TargetsSelf(BotPerception.Target target, uint selfId)
        {
            if (target.Telegraph == null) { return false; }
            foreach (var cue in target.Telegraph.TelegraphCues)
            { if (cue.IsActive && cue.TargetNetId == selfId) { return true; } }
            return false;
        }

        private double EstimateImpact(BotPerception.Target target, BossTelegraphCue cue)
        {
            if (!GameDB.IsInitialized || GameDB.Boss == null || !GameDB.Boss.TryGetPattern(cue.PatternId, out var pattern))
            { return cue.ImpactServerTime; }
            float meanJitter = (pattern.RepeatTimingJitterMin + pattern.RepeatTimingJitterMax) * .5f;
            uint id = target.Identity.netId;
            if (!_bossRhythms.TryGetValue(id, out var rhythm) || rhythm.Attack != cue.AttackInstanceId)
            {
                // 첫 타를 놓쳤으면 지금 보이는 반복 시작과 평균 간격으로 추정한다
                double start = cue.RepeatIndex == 0 ? cue.ImpactServerTime - pattern.GetImpactOffset(0) :
                    cue.StartServerTime - pattern.GetImpactOffset(cue.RepeatIndex - 1) - meanJitter * (cue.RepeatIndex - 1);
                rhythm = (cue.AttackInstanceId, start); _bossRhythms[id] = rhythm;
            }
            return rhythm.Start + pattern.GetImpactOffset(cue.RepeatIndex) + meanJitter * cue.RepeatIndex;
        }

        private Threat Add(BotPerception.Target source, uint attack, byte repeat, double now,
            double impact, Vector3 direction, bool aimed, bool parryable)
        {
            foreach (var threat in _tracked)
            { if (threat.Source == source && threat.Attack == attack && threat.Repeat == repeat) { return threat; } }
            double ready = now + _skill.SampleReaction(_random);
            double estimate = impact + BotSkillProfileSO.Gaussian(_random) * _skill.TimingSigma;
            var observed = new Threat
            {
                Source = source, Attack = attack, Repeat = repeat, ObservedAt = now, ReadyAt = ready,
                Impact = estimate, ExecuteAt = Math.Max(ready, estimate - _skill.RollLeadSeconds),
                Direction = direction, Parryable = aimed && parryable,
                EscapeDirection = Vector3.Cross(Vector3.up, direction).normalized,
                Missed = _random.NextDouble() < _skill.MissChance * (aimed ? 1d : 2d),
            };
            _tracked.Add(observed);
            return observed;
        }

        public static bool IsEnemyAttack(BotPerception.Target source) => source.Enemy != null &&
            (source.Enemy.VisualState == EnemyVisualState.Telegraph || source.Enemy.VisualState == EnemyVisualState.Charge ||
             source.Enemy.VisualState == EnemyVisualState.Attack);

        public bool Valid(Threat threat, BotPerception sensor, Vector3 forward, double now)
        {
            if (!sensor.Visible(threat.Source, forward)) { return false; }
            if (threat.Source.Telegraph != null)
            {
                foreach (BossTelegraphCue cue in threat.Source.Telegraph.TelegraphCues)
                { if (cue.IsPendingAt(now) && cue.AttackInstanceId == threat.Attack && cue.RepeatIndex == threat.Repeat) { return true; } }
                return false;
            }
            return IsEnemyAttack(threat.Source) && threat.Source.Enemy.ObservedAttackExecutionId == threat.Attack;
        }
        public double EarliestImpact(double now)
        {
            double earliest = double.PositiveInfinity;
            foreach (var threat in _tracked)
            { if (!threat.Missed && now >= threat.ReadyAt && threat.Impact >= now) { earliest = Math.Min(earliest, threat.Impact); } }
            return earliest;
        }
        public void Clear() { _tracked.Clear(); _visible.Clear(); _bossRhythms.Clear(); }
    }
}
