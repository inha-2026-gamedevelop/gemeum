// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.AI
{
    public partial class BossController
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS
        [Server]
        public bool ServerDevBeginPattern(int patternId)
        {
            if (_serverStopped || _status.IsDead || _currentPhaseState == null ||
                RunTimeLocator.Current.IsPaused || RunSceneDirector.IsServerTransitioning ||
                _groggyPhase != GroggyPhase.Active || HasEnteredFinalSequence() ||
                !_bossData.TryGetPhase(_currentPhaseIndex, out var phase) ||
                phase.PatternIds == null || Array.IndexOf(phase.PatternIds, patternId) < 0)
            { return false; }
            _currentPhaseState.CancelActiveAttack();
            _transitionLockRemaining = 0f;
            RefreshTarget();
            return _currentPhaseState.TryBeginForcedAttack(patternId, 0);
        }

        [Server]
        public bool ServerDevEnterGroggy()
        {
            if (_serverStopped || _status.IsDead || _currentPhaseState == null ||
                RunTimeLocator.Current.IsPaused || RunSceneDirector.IsServerTransitioning || HasEnteredFinalSequence())
            { return false; }
            EnterGroggy();
            return true;
        }

        /// <summary>개발 테스트용 페이즈 재시작. 이전 공격/부위/최종 원혼 상태를 함께 초기화한다</summary>
        [Server]
        public bool ServerDevRestartPhase(int phaseIndex)
        {
            if (_serverStopped || _status == null || _status.IsDead || _bossData == null ||
                RunTimeLocator.Current.IsPaused || RunSceneDirector.IsServerTransitioning ||
                !_bossData.TryGetPhase(phaseIndex, out BossPatternSO.PhaseEntry phase))
            {
                return false;
            }
            BossPhaseStateBase next = CreatePhaseState(phaseIndex);
            if (next == null) { return false; }
            Transform target = _currentPhaseState?.Target;
            _currentPhaseState?.Exit();
            ServerReleaseSwallowed();
            MarkRegistry.ClearEntity(netId);
            _telegraphCues.Clear();
            _telegraphCue = default;
            _status.Initialize();
            _parts?.ServerInitialize(_bossData);
            _souls?.ServerInitialize(_bossData);
            _status.ServerDevSetHealthRatio(phaseIndex == 1 ? 1f : phase.EnterHealthRatio);
            ExitGroggyForFinal();
            _currentPhaseIndex = phaseIndex;
            _currentPhaseState = next;
            next.Target = target;
            _transitionLockRemaining = _bossData.TransitionLockDuration;
            CommitPhaseSnapshot(BossEncounterStage.Fighting);
            return true;
        }
#endif
        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            _bossData                = GameDB.Boss;
            _serverStopped           = false;
            _serverTerminated        = false;
            _lastResonanceAttackId   = 0u;
            _lastPrecisionInterruptedAttackId = 0u;
            _groggyGeneration        = 0u;
            _telegraphRevision       = 0u;
            _nextParryWindowId       = 0u;
            _transitionLockRemaining = 0f;
            _targetScanTimer         = 0f;
            _telegraphCue            = default;
            _telegraphCues.Clear();
            _isEnrageWarning         = false;
            _isEnraged               = false;
            _groggyPhase             = GroggyPhase.Active;
            _groggyStateTimer        = 0f;
            _status.Initialize();
            _parts?.ServerInitialize(_bossData);
            _souls?.ServerInitialize(_bossData);
            _status.Died += HandleDied;

            _groggyGauge = _bossData.GroggyGaugeMax;

            _eventBus = GameEventBus.Instance;
            if (_eventBus != null)
            {
                _eventBus.Subscribe<RunPhaseChangedEvent>(HandleRunPhaseChanged);
                _eventBus.Subscribe<RunResetEvent>(HandleRunReset);
            }

            _currentPhaseIndex = 1;
            _currentPhaseState = CreatePhaseState(_currentPhaseIndex);
            CommitPhaseSnapshot(BossEncounterStage.Fighting);

            RegisterServerTick();
        }

        public override void OnStopServer()
        {
            _status.Died -= HandleDied;

            if (_eventBus != null)
            {
                _eventBus.Unsubscribe<RunPhaseChangedEvent>(HandleRunPhaseChanged);
                _eventBus.Unsubscribe<RunResetEvent>(HandleRunReset);
                _eventBus = null;
            }

            StopServerExecution(terminate: true);
        }

        // AuthorityTickScheduler가 물리 스텝마다 TICK_PRIORITY_AI 순서로 호출한다
        private void ServerTick(float deltaTime)
        {
            if ((_serverStopped) || (_status.IsDead) || (_currentPhaseState == null))
            {
                return;
            }

            IRunTimeSource runTime = RunTimeLocator.Current;
            if ((runTime.IsPaused) || (runTime.PauseReasons != RunPauseReason.None)
                || RunSceneDirector.IsServerTransitioning)
            {
                return;
            }

            TickGroggyState(deltaTime);
            CommitPendingPhaseTransitions();

            _souls?.ServerTick(runTime.GameplayElapsedSeconds);
            ServerExpireSwallowed(runTime.GameplayElapsedSeconds);

            if (_serverStopped)
            {
                return;
            }

            if (_transitionLockRemaining > 0f)
            {
                _transitionLockRemaining = Mathf.Max(0f, _transitionLockRemaining - deltaTime);
                return;
            }

            TickEnrageState();

            // 대상이 죽거나 삼켜지면 12초 주기를 기다리지 않고 그 프레임에 바로 재평가한다
            if ((_targetDamageable != null) &&
                (_targetDamageable.IsDead || ((_targetDamageable is IBossSwallowTarget swallowedTarget) &&
                    swallowedTarget.IsSwallowed)))
            {
                _targetScanTimer = 0f;
                RefreshTarget();
            }
            else
            {
                _targetScanTimer += deltaTime;
                if (_targetScanTimer >= TARGET_SCAN_INTERVAL)
                {
                    _targetScanTimer = 0f;
                    RefreshTarget();
                }
            }

            if (_groggyPhase == GroggyPhase.Groggy)
            {
                return; // 그로기 중엔 패턴을 새로 고르거나 발동하지 않는다
            }

            if ((_souls != null) &&
                (_souls.SoulSnapshot.State == BossFinalState.SoulWindow))
            {
                return;
            }

            _currentPhaseState.Tick(deltaTime);
        }

        [Server]
        private void TickEnrageState()
        {
            if (_bossData == null)
            {
                return;
            }

            IRunTimeSource runTime = RunTimeLocator.Current;
            if ((runTime.IsPaused) || (runTime.PauseReasons != RunPauseReason.None)
                || RunSceneDirector.IsServerTransitioning)
            {
                return;
            }

            double bossElapsedSeconds = runTime.BossElapsedSeconds;
            if ((double.IsNaN(bossElapsedSeconds)) || (double.IsInfinity(bossElapsedSeconds)))
            {
                return;
            }

            if ((!_isEnrageWarning) && (bossElapsedSeconds >= _bossData.EnrageWarningSeconds))
            {
                _isEnrageWarning = true;
            }

            if ((!_isEnraged) && (bossElapsedSeconds >= _bossData.EnrageStartSeconds))
            {
                _isEnraged = true;
            }
        }

        // 그로기와 면역의 시간만 진행한다. 게이지 감소는 카운터 수락 경로가 맡는다
        [Server]
        private void TickGroggyState(float deltaTime)
        {
            if (HasEnteredFinalSequence())
            {
                return;
            }

            switch (_groggyPhase)
            {
                case GroggyPhase.Groggy:
                    _groggyStateTimer += deltaTime;
                    if ((_groggyStateTimer >= _bossData.GroggyHelplessDuration) &&
                        (_phaseSnapshot.Stage != BossEncounterStage.WakeWarning))
                    {
                        CommitPhaseSnapshot(BossEncounterStage.WakeWarning);
                    }

                    if (_groggyStateTimer >= _bossData.GroggyDuration)
                    {
                        _groggyPhase      = GroggyPhase.Immune;
                        _groggyStateTimer = 0f;
                        _groggyGauge      = _bossData.GroggyGaugeMax;
                        _status.ServerSetBodyDamageMultiplier(1f);
                        _parts?.ServerEndGroggy(_groggyGeneration);
                        CommitPhaseSnapshot(BossEncounterStage.Fighting);

                        if (_bossData.TryGetPhase(_currentPhaseIndex,
                                out BossPatternSO.PhaseEntry phase) &&
                            (phase.WakePatternId > 0) &&
                            (!ServerIsPatternSealed(phase.WakePatternId)))
                        {
                            _currentPhaseState?.TryBeginForcedAttack(phase.WakePatternId, 0);
                        }
                    }
                    break;
                case GroggyPhase.Immune:
                    _groggyStateTimer += deltaTime;
                    if (_groggyStateTimer >= _bossData.GroggyImmunityDuration)
                    {
                        _groggyPhase      = GroggyPhase.Active;
                        _groggyStateTimer = 0f;
                    }
                    break;
            }
        }

        [Server]
        private void RefreshTarget()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, TARGET_SCAN_RADIUS, _targetScanBuffer, _playerLayerMask);

            Transform    nearest           = null;
            IDamageable  nearestDamageable = null;
            float        nearestDistance   = float.MaxValue;

            for (int i = 0; i < count; ++i)
            {
                _targetScanBuffer[i].TryGetComponent(out IDamageable damageable);
                if ((damageable != null) &&
                    (damageable.IsDead || ((damageable is IBossSwallowTarget swallowed) &&
                        swallowed.IsSwallowed)))
                {
                    continue; // 관전 또는 삼켜진 플레이어는 새 공격 대상으로 삼지 않는다
                }

                float distance = Vector3.Distance(transform.position, _targetScanBuffer[i].transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance   = distance;
                    nearest           = _targetScanBuffer[i].transform;
                    nearestDamageable = damageable;
                }
            }

            _currentPhaseState.Target = nearest;
            _targetDamageable         = nearestDamageable;
        }

        [Server]
        internal int ServerCollectAliveTargets(Transform[] destination)
        {
            if ((destination == null) || (destination.Length == 0))
            {
                return 0;
            }

            int hitCount = Physics.OverlapSphereNonAlloc(transform.position, TARGET_SCAN_RADIUS,
                _targetScanBuffer, _playerLayerMask);
            int count = 0;
            for (int i = 0; (i < hitCount) && (count < destination.Length); ++i)
            {
                NetworkIdentity identity = _targetScanBuffer[i].GetComponentInParent<NetworkIdentity>();
                Transform candidate = (identity == null)
                    ? _targetScanBuffer[i].transform
                    : identity.transform;
                if (candidate.TryGetComponent(out IDamageable damageable) &&
                    (damageable.IsDead || ((damageable is IBossSwallowTarget swallowed) &&
                        swallowed.IsSwallowed)))
                {
                    continue;
                }

                bool duplicate = false;
                for (int j = 0; j < count; ++j)
                {
                    if (destination[j] == candidate)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    destination[count] = candidate;
                    ++count;
                }
            }

            return count;
        }

        /****************************************
        *              Server API
        ****************************************/

        [Server]
        private void HandleDied(uint killerNetId)
        {
            CommitPhaseSnapshot(BossEncounterStage.Defeated);
            StopServerExecution(terminate: true);

            // AI는 사실만 발행하고 런 종료는 Network가 판단한다. 여기서 결과 씬을 열지 않는다
            // BossStatus가 _isDead로 재진입을 막으므로 이 발행은 런당 한 번이다
            // HasInstance->Instance 순차 조회는 앱 종료 중 경쟁 조건이 있다, Instance를 한 번만 받는다
            GameEventBus bus = GameEventBus.Instance;
            if (bus == null)
            {
                Debug.LogError("[BossController] GameEventBus가 없어 보스 사망을 알리지 못했다, 런이 끝나지 않는다", this);
                return;
            }

            bus.Publish(EnemyDiedEvent.ForBoss(netId, transform.position, killerNetId));
        }

        // HP 조건을 만족하는 다음 페이즈로 넘어가기 전에 이전 실행을 종료한다
        [Server]
        private void CommitPendingPhaseTransitions()
        {
            while ((_bossData.TryGetPhase(_currentPhaseIndex + 1, out BossPatternSO.PhaseEntry nextPhase))
                   && (_status.HealthRatio <= nextPhase.EnterHealthRatio))
            {
                Transform carriedTarget = _currentPhaseState?.Target;
                BossPhaseStateBase nextState = CreatePhaseState(nextPhase.PhaseIndex);
                if (nextState == null)
                {
                    StopServerExecution(terminate: true);
                    return;
                }

                _currentPhaseState?.Exit();

                _currentPhaseIndex  = nextPhase.PhaseIndex;
                _currentPhaseState  = nextState;
                _currentPhaseState.Target = carriedTarget; // 다음 스캔까지 대상을 잃지 않는다
                _transitionLockRemaining = _bossData.TransitionLockDuration;
                CommitPhaseSnapshot(BossEncounterStage.Fighting);
            }
        }

        [Server]
        private BossPhaseStateBase CreatePhaseState(int phaseIndex)
        {
            switch (phaseIndex)
            {
                case 1:
                    return new Phase1State(transform, _status, this);
                case 2:
                    return new Phase2State(transform, _status, this);
                default:
                    Debug.LogError($"[BossController] {phaseIndex} 페이즈의 State가 아직 없다", this);
                    return null;
            }
        }

        /// <summary> 공격 1회의 단조 증가 ID를 발급한다, 0은 예약값이라 건너뛴다 </summary>
        [Server]
        internal uint ServerNextAttackInstanceId()
        {
            ++_nextAttackInstanceId;
            if (_nextAttackInstanceId == 0u)
            {
                ++_nextAttackInstanceId;
            }

            return _nextAttackInstanceId;
        }

        [Server]
        internal uint ServerNextParryWindowId()
        {
            ++_nextParryWindowId;
            if (_nextParryWindowId == 0u)
            {
                ++_nextParryWindowId;
            }

            return _nextParryWindowId;
        }

        [Server]
        internal float ServerGetAttackDamageMultiplier()
        {
            if (_bossData == null)
            {
                return 1f;
            }

            float enrageMultiplier = _isEnraged ? _bossData.EnrageDamageMultiplier : 1f;
            float partMultiplier = (_parts == null)
                ? 1f
                : _parts.PartSnapshot.AttackDamageMultiplier;
            return enrageMultiplier * partMultiplier;
        }

        [Server]
        internal void ServerHandlePartBroken(byte partId, bool allPartsBroken)
        {
            if (!allPartsBroken)
            {
                return;
            }

            ExitGroggyForFinal();
            _status.ServerSetInvulnerable(true);
            _status.ServerSetHealthFloorRatio(0f);
            _currentPhaseState?.CancelActiveAttack();
            _souls?.ServerBeginFinal(RunTimeLocator.Current.GameplayElapsedSeconds);
        }

        [Server]
        internal bool ServerIsPatternSealed(int patternId)
        {
            return (_parts != null) && _parts.ServerIsPatternSealed(patternId);
        }

        [Server]
        internal bool ServerIsCounterPattern(CharacterKind character, int patternId)
        {
            return ((_bossData != null) && _bossData.IsCounterPattern(character, patternId)) ||
                ((_parts != null) &&
                    _parts.ServerIsTransferredCounterPattern(character, patternId));
        }

        [Server]
        internal bool ServerIsTransferredParryRepeat(int patternId, int repeatIndex)
        {
            return (_parts != null) &&
                _parts.ServerIsTransferredParryRepeat(patternId, repeatIndex);
        }

        [Server]
        internal bool ServerMustCompleteAcceptedPattern(int patternId)
        {
            return (_bossData != null) && (patternId == _bossData.FinalFailurePatternId) &&
                (_souls != null) &&
                (_souls.SoulSnapshot.State == BossFinalState.FailureAttack);
        }

        [Server]
        internal float ServerGetGlobalCadence(int phaseIndex)
        {
            if (_bossData == null)
            {
                return 0f;
            }

            if (_isEnraged)
            {
                return _bossData.EnragedAttackIntervalSeconds;
            }

            if (phaseIndex == 1)
            {
                return _bossData.Phase1AttackIntervalSeconds;
            }

            if (phaseIndex == 2)
            {
                int brokenPartCount = (_parts == null) ? 0 : _parts.PartSnapshot.BrokenCount;
                return _bossData.GetPhase2AttackInterval(brokenPartCount);
            }

            return 0f;
        }

        /// <summary> 서버가 확정한 예고를 모든 클라이언트에 전파한다 </summary>
        [Server]
        internal void ServerBeginTelegraph(BossTelegraphCue cue)
        {
            if ((_serverStopped) || (cue.AttackInstanceId == 0u))
            {
                return;
            }

            cue.IsActive = true;
            if (double.IsNaN(cue.StartServerTime) || double.IsInfinity(cue.StartServerTime) ||
                (cue.StartServerTime <= 0d))
            {
                cue.StartServerTime = NetworkTime.time;
            }

            ++_telegraphRevision;
            if (_telegraphRevision == 0u)
            {
                ++_telegraphRevision;
            }

            cue.Revision = _telegraphRevision;
            int existingIndex = FindTelegraph(cue.AttackInstanceId, cue.RepeatIndex,
                cue.TargetNetId);
            if (existingIndex >= 0)
            {
                _telegraphCues[existingIndex] = cue;
                RefreshLegacyTelegraph();
                return;
            }

            if (_telegraphCues.Count >= MAX_ACTIVE_TELEGRAPHS)
            {
                Debug.LogError($"[BossController] 동시 예고가 {MAX_ACTIVE_TELEGRAPHS}개를 넘었다", this);
                return;
            }

            _telegraphCues.Add(cue);
            RefreshLegacyTelegraph();
        }

        /// <summary> 같은 공격의 예고만 닫는다. 중복 또는 지난 공격의 취소는 무시한다 </summary>
        [Server]
        internal void ServerCancelTelegraph(uint attackInstanceId)
        {
            if (attackInstanceId == 0u)
            {
                return;
            }

            BossTelegraphCue cancelled = default;
            bool removed = false;
            for (int i = _telegraphCues.Count - 1; i >= 0; --i)
            {
                if (_telegraphCues[i].AttackInstanceId != attackInstanceId)
                {
                    continue;
                }

                cancelled = _telegraphCues[i];
                cancelled.IsActive = false;
                _telegraphCues.RemoveAt(i);
                removed = true;
            }

            if (!removed)
            {
                return;
            }

            _telegraphCue = cancelled;
        }

        [Server]
        internal void ServerCancelTelegraph(uint attackInstanceId, byte repeatIndex,
            uint targetNetId)
        {
            int index = FindTelegraph(attackInstanceId, repeatIndex, targetNetId);
            if (index < 0)
            {
                return;
            }

            BossTelegraphCue cancelled = _telegraphCues[index];
            cancelled.IsActive = false;
            _telegraphCues.RemoveAt(index);
            if (_telegraphCues.Count == 0)
            {
                _telegraphCue = cancelled;
                return;
            }

            RefreshLegacyTelegraph();
        }

        /// <summary>
        /// 패턴 하나가 끝났다고 알린다. 무녀 표식 공명은 여기서만 판정한다 -
        /// 카운터 패턴이 끝나는 시각 기준으로 공명 창 안에 걸린 표식이 있으면 게이지를 깎는다
        /// 공격 하나당 한 번만 인정한다, 무녀가 둘이어도 두 번 깎이지 않는다
        ///
        /// 취소된 공격은 여기까지 오지 않는다. 패턴이 끝나야 카운터라는 계약이라
        /// 패링이나 대상 소실로 끊긴 공격에는 공명이 성립하지 않는다
        /// </summary>
        [Server]
        internal void ServerNotifyPatternEnded(int patternId, uint attackInstanceId)
        {
            if ((_serverStopped) || (attackInstanceId == 0u))
            {
                return;
            }

            if ((_souls != null) && (patternId == _bossData.FinalFailurePatternId) &&
                (_souls.SoulSnapshot.State == BossFinalState.FailureAttack))
            {
                _souls.ServerNotifyFailureAttackEnded();
            }

            if (attackInstanceId == _lastResonanceAttackId)
            {
                return;
            }

            if (!ServerIsCounterPattern(CharacterKind.Munyeo, patternId))
            {
                return;
            }

            TargetRef target = CreateBodyTargetRef();
            if (MarkRegistry.CollectOwnersMarkedWithin(target,
                    RunTimeLocator.Current.GameplayElapsedSeconds, _bossData.BrandWindow,
                    _resonanceOwnerBuffer) == 0)
            {
                return;
            }

            _lastResonanceAttackId = attackInstanceId;
            float appliedAmount = ServerReduceGroggyGauge(CharacterKind.Munyeo);
            for (int i = 0; i < _resonanceOwnerBuffer.Count; ++i)
            {
                RecordCounter(RunCounterKind.Resonance, _resonanceOwnerBuffer[i],
                    target, attackInstanceId, appliedAmount);
                if (appliedAmount > Constants.Combat.DAMAGE_EPSILON)
                {
                    ServerRefundSpirit(_resonanceOwnerBuffer[i], _bossData.ResonanceSpiritRefund);
                }
            }
        }

        /// <summary> 현재 신궁 카운터 예고를 실제 정밀 화살 명중으로 중단한다 </summary>
        [Server]
        public bool TryInterruptPrecisionShot(in PrecisionInterruptContext context)
        {
            if ((_serverStopped) || (_status == null) || (_status.IsDead)
                || (_bossData == null) || (_currentPhaseState == null)
                || (_groggyPhase != GroggyPhase.Active) || (!context.IsPrecision)
                || (context.ActionId == 0u) || (!context.Attacker.IsValid)
                || (double.IsNaN(context.HitServerTime))
                || (double.IsInfinity(context.HitServerTime)))
            {
                return false;
            }

            TargetRef currentTarget = CreateBodyTargetRef();
            if ((!currentTarget.IsValid) || (!context.Target.Equals(currentTarget))
                || (!_telegraphCue.IsActive)
                || (!_currentPhaseState.IsAttackWindup(_telegraphCue.AttackInstanceId))
                || (_telegraphCue.AttackInstanceId == _lastPrecisionInterruptedAttackId)
                || (!ServerIsCounterPattern(CharacterKind.Singung, _telegraphCue.PatternId)))
            {
                return false;
            }

            uint attackInstanceId = _telegraphCue.AttackInstanceId;
            _lastPrecisionInterruptedAttackId = attackInstanceId;
            _currentPhaseState.CancelActiveAttack();
            float appliedAmount = ServerReduceGroggyGauge(CharacterKind.Singung);
            RecordCounter(RunCounterKind.Precision, context.Attacker, currentTarget,
                attackInstanceId, appliedAmount);
            return true;
        }

        /// <summary> 패링 성공자의 안정 신원으로 실제 게이지 감소 사실을 기록한다 </summary>
        [Server]
        internal void ServerRecordParryCounter(uint parriedByNetId, uint attackInstanceId)
        {
            ParticipantId actor = ResolveParticipant(parriedByNetId);
            float appliedAmount = ServerReduceGroggyGauge(CharacterKind.Chasa);
            RecordCounter(RunCounterKind.Parry, actor, CreateBodyTargetRef(),
                attackInstanceId, appliedAmount);
        }

        /// <summary>
        /// 표식이 가리키는 본체 참조를 만든다. 런과 씬 세대를 함께 담아
        /// 이전 런에서 남은 표식이 지금 보스에 맞아 떨어지지 않게 한다
        /// </summary>
        [Server]
        private TargetRef CreateBodyTargetRef()
        {
            RunManager       run      = RunManager.ServerInstance;
            RunSceneDirector director = RunSceneDirector.ServerInstance;

            return TargetRef.Create(
                (run == null) ? default : run.RunId,
                (director == null) ? default : director.Revision,
                netId,
                TargetRef.BODY_PART_ID);
        }

        /// <summary>
        /// 카운터 1회가 그로기 게이지를 깎는다. 그로기, 면역 중에는 먹지 않는다
        /// 차사 패링, 신궁 정밀 중단, 무녀의 마지막 impact 공명이 같은 실제 감소 경로를 쓴다
        /// </summary>
        [Server]
        internal float ServerReduceGroggyGauge(CharacterKind character)
        {
            if ((_serverStopped) || (_status.IsDead) ||
                (_groggyPhase != GroggyPhase.Active) || HasEnteredFinalSequence())
            {
                return 0f;
            }

            if (!_bossData.TryGetGaugeReduction(character, out float amount))
            {
                return 0f;
            }

            float previousGauge = _groggyGauge;
            _groggyGauge = Mathf.Max(_groggyGauge - amount, 0f);
            float appliedAmount = previousGauge - _groggyGauge;
            if (_groggyGauge <= 0f)
            {
                EnterGroggy();
            }

            return appliedAmount;
        }

        [Server]
        internal void ServerApplyPatternDamage(IDamageable target, in DamageInfo damage)
        {
            if ((target == null) || target.IsDead)
            {
                return;
            }

            if (target is IBossSwallowTarget swallowTarget)
            {
                BossFinalState finalState = (_souls == null)
                    ? BossFinalState.Dormant
                    : _souls.SoulSnapshot.State;
                bool convertLethal = (finalState == BossFinalState.SoulWindow) ||
                    (finalState == BossFinalState.FailureAttack);
                swallowTarget.ServerApplyBossDamage(damage, new BossSwallowDamageContext(
                    convertLethal,
                    RunTimeLocator.Current.GameplayElapsedSeconds,
                    (_bossData == null) ? 0f : _bossData.SwallowDurationSeconds));
                return;
            }

            target.TakeDamage(damage);
        }

        /// <summary> 패턴 판정과 같은 프레임에 호출해 모든 클라이언트에 임팩트 VFX 신호를 보낸다(러프, 이슈 검증용) </summary>
        [Server]
        internal void ServerPlayPatternImpact(BossPatternSO.PatternTemplate template, Vector3 origin, Vector3 forward)
        {
            RpcPatternImpact(template, origin, forward);
        }

        /// <summary> 패턴 예고 시작과 같은 프레임에 호출해 모든 클라이언트에 예고 VFX 신호를 보낸다(러프, 이슈 검증용) </summary>
        [Server]
        internal void ServerPlayPatternTelegraphVfx(BossPatternSO.PatternTemplate template, Vector3 origin, Vector3 forward)
        {
            RpcPatternTelegraphVfx(template, origin, forward);
        }

        /// <summary> Combat의 실제 구조 성공 뒤 통계 사실을 한 번 발행한다 </summary>
        [Server]
        public bool ServerTryRescueSwallowed(uint targetNetId, ParticipantId rescuer,
            uint expectedGeneration)
        {
            if ((!rescuer.IsValid) || (targetNetId == 0u) || (expectedGeneration == 0u) ||
                (_bossData == null) ||
                (!NetworkServer.spawned.TryGetValue(targetNetId, out NetworkIdentity identity)) ||
                (identity == null) ||
                (!(identity.GetComponent<IBossSwallowTarget>() is IBossSwallowTarget target)) ||
                (!target.ServerTryRescueSwallowed(rescuer, expectedGeneration,
                    _bossData.SwallowRestoreHealthRatio)))
            {
                return false;
            }

            return true;
        }

        [Server]
        internal void ServerHandleSoulStateChanged(BossSoulSnapshot snapshot)
        {
            switch (snapshot.State)
            {
                case BossFinalState.SoulWindow:
                    _currentPhaseState?.CancelActiveAttack();
                    CommitPhaseSnapshot(BossEncounterStage.FinalSoulWindow);
                    break;
                case BossFinalState.FailureAttack:
                    CommitPhaseSnapshot(BossEncounterStage.FinalFailureAttack);
                    if ((_currentPhaseState == null) ||
                        (!_currentPhaseState.TryBeginForcedAttack(
                            _bossData.FinalFailurePatternId, snapshot.FailureBranchCount)))
                    {
                        Debug.LogError("[BossController] 백귀야행 강제 실행을 시작하지 못했다", this);
                    }
                    break;
                case BossFinalState.Killable:
                    _status.ServerSetInvulnerable(false);
                    _status.ServerSetHealthFloorRatio(0f);
                    ServerReleaseSwallowed();
                    CommitPhaseSnapshot(BossEncounterStage.Killable);
                    break;
            }
        }

        private void ServerExpireSwallowed(double gameplayTime)
        {
            IReadOnlyList<IBossSwallowTarget> targets = BossSwallowTargetRegistry.Targets;
            for (int i = targets.Count - 1; i >= 0; --i)
            {
                IBossSwallowTarget target = targets[i];
                if ((target != null) && target.IsSwallowed)
                {
                    target.ServerExpireSwallowed(gameplayTime);
                }
            }
        }

        private void ServerReleaseSwallowed()
        {
            IReadOnlyList<IBossSwallowTarget> targets = BossSwallowTargetRegistry.Targets;
            for (int i = targets.Count - 1; i >= 0; --i)
            {
                IBossSwallowTarget target = targets[i];
                if ((target != null) && target.IsSwallowed)
                {
                    target.ServerReleaseSwallowed(_bossData.SwallowRestoreHealthRatio);
                }
            }
        }

        private void ServerRefundSpirit(ParticipantId participant, float amount)
        {
            if ((!participant.IsValid) || float.IsNaN(amount) || float.IsInfinity(amount) ||
                (amount <= 0f))
            {
                return;
            }

            foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
            {
                if ((identity == null) ||
                    (!(identity.GetComponent<IGrowthParticipant>() is IGrowthParticipant growth)) ||
                    (growth.Participant != participant) ||
                    (!(identity.GetComponent<IResourceTransactionTarget>() is IResourceTransactionTarget target)))
                {
                    continue;
                }

                bool acquired = false;
                try
                {
                    if ((target.ServerPrepareRecovery(RecoveryResourceKind.Spirit, participant,
                            amount, out ResourceCommitPlan plan) != ResourceTransactionResult.Success) ||
                        (!target.ServerTryAcquireResourceTransaction(plan)))
                    {
                        return;
                    }

                    acquired = true;
                    target.ServerApplyResourceTransaction(plan);
                    target.ServerPublishResourceTransaction(plan);
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[BossController] spirit refund failed: {exception}", this);
                }
                finally
                {
                    if (acquired)
                    {
                        try
                        {
                            target.ServerReleaseResourceTransaction();
                        }
                        catch (Exception exception)
                        {
                            Debug.LogError($"[BossController] spirit refund release failed: {exception}", this);
                        }
                    }
                }

                return;
            }
        }

        private void ServerClearTelegraphs()
        {
            if (_telegraphCues.Count > 0)
            {
                _telegraphCues.Clear();
            }

            BossTelegraphCue cue = _telegraphCue;
            cue.IsActive = false;
            _telegraphCue = cue;
        }

        // 그로기 동안 이전 공격이 멈췄다가 되살아나지 않게 진입 시 정리한다
        [Server]
        private void EnterGroggy()
        {
            if (HasEnteredFinalSequence())
            {
                return;
            }

            _groggyPhase      = GroggyPhase.Groggy;
            _groggyStateTimer = 0f;
            ++_groggyGeneration;
            if (_groggyGeneration == 0u)
            {
                ++_groggyGeneration;
            }

            _currentPhaseState?.CancelActiveAttack();
            if (_currentPhaseIndex >= 2)
            {
                _parts?.ServerBeginGroggy(_groggyGeneration);
            }
            _status.ServerSetBodyDamageMultiplier(_bossData.GroggyBodyDamageMultiplier);
            CommitPhaseSnapshot(BossEncounterStage.Groggy);
            RpcGroggyEnter();
        }

        private void HandleRunPhaseChanged(RunPhaseChangedEvent evt)
        {
            if (evt.Current != RunPhase.Boss)
            {
                StopServerExecution(terminate: true);
            }
        }

        private void HandleRunReset(RunResetEvent evt)
        {
            StopServerExecution(terminate: true);
        }

        private void RecordCounter(RunCounterKind kind, ParticipantId actor, TargetRef target,
            uint attackInstanceId, float appliedAmount)
        {
            RunManager run = RunManager.ServerInstance;
            if ((run == null) || (!actor.IsValid) ||
                (appliedAmount <= Constants.Combat.DAMAGE_EPSILON))
            {
                return;
            }

            try
            {
                RunStatisticsSinkLocator.Current.RecordCounter(new RunCounterFact(
                    run.RunId, kind, actor, target, attackInstanceId, appliedAmount));
            }
            catch (Exception exception)
            {
                Debug.LogError($"[BossController] counter fact producer failed: {exception}", this);
            }
        }

        private void CommitPhaseSnapshot(BossEncounterStage stage)
        {
            BossPhaseSnapshot next = _phaseSnapshot;
            bool phaseChanged = (next.PhaseIndex != _currentPhaseIndex);
            next.PhaseIndex = _currentPhaseIndex;
            next.Stage = stage;
            ++next.Revision;
            if (next.Revision == 0u)
            {
                next.Revision = 1u;
            }

            next.ChangedServerTime = NetworkTime.time;
            if (phaseChanged)
            {
                next.PhaseChangedGameplayTime =
                    RunTimeLocator.Current.GameplayElapsedSeconds;
            }

            _phaseSnapshot = next;

            // Host는 SyncVar setter가 hook을 부른다. 전용 서버에는 같은 callback이 없으므로
            // 서버 표현 소비자에게 확정 snapshot을 직접 한 번 알린다
            if (!NetworkServer.activeHost)
            {
                NotifyPhaseChanged(next);
            }
        }

        private int FindTelegraph(uint attackInstanceId, byte repeatIndex, uint targetNetId)
        {
            for (int i = 0; i < _telegraphCues.Count; ++i)
            {
                BossTelegraphCue cue = _telegraphCues[i];
                if ((cue.AttackInstanceId == attackInstanceId) &&
                    (cue.RepeatIndex == repeatIndex) &&
                    (cue.TargetNetId == targetNetId))
                {
                    return i;
                }
            }

            return -1;
        }

        private void RefreshLegacyTelegraph()
        {
            if (_telegraphCues.Count == 0)
            {
                _telegraphCue = default;
                return;
            }

            BossTelegraphCue earliest = _telegraphCues[0];
            for (int i = 1; i < _telegraphCues.Count; ++i)
            {
                if (_telegraphCues[i].ImpactServerTime < earliest.ImpactServerTime)
                {
                    earliest = _telegraphCues[i];
                }
            }

            _telegraphCue = earliest;
        }

        private static ParticipantId ResolveParticipant(uint playerNetId)
        {
            if ((playerNetId == 0u) ||
                (!NetworkServer.spawned.TryGetValue(playerNetId, out NetworkIdentity identity)) ||
                (identity == null))
            {
                return ParticipantId.None;
            }

            IGrowthParticipant participant = identity.GetComponent<IGrowthParticipant>();
            return (participant == null) ? ParticipantId.None : participant.Participant;
        }

        private void ExitGroggyForFinal()
        {
            _groggyPhase = GroggyPhase.Active;
            _groggyStateTimer = 0f;
            _groggyGauge = _bossData.GroggyGaugeMax;
            _status.ServerSetBodyDamageMultiplier(1f);
        }

        private bool HasEnteredFinalSequence()
        {
            return (_souls != null) &&
                (_souls.SoulSnapshot.State != BossFinalState.Dormant);
        }
    }
}
