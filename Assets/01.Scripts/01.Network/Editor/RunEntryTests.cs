// System
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Network.EditorTests
{
    /// <summary> 정식 보스 입구의 경계, 정지 보존, 전실 경유 규칙 </summary>
    public class RunEntryTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const BindingFlags PRIVATE_INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PUBLIC_STATIC    = BindingFlags.Static | BindingFlags.Public;

        private bool                    _wasServerActive;
        private LocalConnectionToClient _previousLocalConnection;

        private GameObject       _runObject;
        private GameObject       _directorObject;
        private GameObject       _networkManagerObject;
        private GameObject       _eventBusObject;
        private readonly List<GameObject> _participantObjects = new List<GameObject>();
        private RunManager       _run;
        private RunSceneDirector _director;
        private RunManager       _previousRun;
        private RunSceneDirector _previousDirector;
        private NetworkManager   _previousNetworkManager;
        private Dictionary<int, NetworkConnectionToClient> _previousConnections;
        private GeumeumAuthenticator _authenticator;
        private GameDatabaseSO   _database;
        private BarrierPhaseSO   _barrier;
        private BossPatternSO    _boss;
        private RunEntryResultFactsStub _resultFacts;
        private RunEntryScoreStub _score;

        /****************************************
        *               Test Lifetime
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _wasServerActive         = NetworkServer.active;
            _previousLocalConnection = NetworkServer.localConnection;
            _previousConnections = new Dictionary<int, NetworkConnectionToClient>(NetworkServer.connections);
            NetworkServer.connections.Clear();
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                .SetValue(null, true);

            _barrier = ScriptableObject.CreateInstance<BarrierPhaseSO>();
            Set(_barrier, "_fieldTimeLimitSeconds", 1800f);
            Set(_barrier, "_bossEntryCountdownSeconds", 30f);

            _boss = ScriptableObject.CreateInstance<BossPatternSO>();
            Set(_boss, "_bossTimeLimitSeconds", 840f);

            _database = ScriptableObject.CreateInstance<GameDatabaseSO>();
            Set(_database, "_barrier", _barrier);
            Set(_database, "_boss", _boss);
            GameDB.Initialize(_database);

            _runObject = new GameObject("RunEntryTestRun", typeof(NetworkIdentity), typeof(RunManager));
            _run = _runObject.GetComponent<RunManager>();
            Set(_run, "_phase", RunPhase.Shrink3);
            Set(_run, "_runIdValue", 1u);
            Set(_run, "_timeline", new RunPhaseTimeline(null));
            _previousRun = RunManager.ServerInstance;
            SetStaticProperty(typeof(RunManager), nameof(RunManager.ServerInstance), _run);

            RunResultFactsProviderLocator.ResetStatic();
            _resultFacts = new RunEntryResultFactsStub();
            RunResultFactsProviderLocator.Register(_resultFacts);
            RunResultProviderLocator.ResetStatic();
            _score = new RunEntryScoreStub();
            RunResultProviderLocator.Register(_score);

            _directorObject = new GameObject(
                "RunEntryTestDirector", typeof(NetworkIdentity), typeof(RunSceneDirector));
            _director = _directorObject.GetComponent<RunSceneDirector>();
            _previousDirector = RunSceneDirector.ServerInstance;
            SetStaticProperty(typeof(RunSceneDirector), nameof(RunSceneDirector.ServerInstance), _director);

            _previousNetworkManager = NetworkManager.singleton;
            _networkManagerObject = new GameObject("RunEntryTestNetworkManager");
            _networkManagerObject.SetActive(false);
            NetworkManager networkManager = _networkManagerObject.AddComponent<NetworkManager>();
            _authenticator = _networkManagerObject.AddComponent<GeumeumAuthenticator>();
            networkManager.authenticator = _authenticator;
            SetStaticProperty(typeof(NetworkManager), nameof(NetworkManager.singleton), networkManager);

            // 다른 EditMode 테스트가 OnApplicationQuit를 거친 뒤 남긴 플래그까지 지우고 실제 버스를 둔다.
            GameEventBus.ResetStatic();
            GameEventBus sceneBus = Object.FindAnyObjectByType<GameEventBus>(FindObjectsInactive.Exclude);
            if (sceneBus == null)
            {
                _eventBusObject = new GameObject("RunEntryTestEventBus", typeof(GameEventBus));
            }

            Assert.IsNotNull(GameEventBus.Instance);
            Assert.IsTrue(GameEventBus.HasInstance);
        }

        [TearDown]
        public void TearDown()
        {
            StopDirectorRoutine();
            SetStaticProperty(typeof(RunSceneDirector), nameof(RunSceneDirector.ServerInstance), _previousDirector);
            SetStaticProperty(typeof(RunManager), nameof(RunManager.ServerInstance), _previousRun);
            SetStaticProperty(typeof(NetworkManager), nameof(NetworkManager.singleton), _previousNetworkManager);

            if (_eventBusObject != null)
            {
                Object.DestroyImmediate(_eventBusObject);
            }
            GameEventBus.ResetStatic();

            for (int i = 0; i < _participantObjects.Count; ++i)
            {
                if (_participantObjects[i] != null)
                {
                    Object.DestroyImmediate(_participantObjects[i]);
                }
            }
            _participantObjects.Clear();

            if (_networkManagerObject != null)
            {
                Object.DestroyImmediate(_networkManagerObject);
            }

            if (_directorObject != null)
            {
                Object.DestroyImmediate(_directorObject);
            }

            if (_runObject != null)
            {
                Object.DestroyImmediate(_runObject);
            }

            GameDB.ResetStatic();
            RunParticipantRegistry.ServerClear();
            RunResultFactsProviderLocator.Unregister(_resultFacts);
            RunResultFactsProviderLocator.ResetStatic();
            RunResultProviderLocator.Unregister(_score);
            RunResultProviderLocator.ResetStatic();
            Object.DestroyImmediate(_database);
            Object.DestroyImmediate(_barrier);
            Object.DestroyImmediate(_boss);

            NetworkServer.connections.Clear();
            foreach (KeyValuePair<int, NetworkConnectionToClient> pair in _previousConnections)
            {
                NetworkServer.connections[pair.Key] = pair.Value;
            }

            SetStaticProperty(typeof(NetworkServer), nameof(NetworkServer.localConnection),
                _previousLocalConnection);
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active))
                .SetValue(null, _wasServerActive);
        }

        /****************************************
        *                 Tests
        ****************************************/

        [Test]
        public void FirstEntryStartsThirtySecondCountdown()
        {
            bool started = BossEntranceRules.TryGetDeadline(
                BossEntranceState.Idle, 900f, 30f, 1800f, out float deadline);

            Assert.IsTrue(started);
            Assert.AreEqual(930f, deadline);
        }

        [Test]
        public void EntryNearFieldLimitUsesFieldLimitAsDeadline()
        {
            BossEntranceRules.TryGetDeadline(
                BossEntranceState.Idle, 1795f, 30f, 1800f, out float deadline);

            Assert.AreEqual(1800f, deadline);
        }

        [Test]
        public void ActualFieldTickStartsOnceAndPauseKeepsRemainingTime()
        {
            Set(_director, "_currentScene", RunSceneId.Field);
            Set(_run, "_runStartServerTime", NetworkTime.time - 1770.0);

            Invoke(_run, "ServerTickFieldClock", 1800f);

            Assert.AreEqual(BossEntranceState.CountingDown, _run.EntranceState);
            Assert.AreEqual(1800f, _run.BossEntranceDeadlineFieldSeconds, 0.001f);

            bool duplicateStarted = (bool) Invoke(_run, "ServerStartBossEntranceCountdown",
                1780f, 1800f, 30f, "중복 시험");
            Assert.IsFalse(duplicateStarted);
            Assert.AreEqual(1800f, _run.BossEntranceDeadlineFieldSeconds, 0.001f);

            Set(_run, "_elapsedSeconds", 1780f);
            float beforePause = _run.BossEntranceCountdownRemainingSeconds;
            Assert.IsTrue(_run.PauseRun("시험 이탈"));

            // 20초를 멈춰 있었고, 보정 전 시계라면 deadline을 넘는 상황을 만든다.
            Set(_run, "_pauseStartServerTime", NetworkTime.time - 20.0);
            Set(_run, "_runStartServerTime", NetworkTime.time - 1800.0);

            Assert.IsTrue(_run.IsPaused);
            Assert.AreEqual(BossEntranceState.CountingDown, _run.EntranceState);
            Assert.AreEqual(beforePause, _run.BossEntranceCountdownRemainingSeconds, 0.001f);

            Assert.IsTrue(_run.ResumeRun());
            Invoke(_run, "ServerTickFieldClock", 1800f);

            float afterResume = _run.BossEntranceCountdownRemainingSeconds;
            Assert.IsFalse(_run.IsPaused);
            Assert.AreEqual(beforePause, afterResume, 0.1f);
            Assert.AreEqual(1800f, _run.BossEntranceDeadlineFieldSeconds, 0.001f);

            // 재개 뒤 다음 실제 field tick에서는 다시 시간이 흐른다.
            Set(_run, "_runStartServerTime", (double) Get(_run, "_runStartServerTime") - 1.0);
            Invoke(_run, "ServerTickFieldClock", 1800f);

            Assert.Less(_run.BossEntranceCountdownRemainingSeconds, afterResume - 0.9f);
            Assert.AreEqual(BossEntranceState.CountingDown, _run.EntranceState);
        }

        [Test]
        public void TwoCompletedTransitionsRequestEntranceAndArenaOnlyOnce()
        {
            Set(_run, "_bossEntranceState", BossEntranceState.TransitionRequested);
            Set(_director, "_currentScene", RunSceneId.Field);
            Set(_director, "_state", RunTransitionState.Idle);

            _run.ServerHandleSceneTransitionReady();

            Assert.AreEqual(BossEntranceState.Entering, _run.EntranceState);
            Assert.AreEqual(1u, _director.Revision.Value);
            Assert.AreEqual(RunSceneId.BossEntrance, GetTransition().ToScene);

            SimulateCompletedTransition(RunSceneId.BossEntrance);
            _run.ServerHandleSceneTransitionReady();

            Assert.AreEqual(2u, _director.Revision.Value);
            Assert.AreEqual(RunSceneId.Boss, GetTransition().ToScene);

            _run.ServerHandleSceneTransitionReady();
            Assert.AreEqual(2u, _director.Revision.Value,
                "같은 완료 콜백이 겹쳐도 아레나 전환 revision은 한 번만 증가해야 한다");

            SimulateCompletedTransition(RunSceneId.Boss);
            Assert.IsFalse((bool) Get(_run, "_hasBossClockStarted"));

            _run.ServerHandleSceneTransitionReady();

            Assert.AreEqual(BossEntranceState.Ready, _run.EntranceState);
            Assert.AreEqual(RunPhase.Boss, _run.Phase);
            Assert.IsFalse((bool) Get(_run, "_hasBossClockStarted"),
                "아레나 준비 완료 콜백 자체가 보스 시계를 미리 진행시키면 안 된다");

            Invoke(_run, "ServerTickBossClock", 840f);
            Assert.IsTrue((bool) Get(_run, "_hasBossClockStarted"));
        }

        [Test]
        public void CountdownExpiresOnlyAtDeadline()
        {
            Assert.IsFalse(BossEntranceRules.HasExpired(
                BossEntranceState.CountingDown, 929.99f, 930f));
            Assert.IsTrue(BossEntranceRules.HasExpired(
                BossEntranceState.CountingDown, 930f, 930f));
            Assert.IsFalse(BossEntranceRules.HasExpired(
                BossEntranceState.TransitionRequested, 940f, 930f));
        }

        [Test]
        public void RouteUsesEntranceStateInsteadOfBossPhase()
        {
            bool entersAntechamber = RunSceneCatalog.TryGetSceneForBossEntrance(
                BossEntranceState.TransitionRequested, RunSceneId.Field, out RunSceneId first);
            bool entersArena = RunSceneCatalog.TryGetSceneForBossEntrance(
                BossEntranceState.Entering, RunSceneId.BossEntrance, out RunSceneId second);

            Assert.IsTrue(entersAntechamber);
            Assert.AreEqual(RunSceneId.BossEntrance, first);
            Assert.IsTrue(entersArena);
            Assert.AreEqual(RunSceneId.Boss, second);
        }

        [TestCase(PlayerLifeSnapshotState.Alive, true)]
        [TestCase(PlayerLifeSnapshotState.ReviveInvulnerable, true)]
        [TestCase(PlayerLifeSnapshotState.Downed, false)]
        [TestCase(PlayerLifeSnapshotState.Spectating, false)]
        [TestCase(PlayerLifeSnapshotState.Swallowed, false)]
        public void OnlyLivingFieldParticipantCanEnter(PlayerLifeSnapshotState lifeState, bool expected)
        {
            Assert.AreEqual(expected, BossEntranceRules.CanEnter(RunPhase.Shrink2, lifeState, 10f));
            Assert.IsFalse(BossEntranceRules.CanEnter(RunPhase.Boss, lifeState, 10f));
            Assert.IsFalse(BossEntranceRules.CanEnter(RunPhase.Shrink2, lifeState, 0f));
        }

        [Test]
        public void OnlyHostCanAbandonActualParticipantWait()
        {
            Assert.IsTrue(BossEntranceRules.CanAbandonParticipantWait(
                true, RunPhase.Shrink3, RunPauseReason.ParticipantMissing, false));
            Assert.IsFalse(BossEntranceRules.CanAbandonParticipantWait(
                false, RunPhase.Shrink3, RunPauseReason.ParticipantMissing, false));
            Assert.IsFalse(BossEntranceRules.CanAbandonParticipantWait(
                true, RunPhase.Shrink3, RunPauseReason.SceneTransition, false));
            Assert.IsFalse(BossEntranceRules.CanAbandonParticipantWait(
                true, RunPhase.Result, RunPauseReason.ParticipantMissing, false));
            Assert.IsTrue(BossEntranceRules.CanAbandonParticipantWait(
                true, RunPhase.Shrink3, RunPauseReason.SceneTransition, true));
            Assert.IsTrue(BossEntranceRules.CanAbandonParticipantWait(
                true, RunPhase.Result, RunPauseReason.None, true));
            Assert.IsTrue(BossEntranceRules.CanAbandonParticipantWait(
                true, RunPhase.Lobby, RunPauseReason.None, true));
        }

        [Test]
        public void ActualServerPathRejectsRemoteAndCommitsHostAbandonWhilePaused()
        {
            LocalConnectionToClient host = new LocalConnectionToClient();
            NetworkConnectionToClient remote = new NetworkConnectionToClient(7);
            SetStaticProperty(typeof(NetworkServer), nameof(NetworkServer.localConnection), host);
            Set(_run, "_pauseReasons", RunPauseReason.ParticipantMissing);

            bool remoteAccepted = (bool) Invoke(
                _run, "ServerTryAbandonParticipantWait", remote);

            Assert.IsFalse(remoteAccepted);
            Assert.AreEqual(RunPhase.Shrink3, _run.Phase);
            Assert.AreEqual(RunOutcome.None, _run.Outcome);

            bool hostAccepted = (bool) Invoke(
                _run, "ServerTryAbandonParticipantWait", host);

            Assert.IsTrue(hostAccepted);
            Assert.AreEqual(RunPhase.Result, _run.Phase);
            Assert.AreEqual(RunOutcome.PlayerLeft, _run.Outcome);
        }

        [Test]
        public void CaptureFailureStopsTransitionAndNamesFaultedConnection()
        {
            RunEntryLifecycleStub lifecycle = AddParticipant(
                new NetworkConnectionToClient(11), 111UL, true);
            lifecycle.CaptureSucceeds = false;
            PrepareParticipantTransition(11);

            LogAssert.Expect(LogType.Error,
                "[RunSceneDirector] 참가자 전환 실패(연결 11): 런 상태를 저장하지 못했습니다");
            bool captured = (bool) Invoke(_director, "ServerCaptureParticipants");

            Assert.IsFalse(captured);
            Assert.AreEqual(RunTransitionState.Failed, _director.State);
            StringAssert.Contains("연결 11", _director.FailureMessage);
            Assert.IsTrue(GetSet<int>(_director, "_faultedConnections").Contains(11));
            Assert.AreEqual(0, RunParticipantRegistry.Count);
        }

        [Test]
        public void RestoreFailureStopsTransitionBeforeBindingCanCommit()
        {
            RunEntryLifecycleStub lifecycle = AddParticipant(
                new NetworkConnectionToClient(12), 112UL, true);
            PrepareParticipantTransition(12);

            Assert.IsTrue((bool) Invoke(_director, "ServerCaptureParticipants"));
            lifecycle.RestoreSucceeds = false;

            LogAssert.Expect(LogType.Error,
                "[RunSceneDirector] 참가자 전환 실패(연결 12): 참가자 상태를 복구하지 못했습니다");
            bool restored = (bool) Invoke(_director, "ServerRestoreParticipants");

            Assert.IsFalse(restored);
            Assert.AreEqual(1, lifecycle.RestoreCount);
            Assert.AreEqual(RunTransitionState.Failed, _director.State);
            Assert.AreEqual(RunPauseReason.None, _run.PauseReasons,
                "직접 호출 fixture에서는 정지를 걸지 않았으며 실패 경로가 임의로 재개해서도 안 된다");
        }

        [Test]
        public void RestoreAcceptsReconnectWithSameAuthenticatedParticipant()
        {
            NetworkConnectionToClient original = new NetworkConnectionToClient(13);
            AddParticipant(original, 113UL, true);
            PrepareParticipantTransition(13);

            Assert.IsTrue((bool) Invoke(_director, "ServerCaptureParticipants"));

            NetworkServer.connections.Remove(13);
            AcceptedIds().Remove(13);
            RunEntryLifecycleStub reconnected = AddParticipant(
                new NetworkConnectionToClient(23), 113UL, true);

            bool restored = (bool) Invoke(_director, "ServerRestoreParticipants");

            Assert.IsTrue(restored);
            Assert.AreEqual(1, reconnected.RestoreCount);
            Assert.AreNotEqual(RunTransitionState.Failed, _director.State);
            Assert.AreEqual(0, RunParticipantRegistry.Count);
        }

        [Test]
        public void ArrivalTimeoutLetsOnlyHostUseTerminalResultExit()
        {
            LocalConnectionToClient host = new LocalConnectionToClient();
            TrackingConnection remote = new TrackingConnection(27);
            SetStaticProperty(typeof(NetworkServer), nameof(NetworkServer.localConnection), host);

            AddParticipant(host, 201UL, true);
            AddParticipant(remote, 202UL, false);
            Set(_director, "_transition", SceneTransitionSnapshot.Create(
                new RunId(1u), new SceneRevision(1u), RunSceneId.Field, RunSceneId.BossEntrance, true));
            GetSet<int>(_director, "_expectedConnections").Add(host.connectionId);
            GetSet<int>(_director, "_expectedConnections").Add(remote.connectionId);
            GetDictionary<int, ParticipantId>(_director, "_participants")[host.connectionId] =
                new ParticipantId(201UL);
            GetDictionary<int, ParticipantId>(_director, "_participants")[remote.connectionId] =
                new ParticipantId(202UL);
            Set(_director, "_state", RunTransitionState.WaitingForParticipant);
            Set(_director, "_currentScene", RunSceneId.BossEntrance);
            Set(_run, "_pauseReasons", RunPauseReason.SceneTransition);

            LogAssert.Expect(LogType.Error,
                "[RunSceneDirector] 참가자 도착을 30초 안에 확인하지 못했습니다");
            Invoke(_director, "ServerFailArrivalTimeout");

            Assert.AreEqual(RunTransitionState.Failed, _director.State);
            Assert.IsFalse(remote.WasDisconnected);

            bool remoteAccepted = (bool) Invoke(
                _run, "ServerTryAbandonParticipantWait", remote);
            Assert.IsFalse(remoteAccepted);
            Assert.AreEqual(RunPhase.Shrink3, _run.Phase);
            Assert.IsFalse(remote.WasDisconnected);

            bool hostAccepted = (bool) Invoke(
                _run, "ServerTryAbandonParticipantWait", host);

            Assert.IsTrue(hostAccepted);
            Assert.IsTrue(remote.WasDisconnected);
            Assert.AreEqual(RunPhase.Result, _run.Phase);
            Assert.AreEqual(RunOutcome.PlayerLeft, _run.Outcome);
            Assert.AreEqual(RunSceneId.Result, GetTransition().ToScene);
            Assert.IsTrue((bool) Get(_director, "_isTerminalTransition"),
                "Result 예외 전환은 같은 ready 대기를 다시 사용하면 안 된다");
        }

        [Test]
        public void ResultReturnCountdownStartsOnlyAfterResultSceneIsReady()
        {
            Set(_director, "_currentScene", RunSceneId.Boss);
            Set(_director, "_state", RunTransitionState.Idle);

            Invoke(_run, "ServerCommitOutcome", RunOutcome.Victory);

            Assert.AreEqual(RunPhase.Result, _run.Phase);
            Assert.AreEqual(RunOutcome.Victory, _run.Outcome);
            Assert.IsNull(Get(_run, "_returnRoutine"),
                "Result 전환 도착이 5초보다 느려도 Lobby 타이머가 먼저 시작하면 안 된다");

            LogAssert.Expect(LogType.Warning,
                "[RunManager] Result 씬 준비 전에는 Lobby phase를 확정하지 않는다: " +
                "scene=Boss, transition=Freezing");
            _run.ServerReturnToLobby();

            Assert.AreEqual(RunPhase.Result, _run.Phase);
            Assert.AreEqual(RunOutcome.Victory, _run.Outcome);

            SimulateCompletedTransition(RunSceneId.Result);
            _run.ServerHandleSceneTransitionReady();

            Assert.IsNotNull(Get(_run, "_returnRoutine"));
            Assert.AreEqual(RunPhase.Result, _run.Phase);
        }

        [Test]
        public void FailedRunKeepsScoreButDoesNotReceiveClearGrade()
        {
            RunResultSnapshot failure = (RunResultSnapshot) Invoke(
                _run, "BuildResultSnapshot", RunOutcome.TimeExpired);
            RunResultSnapshot victory = (RunResultSnapshot) Invoke(
                _run, "BuildResultSnapshot", RunOutcome.Victory);

            Assert.AreEqual(12345, failure.Score);
            Assert.AreEqual(RunEvaluationGrade.None, failure.EvaluationGrade);
            Assert.AreEqual("제한 시간이 끝나 이번 런을 마치지 못했다.",
                failure.EvaluationMessage);
            Assert.AreEqual(RunEvaluationGrade.S, victory.EvaluationGrade);
            Assert.AreEqual("승리 평가", victory.EvaluationMessage);
        }

        [Test]
        public void FailedResultTransitionLetsOnlyHostRetryWithoutChangingOutcome()
        {
            LocalConnectionToClient host = new LocalConnectionToClient();
            TrackingConnection remote = new TrackingConnection(31);
            SetStaticProperty(typeof(NetworkServer), nameof(NetworkServer.localConnection), host);
            NetworkServer.connections[remote.connectionId] = remote;

            Set(_run, "_phase", RunPhase.Result);
            Set(_run, "_outcome", RunOutcome.Victory);
            Set(_director, "_currentScene", RunSceneId.Boss);
            Set(_director, "_state", RunTransitionState.Failed);
            Set(_director, "_transition", SceneTransitionSnapshot.Create(
                new RunId(1u), new SceneRevision(2u), RunSceneId.Boss, RunSceneId.Result, false));
            GetSet<int>(_director, "_faultedConnections").Add(remote.connectionId);

            Assert.IsFalse((bool) Invoke(
                _run, "ServerTryAbandonParticipantWait", remote));
            Assert.IsFalse(remote.WasDisconnected);

            Assert.IsTrue((bool) Invoke(
                _run, "ServerTryAbandonParticipantWait", host));

            Assert.IsTrue(remote.WasDisconnected);
            Assert.AreEqual(RunPhase.Result, _run.Phase);
            Assert.AreEqual(RunOutcome.Victory, _run.Outcome);
            Assert.AreEqual(RunSceneId.Result, GetTransition().ToScene);
            Assert.IsTrue((bool) Get(_director, "_isTerminalTransition"));
            Assert.IsNull(Get(_run, "_returnRoutine"));
        }

        [Test]
        public void FailedLobbyTransitionBlocksNextRunAndLetsHostEndSession()
        {
            LocalConnectionToClient host = new LocalConnectionToClient();
            NetworkConnectionToClient remote = new NetworkConnectionToClient(32);
            SetStaticProperty(typeof(NetworkServer), nameof(NetworkServer.localConnection), host);

            Set(_run, "_phase", RunPhase.Lobby);
            Set(_run, "_outcome", RunOutcome.Victory);
            Set(_director, "_currentScene", RunSceneId.Result);
            Set(_director, "_state", RunTransitionState.Failed);
            Set(_director, "_transition", SceneTransitionSnapshot.Create(
                new RunId(1u), new SceneRevision(3u), RunSceneId.Result, RunSceneId.Lobby, false));

            LogAssert.Expect(LogType.Warning,
                "[RunManager] 로비 씬 준비가 끝나지 않아 런을 시작할 수 없다: " +
                "scene=Result, transition=Failed");
            Assert.IsFalse(_run.StartRun());
            Assert.AreEqual(RunOutcome.Victory, _run.Outcome);

            Assert.IsFalse((bool) Invoke(
                _run, "ServerTryAbandonParticipantWait", remote));
            Assert.IsTrue((bool) Invoke(
                _run, "ServerTryAbandonParticipantWait", host));

            Assert.AreEqual(RunTransitionState.Failed, _director.State,
                "세션이 닫히기 전에 실패를 성공으로 위장하면 안 된다");
            Assert.IsNotNull(Get(_director, "_routine"));
            Assert.AreEqual(RunOutcome.Victory, _run.Outcome);
        }

        /****************************************
        *               Test Helpers
        ****************************************/

        private SceneTransitionSnapshot GetTransition()
        {
            return (SceneTransitionSnapshot) Get(_director, "_transition");
        }

        private void SimulateCompletedTransition(RunSceneId sceneId)
        {
            StopDirectorRoutine();
            Set(_director, "_currentScene", sceneId);
            Set(_director, "_state", RunTransitionState.Idle);
            Set(_run, "_pauseReasons", RunPauseReason.None);
            Set(_run, "_pauseStartServerTime", 0.0);
        }

        private void StopDirectorRoutine()
        {
            if ((_director == null) || (Get(_director, "_routine") is not Coroutine routine))
            {
                return;
            }

            _director.StopCoroutine(routine);
            Set(_director, "_routine", null);
        }

        private RunEntryLifecycleStub AddParticipant(NetworkConnectionToClient connection,
            ulong platformId, bool isReady)
        {
            GameObject playerObject = new GameObject(
                $"RunEntryParticipant{connection.connectionId}",
                typeof(NetworkIdentity), typeof(RunEntryLifecycleStub));
            _participantObjects.Add(playerObject);

            NetworkIdentity identity = playerObject.GetComponent<NetworkIdentity>();
            typeof(NetworkConnection).GetProperty(
                    nameof(NetworkConnection.identity), BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic)
                .SetValue(connection, identity);
            connection.isReady = isReady;
            NetworkServer.connections[connection.connectionId] = connection;
            AcceptedIds()[connection.connectionId] = new PlayerPlatformId(platformId);
            return playerObject.GetComponent<RunEntryLifecycleStub>();
        }

        private void PrepareParticipantTransition(int connectionId)
        {
            Set(_director, "_transition", SceneTransitionSnapshot.Create(
                new RunId(1u), new SceneRevision(1u), RunSceneId.Field, RunSceneId.BossEntrance, true));
            GetSet<int>(_director, "_expectedConnections").Add(connectionId);
        }

        private Dictionary<int, PlayerPlatformId> AcceptedIds()
        {
            return (Dictionary<int, PlayerPlatformId>) Get(_authenticator, "_acceptedIds");
        }

        private static HashSet<T> GetSet<T>(object target, string fieldName)
        {
            return (HashSet<T>) Get(target, fieldName);
        }

        private static Dictionary<TKey, TValue> GetDictionary<TKey, TValue>(
            object target, string fieldName)
        {
            return (Dictionary<TKey, TValue>) Get(target, fieldName);
        }

        private static object Get(object target, string fieldName)
        {
            return target.GetType().GetField(fieldName, PRIVATE_INSTANCE).GetValue(target);
        }

        private static void Set(object target, string fieldName, object value)
        {
            target.GetType().GetField(fieldName, PRIVATE_INSTANCE).SetValue(target, value);
        }

        private static object Invoke(object target, string methodName, params object[] arguments)
        {
            return target.GetType().GetMethod(methodName, PRIVATE_INSTANCE)
                .Invoke(target, arguments);
        }

        private static void SetStaticProperty(System.Type type, string propertyName, object value)
        {
            type.GetProperty(propertyName, PUBLIC_STATIC).SetValue(null, value);
        }
    }

    /// <summary> 씬 전환 fixture가 결과 동결 계약만 충족하도록 하는 권위 사실 stub </summary>
    internal sealed class RunEntryResultFactsStub : IRunResultFactsProvider
    {
        public bool BeginRun(RunId run) => run.IsValid;

        public bool TryFreeze(ref RunResultSnapshot snapshot)
        {
            if (snapshot.RunIdValue == 0u)
            {
                return false;
            }

            snapshot.ResultRevision = 1u;
            snapshot.FactsState     = RunResultFactsState.Authoritative;
            snapshot.Participants  = System.Array.Empty<RunParticipantResultSnapshot>();
            return true;
        }

        public void ResetRun(RunId run)
        {
        }
    }

    internal sealed class RunEntryScoreStub : IRunResultProvider
    {
        public int ScoreRuleVersion => 2;

        public int CalculateScore(in RunResultSnapshot facts) => 12345;

        public RunEvaluationGrade CalculateGrade(int score) => RunEvaluationGrade.S;

        public string GetEvaluationMessage(RunEvaluationGrade grade) => "승리 평가";
    }

    public class RunEntryLifecycleStub : MonoBehaviour, IPlayerRunLifecycle
    {
        public bool CaptureSucceeds { get; set; } = true;
        public bool RestoreSucceeds { get; set; } = true;
        public int  CaptureCount    { get; private set; }
        public int  RestoreCount    { get; private set; }

        public Vector3 Position => transform.position;

        public void Teleport(Vector3 position)
        {
            transform.position = position;
        }

        public void ResetForNewRun()
        {
        }

        public bool CaptureRunState(RunParticipantSnapshot snapshot)
        {
            ++CaptureCount;
            snapshot.Health = 10f;
            return CaptureSucceeds;
        }

        public bool RestoreRunState(RunParticipantSnapshot snapshot)
        {
            ++RestoreCount;
            return RestoreSucceeds;
        }
    }

    public class TrackingConnection : NetworkConnectionToClient
    {
        public bool WasDisconnected { get; private set; }

        public TrackingConnection(int connectionId) : base(connectionId)
        {
        }

        public override void Disconnect()
        {
            WasDisconnected = true;
            isReady          = false;
        }
    }
}
