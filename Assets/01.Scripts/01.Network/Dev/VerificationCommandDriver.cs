#if UNITY_EDITOR || DEVELOPMENT_BUILD

// System
using System;
using System.Collections.Generic;
using System.IO;

// Unity
using UnityEngine;

// Third party
using Mirror;

// Project
using Geumeum.AI;
using Geumeum.Combat;
using Geumeum.Common;
using Geumeum.Systems;
using Geumeum.UI;

namespace Geumeum.Network
{
    /// <summary> 명시적으로 지정한 JSON 큐의 제한된 M2 명령만 실행한다 </summary>
    public sealed partial class VerificationCommandDriver : MonoBehaviour
    {
        private const string PATH_ARGUMENT = "-verification-command-path";
        private const float POLL_INTERVAL = 0.25f;
        private const int MAX_EXECUTION_ID_LENGTH = 128;

        private static VerificationCommandDriver G_Instance;

        private string _commandPath;
        private string _responsePath;
        private string _captureNamePrefix;
        private string _lastExecutionId;
        private bool _checkpointLoadFailed;
        private long _lastProcessedWriteTicks;
        private float _nextPollTime;

        [Serializable]
        private sealed class CommandQueue
        {
            public string executionId;
            public VerificationCommand[] commands;
        }

        [Serializable]
        private sealed class VerificationCommand
        {
            public int commandId;
            public string verb;
            public int index;
            public int secondaryIndex;
            public uint uintValue;
            public bool captureScreen;
        }

        [Serializable]
        private sealed class CommandResponse
        {
            public int commandId;
            public string verb;
            public bool accepted;
            public string message;
            public string capturePath;
            public string captureMode;
        }

        [Serializable]
        private sealed class VerificationResponse
        {
            public string executionId;
            public string writtenUtc;
            public CommandResponse[] responses;
            public VerificationSnapshot snapshot;
        }

        [Serializable]
        private sealed class VerificationSnapshot
        {
            public bool lobbyBotFill;
            public LobbySlot[] lobbySlots;
            public bool localPlayerBound;
            public bool managementPanelBound;
            public bool managementPanelVisible;
            public bool managementPanelActiveInHierarchy;
            public bool managementPanelCanvasEnabled;
            public bool talentPageActiveInHierarchy;
            public bool panelTalentHasWeapon;
            public string inventoryStatusMessage;
            public string talentStatusMessage;
            public string shopStatusMessage;
            public string transferStatusMessage;
            public int[] visibleTalentNodeIds;
            public uint[] visibleRecipientNetIds;
            public uint activeTransferOfferId;
            public int activeTransferState;
            public uint activeTransferSenderNetId;
            public uint activeTransferRecipientNetId;
            public uint activeTransferEquipmentInstanceId;
            public double activeTransferExpiresAtGameplayTime;
            public int screenWidth;
            public int screenHeight;
            public bool applicationFocused;
            public string graphicsDeviceType;
            public int activeCameraCount;
            public int activeOverlayCanvasCount;
            public AbilityEnemySnapshot[] abilityEnemies;
            public BossVerificationSnapshot[] bosses;
            public PlayerVerificationSnapshot[] players;
            public uint localPlayerNetId;
            public string interactionPromptText;
            public int localPlayerLifeState;
            public float localPlayerHealth;
            public float localPlayerMaxHealth;
            public float localPlayerStamina;
            public float localPlayerSpirit;
            public bool localPlayerIsInvincible;
            public bool localPlayerIsSwallowed;
            public uint localPlayerSwallowGeneration;
            public double localPlayerSwallowedRemainingSeconds;
            public uint localPlayerResourceRevision;
            public bool localPlayerHasWeapon;
            public int localPlayerWeapon;
            public bool localPlayerHasEquippedWeapon;
            public WeaponCombatSnapshot localPlayerEquippedWeapon;
            public int runPhase;
            public uint runId;
            public int runScene;
            public int transitionState;
            public int resultPartyKind;
            public uint inventoryRevision;
            public int[] slotKinds;
            public int[] slotCounts;
            public uint[] slotEquipmentIds;
            public int[] slotWeapons;
            public LootVerificationSnapshot[] drops;
            public uint[] shopIds;
            public string interactionDisplay;
            public uint talentRevision;
            public bool talentHasWeapon;
            public int talentWeapon;
            public int talentSpentPoints;
            public int talentAvailablePoints;
            public int[] talentNodeIds;
            public int walletBalance;
            public uint walletRevision;
            public int resultOutcome;
            public uint resultRevision;
            public bool resultHasAuthoritativeFacts;
            public bool resultHasAuthoritativeScore;
            public int resultScore;
            public string resultEvaluationMessage;
        }

        [Serializable]
        private sealed class LootVerificationSnapshot
        {
            public uint netId;
            public WeaponCombatSnapshot equipment;
            public Vector3 position;
        }

        [Serializable]
        private sealed class BossVerificationSnapshot
        {
            public uint netId;
            public bool activeInHierarchy;
            public bool controllerEnabled;
            public float health;
            public BossPhaseSnapshot phase;
            public bool partControllerBound;
            public BossPartSnapshot parts;
            public bool soulControllerBound;
            public BossSoulSnapshot souls;
            public BossTelegraphCue telegraph;
            public BossTelegraphCue[] telegraphs;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void TryCreateFromCommandLine()
        {
            if (G_Instance != null)
            {
                return;
            }

            if (!TryParseCommandPathArgument(Environment.GetCommandLineArgs(),
                    out string commandPath, out string failure))
            {
                if (!string.IsNullOrEmpty(failure))
                {
                    Debug.LogError($"[VerificationCommandDriver] {failure}");
                }

                return;
            }

            GameObject gameObject = new GameObject("@VerificationCommandDriver");
            DontDestroyOnLoad(gameObject);
            G_Instance = gameObject.AddComponent<VerificationCommandDriver>();
            G_Instance.Initialize(commandPath);
        }

        public static bool TryParseCommandPathArgument(string[] arguments,
            out string path, out string failure)
        {
            path = null;
            failure = null;
            if (arguments == null)
            {
                return false;
            }

            for (int i = 0; i < arguments.Length; ++i)
            {
                if (!string.Equals(arguments[i], PATH_ARGUMENT,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if ((i + 1 >= arguments.Length) ||
                    string.IsNullOrWhiteSpace(arguments[i + 1]))
                {
                    failure = $"{PATH_ARGUMENT} 뒤에 JSON 경로가 필요하다";
                    return false;
                }

                try
                {
                    string fullPath = Path.GetFullPath(arguments[i + 1]);
                    if (!string.Equals(Path.GetExtension(fullPath), ".json",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        failure = "명령 큐는 JSON 파일이어야 한다";
                        return false;
                    }

                    string directory = Path.GetDirectoryName(fullPath);
                    if (string.IsNullOrWhiteSpace(directory) ||
                        !Directory.Exists(directory))
                    {
                        failure = "명령 큐 폴더가 존재하지 않는다";
                        return false;
                    }

                    path = fullPath;
                    return true;
                }
                catch (Exception exception)
                {
                    failure = $"명령 큐 경로를 해석하지 못했다: {exception.Message}";
                    return false;
                }
            }

            return false;
        }

        private void Initialize(string commandPath)
        {
            _commandPath = commandPath;
            string directory = Path.GetDirectoryName(commandPath);
            string name = Path.GetFileNameWithoutExtension(commandPath);
            _responsePath = Path.Combine(directory, name + ".response.json");
            _captureNamePrefix = Path.Combine(directory, name + ".capture");
            TryLoadLastExecutionId();
            Debug.Log($"[VerificationCommandDriver] 명령 큐를 감시한다: {_commandPath}", this);
        }

        private void OnDestroy()
        {
            if (G_Instance == this)
            {
                G_Instance = null;
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextPollTime)
            {
                return;
            }

            _nextPollTime = Time.unscaledTime + POLL_INTERVAL;
            TryProcessQueue();
        }

        private void TryProcessQueue()
        {
            if (_checkpointLoadFailed)
            {
                return;
            }

            if (!File.Exists(_commandPath))
            {
                return;
            }

            long writeTicks;
            string json;
            try
            {
                writeTicks = File.GetLastWriteTimeUtc(_commandPath).Ticks;
                if (writeTicks == _lastProcessedWriteTicks)
                {
                    return;
                }

                json = File.ReadAllText(_commandPath);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            CommandQueue queue;
            try
            {
                queue = JsonUtility.FromJson<CommandQueue>(json);
            }
            catch (ArgumentException exception)
            {
                WriteFailureResponse("invalid", $"JSON을 읽지 못했다: {exception.Message}");
                _lastProcessedWriteTicks = writeTicks;
                return;
            }

            if ((queue == null) || string.IsNullOrWhiteSpace(queue.executionId) ||
                (queue.executionId.Length > MAX_EXECUTION_ID_LENGTH) ||
                (queue.commands == null))
            {
                WriteFailureResponse("invalid", "executionId와 commands가 필요하다");
                _lastProcessedWriteTicks = writeTicks;
                return;
            }

            if (string.Equals(queue.executionId, _lastExecutionId,
                    StringComparison.Ordinal))
            {
                _lastProcessedWriteTicks = writeTicks;
                return;
            }

            var response = new VerificationResponse
            {
                executionId = queue.executionId,
                writtenUtc = DateTime.UtcNow.ToString("O"),
                responses = new CommandResponse[queue.commands.Length],
            };

            for (int i = 0; i < queue.commands.Length; ++i)
            {
                response.responses[i] = Execute(queue.commands[i]);
            }

            response.snapshot = CaptureSnapshot();
            if (TryWriteResponse(response))
            {
                _lastExecutionId = queue.executionId;
                _lastProcessedWriteTicks = writeTicks;
            }
        }

        private CommandResponse Execute(VerificationCommand command)
        {
            var response = new CommandResponse
            {
                commandId = (command == null) ? 0 : command.commandId,
                verb = (command == null) ? string.Empty : command.verb,
                accepted = false,
                message = "명령 형식이 올바르지 않다",
                capturePath = string.Empty,
                captureMode = string.Empty,
            };

            if ((command == null) || (command.commandId <= 0) ||
                string.IsNullOrWhiteSpace(command.verb))
            {
                return response;
            }

            FunctionalManagementPanel panel = FunctionalManagementPanel.Instance;
            try
            {
                response.accepted = ExecuteKnownVerb(panel, command,
                    out response.message);
                bool worldOnlyCapture = string.Equals(command.verb,
                    "capture_world", StringComparison.Ordinal);
                if (command.captureScreen || string.Equals(command.verb,
                        "capture", StringComparison.Ordinal) || worldOnlyCapture)
                {
                    TryCaptureScreen(command.commandId, !worldOnlyCapture,
                        out response.capturePath, out response.captureMode,
                        out string captureFailure);
                    if (!string.IsNullOrEmpty(captureFailure))
                    {
                        response.message += " 화면 캡처 실패: " + captureFailure;
                    }
                }
            }
            catch (Exception exception)
            {
                response.accepted = false;
                response.message = $"명령 실행 중 예외가 발생했다: {exception.Message}";
            }

            return response;
        }

        private void PauseHitTest()
        {
            RunManager.ServerInstance?.ServerAddPauseReason(RunPauseReason.BossCinematic, "Hit reaction verification");
        }

        private static bool ExecuteKnownVerb(FunctionalManagementPanel panel,
            VerificationCommand command, out string message)
        {
            if (command.verb.StartsWith("ability_", StringComparison.Ordinal))
            { return G_Instance.ExecuteAbilityVerification(command, out message); }
            if (command.verb == "hit_test_overlap")
            {
                if (!NetworkServer.active || NetworkClient.localPlayer == null)
                { message = "Server host required"; return false; }
                Vector3 position = NetworkClient.localPlayer.transform.position;
                foreach (var player in PlayerStatus.ServerPlayers)
                {
                    player.GetComponent<PlayerController>().ServerTeleport(position);
                }

                message = "Players placed at the same position"; return true;
            }
            if (command.verb == "hit_test_damage")
            {
                if (!NetworkServer.active || !NetworkServer.spawned.TryGetValue(command.uintValue, out var target) ||
                    !target.TryGetComponent(out PlayerStatus status) || command.index <= 0)
                { message = "Server player and positive damage required"; return false; }
                status.TakeDamage(new DamageInfo(command.index, uint.MaxValue, HitStrength.Light));
                if (command.secondaryIndex == 1) { G_Instance.Invoke(nameof(PauseHitTest), 0.08f); }
                var attack = target.GetComponent<PlayerAttack>();
                var rejection = attack.ServerRequestAttack((uint)command.commandId, Vector2.up, default);
                message = $"reacting={status.IsHitReacting}, generation={status.HitReaction.Generation}, attack={rejection}";
                return true;
            }
            if (command.verb == "loot_test_defeat")
            {
                if (!NetworkServer.active) { message = "서버 전용"; return false; }
                int count = 0;
                foreach (var enemy in UnityEngine.Object.FindObjectsByType<EnemyStatus>(FindObjectsInactive.Exclude))
                {
                    if (enemy.IsDead || count >= Mathf.Clamp(command.index, 1, 100)) { continue; }
                    enemy.TakeDamage(DamageInfo.FromEnvironment(Mathf.CeilToInt(enemy.MaxHealth) + 1));
                    ++count;
                }
                message = "실제 적 사망 처리=" + count; return count > 0;
            }
            if (command.verb == "loot_test_position")
            {
                if (!NetworkServer.active || !NetworkServer.spawned.TryGetValue(command.uintValue, out var destination))
                { message = "서버 대상 없음"; return false; }
                var actors = new List<PlayerStatus>(PlayerStatus.ServerPlayers);
                actors.Sort((a,b) => a.netId.CompareTo(b.netId));
                for (int i = 0; i < actors.Count; ++i)
                {
                    if (command.index >= 0 && command.index != i) { continue; }
                    actors[i].GetComponent<PlayerController>().ServerTeleport(destination.transform.position + Vector3.back);
                }
                message = "검증 시작 위치 배치"; return true;
            }
            if (command.verb == "ui_test_supplies")
            {
                if (!NetworkServer.active || !NetworkServer.spawned.TryGetValue(command.uintValue, out var actor))
                { message = "서버 대상 없음"; return false; }
                var inventory = actor.GetComponent<InventoryController>();
                if (inventory == null) { message = "배낭 없음"; return false; }
                foreach (var kind in new[]{ShopItemKind.HealthPotion, ShopItemKind.RerollTicket, ShopItemKind.UpgradeTicket})
                {
                    inventory.ServerTryStoreConsumable(kind, 1, 3, 3, inventory.Revision);
                }
                UnityEngine.Object.FindAnyObjectByType<GrowthManager>().ServerTryAddExperience(1000);
                UnityEngine.Object.FindAnyObjectByType<CurrencyManager>().TryCredit(5000);
                message = "UI 테스트용 물약/티켓/경험치/노잣돈 지급 (몬스터 장비 드롭 검증과 별개)"; return true;
            }
            if (command.verb == "ui_test_open_shop_phase")
            {
                var run = RunManager.ServerInstance;
                if (!NetworkServer.active || run == null || run.Phase != RunPhase.Farming)
                { message = "파밍 서버 필요"; return false; }
                typeof(RunManager).GetMethod("CommitPhase", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .Invoke(run, new object[]{RunPhase.Shrink2});
                message = "객주 UI 검증용 2차 축소 진입"; return true;
            }
            if (command.verb == "ui_layout")
            {
                var lines = new List<string>();
                foreach (var button in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsInactive.Exclude))
                {
                    var corners = new Vector3[4]; button.GetComponent<RectTransform>().GetWorldCorners(corners);
                    lines.Add(button.name + ":" + button.interactable + ":" + ((corners[0]+corners[2])*.5f));
                }
                message = string.Join(";", lines); return true;
            }
            if (command.verb == "bot_inventory_request" || command.verb == "bot_inventory_probe")
            {
                var local = NetworkClient.localPlayer;
                var reader = local == null ? null : local.GetComponent<InventoryController>();
                var run = RunManager.LocalInstance;
                var director = RunSceneDirector.LocalInstance;
                if (reader == null || run == null || director == null) { message = "조회 실행기 없음"; return false; }
                if (command.verb == "bot_inventory_request")
                {
                    reader.RequestBotInventory(command.uintValue, run.RunId.Value, director.Revision.Value);
                    message = "봇 배낭 조회 요청"; return true;
                }
                bool available = reader.TryGetBotInventory(command.uintValue, run.RunId.Value, director.Revision.Value, out var bag);
                int count = 0;
                if (available) { foreach (var slot in bag.Slots) { count += slot.Count; } }
                message = $"available={available}, bot={bag.BotNetId}, revision={bag.InventoryRevision}, count={count}, slots={bag.Slots?.Length ?? 0}";
                return available;
            }
            if (command.verb == "bot_test_store_potion")
            {
                if (!NetworkServer.active || !NetworkServer.spawned.TryGetValue(command.uintValue, out var actor) ||
                    !actor.TryGetComponent(out PlayerStatus bot) || bot.Participant.Kind != ParticipantKind.Bot)
                { message = "서버 봇 아님"; return false; }
                var inventory = actor.GetComponent<InventoryController>();
                var result = inventory.ServerTryStoreConsumable(ShopItemKind.HealthPotion, 1, 3, 3, inventory.Revision);
                message = result.ToString(); return result == InventoryResult.Success;
            }
            if (command.verb == "bot_test_group_enemy" || command.verb == "bot_test_group_boss")
            {
                if (!NetworkServer.active) { message = "서버 아님"; return false; }
                Transform target = null;
                foreach (var identity in NetworkServer.spawned.Values)
                {
                    if (identity == null) { continue; }
                    if (command.verb == "bot_test_group_enemy" && identity.TryGetComponent(out EnemyStatus enemy) && !enemy.IsDead)
                    { target = enemy.transform; break; }
                    if (command.verb == "bot_test_group_boss" && identity.TryGetComponent(out BossStatus boss) && !boss.IsDead)
                    { target = boss.transform; break; }
                }
                if (target == null) { message = "전투 대상 없음"; return false; }
                foreach (var player in PlayerStatus.ServerPlayers)
                {
                    WeaponKind weapon = player.GetComponent<PlayerWeapon>().Weapon;
                    float distance = weapon == WeaponKind.Sword ? 2f : weapon == WeaponKind.Bow ? 9f : 3f;
                    float side = weapon == WeaponKind.Sword ? 0f : weapon == WeaponKind.Bow ? 2f : -2f;
                    player.GetComponent<PlayerController>().ServerTeleport(target.position + new Vector3(side, 0f, -distance));
                }
                message = "전투 검증 시작 위치 배치"; return true;
            }
            if (string.Equals(command.verb, "bot_place_near_enemy", StringComparison.Ordinal))
            {
                if ((!NetworkServer.active) || (command.index <= 0) ||
                    (!NetworkServer.spawned.TryGetValue(command.uintValue, out NetworkIdentity bot)) ||
                    (!bot.TryGetComponent(out PlayerStatus status)) ||
                    (status.Participant.Kind != ParticipantKind.Bot))
                { message = "서버 봇 대상이 필요하다"; return false; }
                foreach (EnemyStatus enemy in UnityEngine.Object.FindObjectsByType<EnemyStatus>(FindObjectsInactive.Exclude))
                {
                    if (enemy.IsDead) { continue; }
                    Vector3 position = enemy.transform.position - Vector3.forward * command.index;
                    bot.GetComponent<PlayerController>().ServerTeleport(position);
                    message = $"bot={bot.netId}, enemy={enemy.netId}, enemyHealth={enemy.Health}, position={position}";
                    return true;
                }
                message = "살아 있는 잡귀가 없다"; return false;
            }
            if (string.Equals(command.verb, "host_offset", StringComparison.Ordinal))
            {
                if ((!NetworkServer.active) || (NetworkClient.localPlayer == null))
                { message = "호스트 전용"; return false; }
                PlayerController player = NetworkClient.localPlayer.GetComponent<PlayerController>();
                player.ServerTeleport(player.transform.position + new Vector3(command.index, 0f, command.secondaryIndex));
                message = "호스트 이동 목표 배치"; return true;
            }
            if (string.Equals(command.verb, "brain_pause", StringComparison.Ordinal))
            {
                RunManager run = RunManager.ServerInstance;
                if ((!NetworkServer.active) || (run == null)) { message = "서버 런이 없다"; return false; }
                bool changed = (command.index != 0)
                    ? run.ServerAddPauseReason(RunPauseReason.BossCinematic, "BT verification")
                    : run.ServerClearPauseReason(RunPauseReason.BossCinematic);
                message = $"pause={run.IsPaused}"; return changed;
            }
            if (string.Equals(command.verb, "bot_damage", StringComparison.Ordinal) ||
                string.Equals(command.verb, "bot_heal", StringComparison.Ordinal))
            {
                if ((!NetworkServer.active) ||
                    (!NetworkServer.spawned.TryGetValue(command.uintValue, out NetworkIdentity bot)) ||
                    (!bot.TryGetComponent(out PlayerStatus status)) ||
                    (status.Participant.Kind != ParticipantKind.Bot) || (command.index <= 0))
                {
                    message = "서버 봇 대상 또는 수치가 유효하지 않다";
                    return false;
                }

                float before = status.Health;
                if (command.verb == "bot_damage")
                {
                    status.TakeDamage(DamageInfo.FromEnvironment(command.index));
                }
                else
                {
                    status.TryRestoreHealth(command.index);
                }

                message = $"bot={bot.netId}, health={before}->{status.Health}";
                return !Mathf.Approximately(before, status.Health);
            }

            if (string.Equals(command.verb, "bot_spawn", StringComparison.Ordinal))
            {
                NetworkIdentity host = NetworkClient.localPlayer;
                NetworkManager manager = NetworkManager.singleton;
                if ((!NetworkServer.active) || (host == null) || (manager == null))
                {
                    message = "서버 호스트에서만 봇을 생성할 수 있다";
                    return false;
                }

                bool spawned = PlayerBotSpawner.TrySpawn(manager.playerPrefab,
                    host.transform.position + host.transform.right * 2f,
                    host.transform.rotation, (WeaponKind)command.index,
                    (CharacterKind)command.secondaryIndex, out NetworkIdentity bot);
                message = spawned
                    ? $"bot={bot.netId}, participant={bot.GetComponent<PlayerStatus>().Participant}, owner={(bot.connectionToClient == null ? "none" : "connected")}, role={bot.GetComponent<PlayerRunAgent>().HasRole}"
                    : "봇 생성 거부";
                return spawned;
            }

            if (string.Equals(command.verb, "bot_despawn", StringComparison.Ordinal))
            {
                bool removed = NetworkServer.active &&
                    NetworkServer.spawned.TryGetValue(command.uintValue, out NetworkIdentity bot) &&
                    bot.TryGetComponent(out PlayerStatus status) &&
                    PlayerBotSpawner.TryDespawn(status.Participant);
                message = removed ? "봇 제거" : "봇 제거 거부";
                return removed;
            }

            if (string.Equals(command.verb, "attack_request", StringComparison.Ordinal))
            {
                PlayerAttack attack = (NetworkClient.localPlayer == null) ? null
                    : NetworkClient.localPlayer.GetComponent<PlayerAttack>();
                bool sent = (attack != null) && (attack.RequestAttack());
                message = sent ? "기본 공격 요청 전송" : "기본 공격 요청 불가";
                return sent;
            }
            if (string.Equals(command.verb, "lobby_select_weapon", StringComparison.Ordinal))
            {
                LobbyController lobby = FindClientLobbyController();
                bool sent = (lobby != null) && (lobby.RequestWeaponPreference((WeaponKind)command.index));
                message = sent ? "무기 선호 요청 전송" : "무기 선호 요청 불가";
                return sent;
            }
            if (string.Equals(command.verb, "interaction_request", StringComparison.Ordinal))
            {
                NetworkIdentity player = NetworkClient.localPlayer;
                IInteractionRequestSource requests = (player == null)
                    ? null : player.GetComponent<InteractionController>();
                bool sent = (requests != null) && (requests.RequestInteract(command.uintValue));
                message = sent ? "상호작용 요청을 전송했다. 서버 승인은 별도 상태로 확인한다"
                    : "상호작용 요청을 전송하지 못했다";
                return sent;
            }
            if (string.Equals(command.verb, "capture", StringComparison.Ordinal) ||
                string.Equals(command.verb, "capture_world",
                    StringComparison.Ordinal))
            {
                message = "화면 snapshot을 기록했다";
                return true;
            }

            if (string.Equals(command.verb, "lobby_bot_fill", StringComparison.Ordinal))
            {
                LobbyController lobby = FindClientLobbyController();
                bool accepted = lobby != null && lobby.RequestBotFill(command.index != 0);
                message = accepted ? "봇 충원 요청 전송" : "호스트 로비 없음";
                return accepted;
            }

            if (string.Equals(command.verb, "bot_test_transition", StringComparison.Ordinal))
            {
                bool accepted = NetworkServer.active && RunSceneDirector.ServerInstance != null &&
                    RunSceneDirector.ServerInstance.ServerBeginTransition((RunSceneId)command.index);
                message = accepted ? "씬 전환 시작" : "씬 전환 거절";
                return accepted;
            }
            if (string.Equals(command.verb, "bot_test_gate", StringComparison.Ordinal))
            {
                bool accepted = NetworkServer.active && RunManager.ServerInstance != null &&
                    RunManager.ServerInstance.ServerTryStartBossEntranceCountdown(NetworkServer.localConnection);
                message = accepted ? "실제 보스 입구 카운트다운" : "입구 거절"; return accepted;
            }
            if (string.Equals(command.verb, "bot_test_player_damage", StringComparison.Ordinal))
            {
                if (!NetworkServer.active || !NetworkServer.spawned.TryGetValue(command.uintValue, out NetworkIdentity actor) ||
                    !actor.TryGetComponent(out PlayerStatus status)) { message = "대상 없음"; return false; }
                status.TakeDamage(DamageInfo.FromEnvironment(command.index)); message = "플레이어 피해 적용"; return true;
            }
            if (string.Equals(command.verb, "bot_test_spirit", StringComparison.Ordinal))
            {
                if (!NetworkServer.active) { message = "서버 아님"; return false; }
                foreach (PlayerStatus actor in PlayerStatus.ServerPlayers) { actor.ServerAddSpirit(command.index); }
                message = "테스트 신력 지급"; return true;
            }
            if (string.Equals(command.verb, "bot_test_boss_damage", StringComparison.Ordinal))
            {
                if (!NetworkServer.active) { message = "서버 아님"; return false; }
                foreach (var identity in NetworkServer.spawned.Values)
                {
                    if (identity != null && identity.TryGetComponent(out BossStatus boss))
                    { boss.TakeDamage(DamageInfo.FromEnvironment(command.index)); message = "보스 단계 테스트 피해"; return true; }
                }
                message = "보스 없음"; return false;
            }
            if (string.Equals(command.verb, "bot_test_soul_window", StringComparison.Ordinal))
            {
                if (!NetworkServer.active) { message = "서버 아님"; return false; }
                foreach (var identity in NetworkServer.spawned.Values)
                {
                    if (identity != null && identity.TryGetComponent(out BossSoulController souls))
                    { souls.ServerBeginFinal(RunTimeLocator.Current.GameplayElapsedSeconds); message = "원혼 단계 QA 시작"; return true; }
                }
                message = "보스 없음"; return false;
            }
            if (string.Equals(command.verb, "bot_test_finish", StringComparison.Ordinal))
            {
                bool accepted = NetworkServer.active && RunManager.ServerInstance != null &&
                    RunManager.ServerInstance.TryFinishRun((RunOutcome)command.index);
                message = accepted ? "런 종료 요청" : "런 종료 거절";
                return accepted;
            }
            if (string.Equals(command.verb, "bot_test_return", StringComparison.Ordinal))
            {
                if (!NetworkServer.active || RunManager.ServerInstance == null) { message = "서버 없음"; return false; }
                RunManager.ServerInstance.ServerReturnToLobby(); message = "로비 복귀 요청"; return true;
            }

            if (string.Equals(command.verb, "lobby_select_character",
                    StringComparison.Ordinal) ||
                string.Equals(command.verb, "lobby_ready", StringComparison.Ordinal))
            {
                return ExecuteLobbyVerb(command, out message);
            }

            if (panel == null)
            {
                message = "기능 UI가 아직 준비되지 않았다";
                return false;
            }

            switch (command.verb)
            {
                case "ui_toggle":
                    panel.Toggle();
                    break;
                case "ui_open_inventory":
                    panel.OpenInventory();
                    break;
                case "ui_open_transfer":
                    panel.OpenTransfer();
                    break;
                case "ui_open_talents":
                    panel.OpenTalents();
                    break;
                case "ui_close":
                    panel.Close();
                    break;
                case "inventory_select":
                    panel.SelectInventorySlot(command.index);
                    break;
                case "equipment_equip":
                    panel.SelectInventorySlot(command.index);
                    panel.RequestSelectedPrimaryAction();
                    break;
                case "equipment_unequip":
                    panel.RequestUnequip();
                    break;
                case "potion_use":
                    panel.SelectInventorySlot(command.index);
                    panel.RequestUseSelectedPotion();
                    break;
                case "ticket_use_stored":
                    panel.SelectInventorySlot(command.secondaryIndex);
                    panel.SelectInventorySlot(command.index);
                    panel.RequestUseSelectedTicket();
                    break;
                case "ticket_use_equipped":
                    panel.SelectInventorySlot(command.index);
                    panel.SelectEquippedTicketTarget();
                    panel.RequestUseSelectedTicket();
                    break;
                case "shop_open":
                    panel.OpenShop(command.uintValue);
                    break;
                case "shop_purchase":
                    panel.RequestPurchase(command.index);
                    break;
                case "transfer_offer":
                    panel.SelectInventorySlot(command.index);
                    panel.RequestOfferTransfer(command.secondaryIndex);
                    break;
                case "transfer_accept":
                    panel.RequestAcceptTransfer();
                    break;
                case "transfer_reject":
                    panel.RequestRejectTransfer();
                    break;
                case "transfer_cancel":
                    panel.RequestCancelTransfer();
                    break;
                case "talent_allocate":
                    panel.RequestAllocateTalent(command.index);
                    break;
                case "talent_reset":
                    panel.RequestResetTalents();
                    break;
                default:
                    message = "허용되지 않은 명령이다";
                    return false;
            }

            message = "명령을 기능 UI에 전달했다";
            return true;
        }

        private static bool ExecuteLobbyVerb(VerificationCommand command,
            out string message)
        {
            LobbyController lobby = FindClientLobbyController();
            if (lobby == null)
            {
                message = "로비 요청 창구를 찾지 못했다";
                return false;
            }

            if (string.Equals(command.verb, "lobby_select_character",
                    StringComparison.Ordinal))
            {
                CharacterKind character = (CharacterKind)command.index;
                if (!Enum.IsDefined(typeof(CharacterKind), character))
                {
                    message = "선택할 역할 번호가 유효하지 않다";
                    return false;
                }

                lobby.CmdSelectCharacter(character);
                message = "로비 역할 선택을 요청했다";
                return true;
            }

            if ((command.index != 0) && (command.index != 1))
            {
                message = "준비 상태는 0 또는 1이어야 한다";
                return false;
            }

            lobby.CmdSetReady(command.index == 1);
            message = "로비 준비 상태 변경을 요청했다";
            return true;
        }

        private static LobbyController FindClientLobbyController()
        {
            foreach (NetworkIdentity identity in NetworkClient.spawned.Values)
            {
                if (identity == null)
                {
                    continue;
                }

                LobbyController lobby = identity.GetComponent<LobbyController>();
                if ((lobby != null) && lobby.isActiveAndEnabled)
                {
                    return lobby;
                }
            }

            return null;
        }

        private void TryCaptureScreen(int commandId, bool includeOverlay,
            out string capturePath, out string captureMode, out string failure)
        {
            capturePath = string.Empty;
            captureMode = string.Empty;
            failure = null;
            if (Application.isBatchMode)
            {
                failure = "batch mode에서는 화면을 캡처할 수 없다";
                return;
            }

            Camera camera = FindCaptureCamera();
            if (camera == null)
            {
                failure = "활성 캡처 카메라가 없다";
                return;
            }

            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);
            string destination = _captureNamePrefix + "." + commandId + ".png";
            string temporary = destination + ".tmp";
            RenderTexture renderTexture = null;
            Texture2D texture = null;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            int previousCullingMask = camera.cullingMask;
            CameraClearFlags previousClearFlags = camera.clearFlags;
            Canvas[] canvases = includeOverlay
                ? UnityEngine.Object.FindObjectsByType<Canvas>(
                    FindObjectsInactive.Exclude)
                : Array.Empty<Canvas>();
            var canvasStates = new CanvasCaptureState[canvases.Length];
            var canvasObjects = new HashSet<GameObject>();
            var layerStates = new List<LayerCaptureState>();
            int convertedCanvasCount = 0;

            try
            {
                for (int i = 0; i < canvases.Length; ++i)
                {
                    Canvas canvas = canvases[i];
                    if ((canvas == null) || (!canvas.isRootCanvas) ||
                        (!canvas.isActiveAndEnabled) ||
                        (canvas.renderMode != RenderMode.ScreenSpaceOverlay))
                    {
                        continue;
                    }

                    canvasStates[convertedCanvasCount++] =
                        new CanvasCaptureState(canvas);
                    Transform[] transforms =
                        canvas.GetComponentsInChildren<Transform>(true);
                    for (int j = 0; j < transforms.Length; ++j)
                    {
                        canvasObjects.Add(transforms[j].gameObject);
                    }

                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = ResolveCanvasPlaneDistance(camera);
                }

                int captureLayer = FindUnusedCaptureLayer(canvasObjects);
                if ((convertedCanvasCount > 0) && (captureLayer < 0))
                {
                    throw new InvalidOperationException(
                        "UI 전용 캡처 레이어를 확보하지 못했다");
                }

                foreach (GameObject canvasObject in canvasObjects)
                {
                    layerStates.Add(new LayerCaptureState(canvasObject));
                    canvasObject.layer = captureLayer;
                }

                int captureLayerMask = (captureLayer >= 0)
                    ? 1 << captureLayer
                    : 0;
                camera.cullingMask = previousCullingMask & ~captureLayerMask;
                renderTexture = new RenderTexture(width, height, 24,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
                {
                    name = "VerificationCapture",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                if (!renderTexture.Create())
                {
                    throw new InvalidOperationException(
                        "오프스크린 렌더 텍스처를 만들지 못했다");
                }

                Canvas.ForceUpdateCanvases();
                var renderRequest =
                    new UnityEngine.Rendering.RenderPipeline.StandardRequest
                    {
                        destination = renderTexture,
                        mipLevel = 0,
                        slice = 0,
                        face = CubemapFace.Unknown,
                    };
                bool useRenderRequest =
                    UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(
                        camera, renderRequest);
                RenderCapturePass(camera, renderTexture, renderRequest,
                    useRenderRequest);

                if (convertedCanvasCount > 0)
                {
                    camera.clearFlags = CameraClearFlags.Depth;
                    camera.cullingMask = captureLayerMask;
                    RenderCapturePass(camera, renderTexture, renderRequest,
                        useRenderRequest);
                    captureMode = useRenderRequest
                        ? "offscreen-render-request-overlay-composite"
                        : "offscreen-camera-overlay-composite";
                }
                else
                {
                    captureMode = includeOverlay
                        ? (useRenderRequest
                            ? "offscreen-render-request"
                            : "offscreen-camera")
                        : (useRenderRequest
                            ? "offscreen-render-request-world"
                            : "offscreen-camera-world");
                }

                RenderTexture.active = renderTexture;
                texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                texture.Apply(false, false);
                byte[] png = texture.EncodeToPNG();
                if ((png == null) || (png.Length == 0))
                {
                    throw new InvalidDataException("PNG 인코딩 결과가 비어 있다");
                }

                File.WriteAllBytes(temporary, png);
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }

                File.Move(temporary, destination);
                capturePath = destination;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                if (File.Exists(temporary))
                {
                    try
                    {
                        File.Delete(temporary);
                    }
                    catch (Exception cleanupException)
                    {
                        Debug.LogWarning(
                            $"[VerificationCommandDriver] 캡처 임시 파일 정리 실패: {cleanupException.Message}",
                            this);
                    }
                }
            }
            finally
            {
                RenderTexture.active = previousActive;
                camera.targetTexture = previousTarget;
                camera.cullingMask = previousCullingMask;
                camera.clearFlags = previousClearFlags;
                for (int i = layerStates.Count - 1; i >= 0; --i)
                {
                    layerStates[i].Restore();
                }

                for (int i = convertedCanvasCount - 1; i >= 0; --i)
                {
                    canvasStates[i].Restore();
                }

                Canvas.ForceUpdateCanvases();
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }

                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.Destroy(renderTexture);
                }
            }
        }

        private static void RenderCapturePass(Camera camera,
            RenderTexture renderTexture,
            UnityEngine.Rendering.RenderPipeline.StandardRequest renderRequest,
            bool useRenderRequest)
        {
            if (useRenderRequest)
            {
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(
                    camera, renderRequest);
                return;
            }

            camera.targetTexture = renderTexture;
            camera.Render();
        }

        private static Camera FindCaptureCamera()
        {
            Camera main = Camera.main;
            if ((main != null) && main.isActiveAndEnabled)
            {
                return main;
            }

            Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Exclude);
            Camera selected = null;
            for (int i = 0; i < cameras.Length; ++i)
            {
                Camera candidate = cameras[i];
                if ((candidate == null) || (!candidate.isActiveAndEnabled) ||
                    (candidate.targetTexture != null))
                {
                    continue;
                }

                if ((selected == null) || (candidate.depth < selected.depth))
                {
                    selected = candidate;
                }
            }

            return selected;
        }

        private static float ResolveCanvasPlaneDistance(Camera camera)
        {
            float minimum = camera.nearClipPlane + 0.01f;
            float maximum = camera.farClipPlane - 0.01f;
            return (maximum > minimum) ? Mathf.Clamp(100f, minimum, maximum) : minimum;
        }

        private static int FindUnusedCaptureLayer(
            HashSet<GameObject> canvasObjects)
        {
            if (canvasObjects.Count == 0)
            {
                return -1;
            }

            var usedOutsideCanvas = new bool[32];
            Transform[] transforms =
                UnityEngine.Object.FindObjectsByType<Transform>(
                    FindObjectsInactive.Include);
            for (int i = 0; i < transforms.Length; ++i)
            {
                GameObject candidate = transforms[i].gameObject;
                if (!canvasObjects.Contains(candidate))
                {
                    usedOutsideCanvas[candidate.layer] = true;
                }
            }

            for (int layer = 31; layer >= 0; --layer)
            {
                if (!usedOutsideCanvas[layer])
                {
                    return layer;
                }
            }

            return -1;
        }

        private readonly struct CanvasCaptureState
        {
            private readonly Canvas _canvas;
            private readonly RenderMode _renderMode;
            private readonly Camera _worldCamera;
            private readonly float _planeDistance;

            public CanvasCaptureState(Canvas canvas)
            {
                _canvas = canvas;
                _renderMode = canvas.renderMode;
                _worldCamera = canvas.worldCamera;
                _planeDistance = canvas.planeDistance;
            }

            public void Restore()
            {
                if (_canvas == null)
                {
                    return;
                }

                _canvas.renderMode = _renderMode;
                _canvas.worldCamera = _worldCamera;
                _canvas.planeDistance = _planeDistance;
            }
        }

        private readonly struct LayerCaptureState
        {
            private readonly GameObject _gameObject;
            private readonly int _layer;

            public LayerCaptureState(GameObject gameObject)
            {
                _gameObject = gameObject;
                _layer = gameObject.layer;
            }

            public void Restore()
            {
                if (_gameObject != null)
                {
                    _gameObject.layer = _layer;
                }
            }
        }

        [Serializable]
        private sealed class PlayerVerificationSnapshot
        {
            public WeaponAbilityMark wedge, echoFirst, echoSecond;
            public bool recalling;
            public float echoCooldown;
            public byte counterMarks;
            public Vector2 actionFacing;
            public bool actionFacingActive;
            public float skillCooldown;
            public bool skillActive;
            public double bellFieldEnd;
            public Vector3 bellFieldPoint;
            public float bellFieldRadius, fieldBuffMultiplier;
            public double fieldBuffExpiry;
            public bool placementActive, placementPending, talentBlocked;
            public uint netId;
            public float health;
            public bool hitReacting;
            public PlayerHitReactionCue hitReaction;
            public double gameplayTime;
            public int animatorState;
            public bool animatorTransition;
            public bool hasRole;
            public string participant;
            public int weapon;
            public bool owned;
            public float stamina;
            public float spirit;
            public Vector3 position;
            public string brainNode;
            public string brainGraph;
            public bool brainCatchUp;
            public uint brainGeneration;
            public uint brainTarget;
            public int brainAttacks;
            public int brainRolls, brainParryAttempts, brainHeals, brainRescues, brainPrecision;
            public int pathFailures;
            public ActionRejection brainRejection;
        }

        private static VerificationSnapshot CaptureSnapshot()
        {
            var snapshot = new VerificationSnapshot
            {
                slotKinds = Array.Empty<int>(),
                slotCounts = Array.Empty<int>(),
                talentNodeIds = Array.Empty<int>(),
                visibleTalentNodeIds = Array.Empty<int>(),
                visibleRecipientNetIds = Array.Empty<uint>(),
                bosses = Array.Empty<BossVerificationSnapshot>(),
                inventoryStatusMessage = string.Empty,
                talentStatusMessage = string.Empty,
                shopStatusMessage = string.Empty,
                transferStatusMessage = string.Empty,
                screenWidth = Screen.width,
                screenHeight = Screen.height,
                applicationFocused = Application.isFocused,
                graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
                resultEvaluationMessage = string.Empty,
            };
            LobbyController lobby = NetworkServer.active ? LobbyController.ServerInstance : FindClientLobbyController();
            LobbyRoster roster = lobby == null ? null : lobby.GetComponent<LobbyRoster>();
            snapshot.lobbyBotFill = lobby != null && lobby.FillWithBots;
            snapshot.lobbySlots = roster == null ? Array.Empty<LobbySlot>() : new LobbySlot[roster.SlotCount];
            if (roster != null)
            {
                for (int i = 0; i < roster.SlotCount; ++i)
                {
                    snapshot.lobbySlots[i] = roster.GetSlot(i);
                }
            }

            EnemyStatus[] enemies = UnityEngine.Object.FindObjectsByType<EnemyStatus>(FindObjectsInactive.Exclude);
            snapshot.abilityEnemies = new AbilityEnemySnapshot[enemies.Length];
            for (int i = 0; i < enemies.Length; ++i)
            { snapshot.abilityEnemies[i] = new AbilityEnemySnapshot { netId = enemies[i].netId, health = enemies[i].Health, position = enemies[i].transform.position }; }
            PlayerStatus[] players = UnityEngine.Object.FindObjectsByType<PlayerStatus>(
                FindObjectsInactive.Exclude);
            snapshot.players = new PlayerVerificationSnapshot[players.Length];
            for (int i = 0; i < players.Length; ++i)
            {
                PlayerStatus player = players[i];
                PlayerRunAgent agent = player.GetComponent<PlayerRunAgent>();
                PlayerBrain brain = player.GetComponent<PlayerController>()?.ServerBrain;
                snapshot.players[i] = new PlayerVerificationSnapshot
                {
                    wedge = player.GetComponent<PlayerAttack>().Wedge,
                    echoFirst = player.GetComponent<PlayerAttack>().EchoFirst,
                    echoSecond = player.GetComponent<PlayerAttack>().EchoSecond,
                    recalling = player.GetComponent<PlayerAttack>().IsRecalling,
                    echoCooldown = player.GetComponent<PlayerAttack>().EchoCooldownRemaining,
                    counterMarks = player.GetComponent<PlayerAttack>().CounterMarks,
                    actionFacing = player.GetComponent<PlayerAttack>().ActionFacing,
                    actionFacingActive = player.GetComponent<PlayerAttack>().UsesActionFacing,
                    skillCooldown = player.GetComponent<PlayerAttack>().WeaponSkillCooldownRemaining,
                    skillActive = player.GetComponent<PlayerAttack>().IsWeaponSkillActive,
                    bellFieldEnd = player.GetComponent<PlayerAttack>().BellFieldEnd,
                    bellFieldPoint = player.GetComponent<PlayerAttack>().BellFieldPoint,
                    bellFieldRadius = player.GetComponent<PlayerAttack>().BellFieldRadius,
                    fieldBuffMultiplier = player.GetComponent<SupportBuffController>()?.ActiveDamageMultiplier ?? 1f,
                    fieldBuffExpiry = player.GetComponent<SupportBuffController>()?.ActiveExpiryGameplayTime ?? 0,
                    placementActive = player.GetComponent<PlayerAttack>().IsPlacingField,
                    placementPending = player.GetComponent<PlayerAttack>().IsPlacementPending,
                    talentBlocked = player.GetComponent<PlayerAttack>().BlocksTalentMutation,
                    netId = player.netId, health = player.Health,
                    hitReacting = player.IsHitReacting,
                    hitReaction = player.HitReaction,
                    gameplayTime = RunTimeLocator.Current.GameplayElapsedSeconds,
                    animatorState = player.GetComponentInChildren<Animator>()?.GetCurrentAnimatorStateInfo(0).shortNameHash ?? 0,
                    animatorTransition = player.GetComponentInChildren<Animator>()?.IsInTransition(0) ?? false,
                    hasRole = (agent != null) && agent.HasRole,
                    participant = player.Participant.ToString(),
                    weapon = (int)player.GetComponent<PlayerWeapon>().Weapon,
                    owned = player.connectionToClient != null,
                    stamina = player.Stamina, spirit = player.Spirit,
                    position = player.transform.position,
                    brainNode = brain?.ActiveNode,
                    brainCatchUp = brain?.IsCatchUpSprinting ?? false,
                    brainGraph = brain?.BehaviorGraph != null ? brain.BehaviorGraph.name : null,
                    brainGeneration = brain?.Generation ?? 0u,
                    brainTarget = brain?.TargetNetId ?? 0u,
                    brainAttacks = brain?.AcceptedAttacks ?? 0,
                    brainRolls = brain?.AcceptedRolls ?? 0,
                    brainParryAttempts = brain?.ParryAttempts ?? 0,
                    brainHeals = brain?.AcceptedHeals ?? 0,
                    brainRescues = brain?.StartedRescues ?? 0,
                    brainPrecision = brain?.AcceptedPrecision ?? 0,
                    pathFailures = brain?.PathFailures ?? 0,
                    brainRejection = brain?.LastRejection ?? ActionRejection.None,
                };
            }

            Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Exclude);
            for (int i = 0; i < cameras.Length; ++i)
            {
                if ((cameras[i] != null) && cameras[i].isActiveAndEnabled)
                {
                    ++snapshot.activeCameraCount;
                }
            }

            Canvas[] canvases = UnityEngine.Object.FindObjectsByType<Canvas>(
                FindObjectsInactive.Exclude);
            for (int i = 0; i < canvases.Length; ++i)
            {
                if ((canvases[i] != null) && canvases[i].isRootCanvas &&
                    canvases[i].isActiveAndEnabled &&
                    (canvases[i].renderMode == RenderMode.ScreenSpaceOverlay))
                {
                    ++snapshot.activeOverlayCanvasCount;
                }
            }

            FunctionalManagementPanel panel = FunctionalManagementPanel.Instance;
            snapshot.managementPanelBound = panel != null;
            snapshot.managementPanelVisible = (panel != null) && panel.IsVisible;
            if (panel != null)
            {
                snapshot.managementPanelActiveInHierarchy = panel.IsActiveInHierarchy;
                snapshot.managementPanelCanvasEnabled = panel.IsCanvasEnabled;
                snapshot.talentPageActiveInHierarchy = panel.IsTalentPageActive;
                snapshot.panelTalentHasWeapon = panel.CachedTalentHasWeapon;
                snapshot.inventoryStatusMessage = panel.InventoryStatusMessage;
                snapshot.talentStatusMessage = panel.TalentStatusMessage;
                snapshot.shopStatusMessage = panel.ShopStatusMessage;
                snapshot.transferStatusMessage = panel.TransferStatusMessage;
                snapshot.visibleTalentNodeIds = panel.CopyVisibleTalentNodeIds();
                snapshot.visibleRecipientNetIds = panel.CopyVisibleRecipientNetIds();
                EquipmentTransferOfferSnapshot offer = panel.ActiveTransferOffer;
                snapshot.activeTransferOfferId = offer.OfferId;
                snapshot.activeTransferState = (int)offer.State;
                snapshot.activeTransferSenderNetId = offer.SenderNetId;
                snapshot.activeTransferRecipientNetId = offer.RecipientNetId;
                snapshot.activeTransferEquipmentInstanceId =
                    offer.EquipmentInstanceId;
                snapshot.activeTransferExpiresAtGameplayTime =
                    offer.ExpiresAtGameplayTime;
            }

            if ((panel != null) && (panel.Currency != null))
            {
                snapshot.walletBalance = panel.Currency.Balance;
                snapshot.walletRevision = panel.Currency.Revision;
            }

            NetworkIdentity localPlayer = NetworkClient.localPlayer;
            snapshot.localPlayerBound = localPlayer != null;
            if (localPlayer != null)
            {
                CaptureOwnerState(localPlayer, snapshot);
            }

            snapshot.bosses = CaptureBosses();

            RunManager run = RunManager.LocalInstance;
            if (run != null)
            {
                snapshot.runPhase = (int)run.Phase;
                snapshot.runId = run.RunId.Value;
                RunSceneDirector director = RunSceneDirector.ServerInstance != null ? RunSceneDirector.ServerInstance : RunSceneDirector.LocalInstance;
                snapshot.runScene = director == null ? 0 : (int)director.CurrentScene;
                snapshot.transitionState = director == null ? 0 : (int)director.State;
                RunResultSnapshot result = run.Result;
                snapshot.resultPartyKind = (int)result.PartyKind;
                snapshot.resultOutcome = (int)result.Outcome;
                snapshot.resultRevision = result.ResultRevision;
                snapshot.resultHasAuthoritativeFacts = result.HasAuthoritativeFacts;
                snapshot.resultHasAuthoritativeScore = result.HasAuthoritativeScore;
                snapshot.resultScore = result.Score;
                snapshot.resultEvaluationMessage = result.EvaluationMessage ?? string.Empty;
            }

            return snapshot;
        }

        private static void CaptureOwnerState(NetworkIdentity localPlayer,
            VerificationSnapshot snapshot)
        {
            snapshot.localPlayerNetId = localPlayer.netId;
            InteractionController interaction = localPlayer.GetComponent<InteractionController>();
            snapshot.interactionPromptText = (interaction == null) ? null : interaction.PromptText;
            var loot = new List<LootVerificationSnapshot>();
            var shops = new List<uint>();
            foreach (var identity in NetworkClient.spawned.Values)
            {
                if (identity.TryGetComponent(out DroppedItem drop))
                { loot.Add(new LootVerificationSnapshot { netId = identity.netId, equipment = drop.Equipment, position = identity.transform.position }); }
                if (identity.GetComponent<ShopInteractionPoint>() != null) { shops.Add(identity.netId); }
            }
            snapshot.drops = loot.ToArray(); snapshot.shopIds = shops.ToArray();
            var prompt = UnityEngine.Object.FindAnyObjectByType<InteractionPromptView>();
            snapshot.interactionDisplay = prompt == null ? null : prompt.DisplayText;
            PlayerStatus status = localPlayer.GetComponent<PlayerStatus>();
            if (status != null)
            {
                snapshot.localPlayerLifeState = (int)status.LifeState;
                snapshot.localPlayerHealth = status.Health;
                snapshot.localPlayerMaxHealth = status.MaxHealth;
                snapshot.localPlayerStamina = status.Stamina;
                snapshot.localPlayerSpirit = status.Spirit;
                snapshot.localPlayerIsInvincible = status.IsInvincible;
                snapshot.localPlayerIsSwallowed = status.IsSwallowed;
                snapshot.localPlayerSwallowGeneration = status.SwallowGeneration;
                snapshot.localPlayerSwallowedRemainingSeconds =
                    status.SwallowedRemainingSeconds;
                snapshot.localPlayerResourceRevision = status.ResourceRevision;
            }

            PlayerWeapon weapon = localPlayer.GetComponent<PlayerWeapon>();
            if (weapon != null)
            {
                snapshot.localPlayerHasWeapon = weapon.HasWeapon;
                snapshot.localPlayerWeapon = (int)weapon.Weapon;
            }

            InventoryController inventory =
                localPlayer.GetComponent<InventoryController>();
            if (inventory != null)
            {
                snapshot.localPlayerHasEquippedWeapon =
                    inventory.HasEquippedWeapon;
                snapshot.localPlayerEquippedWeapon = inventory.EquippedWeapon;
                var slots = new InventorySlotSnapshot[InventorySnapshot.SLOT_COUNT];
                if (inventory.TryCopyOwnerSlots(slots, out uint revision))
                {
                    snapshot.inventoryRevision = revision;
                    snapshot.slotKinds = new int[slots.Length];
                    snapshot.slotCounts = new int[slots.Length];
                    snapshot.slotEquipmentIds = new uint[slots.Length];
                    snapshot.slotWeapons = new int[slots.Length];
                    for (int i = 0; i < slots.Length; ++i)
                    {
                        snapshot.slotKinds[i] = (int)slots[i].Kind;
                        snapshot.slotCounts[i] = slots[i].Count;
                        snapshot.slotEquipmentIds[i] = slots[i].Equipment.InstanceId;
                        snapshot.slotWeapons[i] = (int)slots[i].Equipment.WeaponKind;
                    }
                }
            }

            TalentController talents = localPlayer.GetComponent<TalentController>();
            if ((talents != null) && talents.TryGetOwnerAllocation(
                    out TalentAllocationSnapshot allocation))
            {
                snapshot.talentRevision = allocation.Revision;
                snapshot.talentHasWeapon = allocation.HasWeapon;
                snapshot.talentWeapon = (int)allocation.Weapon;
                snapshot.talentSpentPoints = allocation.SpentPoints;
                snapshot.talentAvailablePoints = talents.AvailablePoints;
                snapshot.talentNodeIds = (int[])allocation.NodeIds.Clone();
            }
        }

        private static BossVerificationSnapshot[] CaptureBosses()
        {
            var bosses = new List<BossVerificationSnapshot>();
            foreach (NetworkIdentity identity in NetworkClient.spawned.Values)
            {
                if (identity == null)
                {
                    continue;
                }

                BossController controller = identity.GetComponent<BossController>();
                if (controller == null)
                {
                    continue;
                }

                BossPartController parts = identity.GetComponent<BossPartController>();
                BossSoulController souls = identity.GetComponent<BossSoulController>();
                BossStatus status = identity.GetComponent<BossStatus>();
                IReadOnlyList<BossTelegraphCue> sourceTelegraphs =
                    controller.TelegraphCues;
                int telegraphCount = (sourceTelegraphs == null)
                    ? 0
                    : sourceTelegraphs.Count;
                var telegraphs = new BossTelegraphCue[telegraphCount];
                for (int i = 0; i < telegraphCount; ++i)
                {
                    telegraphs[i] = sourceTelegraphs[i];
                }

                bosses.Add(new BossVerificationSnapshot
                {
                    netId = identity.netId,
                    activeInHierarchy = controller.gameObject.activeInHierarchy,
                    controllerEnabled = controller.enabled,
                    health = (status == null) ? 0f : status.Health,
                    phase = controller.PhaseSnapshot,
                    partControllerBound = parts != null,
                    parts = (parts == null) ? default : parts.PartSnapshot,
                    soulControllerBound = souls != null,
                    souls = (souls == null) ? default : souls.SoulSnapshot,
                    telegraph = controller.TelegraphCue,
                    telegraphs = telegraphs,
                });
            }

            bosses.Sort((left, right) => left.netId.CompareTo(right.netId));
            return bosses.ToArray();
        }

        private void TryLoadLastExecutionId()
        {
            if (!File.Exists(_responsePath))
            {
                return;
            }

            try
            {
                VerificationResponse response = JsonUtility.FromJson<VerificationResponse>(
                    File.ReadAllText(_responsePath));
                if ((response == null) ||
                    string.IsNullOrWhiteSpace(response.executionId) ||
                    (response.executionId.Length > MAX_EXECUTION_ID_LENGTH))
                {
                    throw new InvalidDataException(
                        "응답 checkpoint에 유효한 executionId가 없습니다.");
                }

                _lastExecutionId = response.executionId;
            }
            catch (Exception exception)
            {
                _checkpointLoadFailed = true;
                Debug.LogError(
                    $"[VerificationCommandDriver] 기존 응답 checkpoint를 읽지 못해 명령 재실행을 차단합니다: {exception.Message}",
                    this);
            }
        }

        private void WriteFailureResponse(string executionId, string message)
        {
            var response = new VerificationResponse
            {
                executionId = executionId,
                writtenUtc = DateTime.UtcNow.ToString("O"),
                responses = new[]
                {
                    new CommandResponse
                    {
                        commandId = 0,
                        verb = string.Empty,
                        accepted = false,
                        message = message,
                        capturePath = string.Empty,
                        captureMode = string.Empty,
                    },
                },
                snapshot = CaptureSnapshot(),
            };
            TryWriteResponse(response);
        }

        private bool TryWriteResponse(VerificationResponse response)
        {
            string temporaryPath = _responsePath + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(response, true));
                if (File.Exists(_responsePath))
                {
                    File.Delete(_responsePath);
                }

                File.Move(temporaryPath, _responsePath);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[VerificationCommandDriver] 응답을 기록하지 못했다: {exception}",
                    this);
                return false;
            }
        }
    }
}

#endif
