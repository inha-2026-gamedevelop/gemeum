#if UNITY_EDITOR || DEVELOPMENT_BUILD
// System
using System;

// Unity
using UnityEngine;

// Third party
using Mirror;

// Project
using Geumeum.Combat;
using Geumeum.Common;
using Geumeum.Dev;
using Geumeum.UI;

namespace Geumeum.Network
{
    public sealed partial class VerificationCommandDriver
    {
        [Serializable]
        private sealed class ChatVerificationMessage
        {
            public int channel;
            public string sender;
            public string text;
        }

        [Serializable]
        private sealed class DevPanelCommandBody
        {
            public string panel;
            public string control;
            public string value;
        }

        private static bool TryExecuteChatDevVerb(VerificationCommand command, out bool accepted, out string message)
        {
            accepted = false;
            message = string.Empty;
            PartyChatPanel chat = PartyChatPanel.Instance;
            switch (command.verb)
            {
                case "chat_focus":
                    if (chat != null)
                    {
                        chat.FocusInput();
                        accepted = chat.IsTyping;
                    }
                    message = accepted ? "채팅 입력 시작" : "채팅 또는 입력 소유권 없음";
                    return true;
                case "chat_type":
                    if ((chat != null) && chat.IsTyping)
                    {
                        chat.InputField.text = command.text ?? string.Empty;
                        accepted = true;
                    }
                    message = accepted ? "채팅 입력" : "입력칸 선택 필요";
                    return true;
                case "chat_submit":
                    if ((chat != null) && chat.IsTyping)
                    {
                        // 실제 TMP 입력칸의 Enter 이벤트 경로를 통과한다
                        chat.InputField.onSubmit.Invoke(chat.InputField.text);
                        accepted = true;
                    }
                    message = accepted ? "채팅 제출 이벤트" : "입력칸 선택 필요";
                    return true;
                case "chat_cancel":
                    chat?.CancelInput();
                    accepted = chat != null;
                    message = "채팅 입력 취소";
                    return true;
                case "chat_tab":
                    chat?.SelectTab(command.index);
                    accepted = chat != null;
                    message = "채팅 탭 변경";
                    return true;
                case "chat_collapse":
                    chat?.SetCollapsed(command.enabled);
                    accepted = chat != null;
                    message = "채팅 접기 변경";
                    return true;
                case "chat_clear":
                    LocalChatLog.Clear();
                    accepted = true;
                    message = "로컬 채팅 기록 초기화";
                    return true;
                case "dev_panel_command":
                    if (NetworkServer.active && DevAccess.IsUnlocked)
                    {
                        DevPanelCommandBody body = JsonUtility.FromJson<DevPanelCommandBody>(command.text ?? "{}");
                        if ((body != null) && !string.IsNullOrEmpty(body.panel))
                        {
                            DevPanelHost.EnqueueCommandBody(JsonUtility.ToJson(body));
                            accepted = true;
                        }
                    }
                    message = accepted ? "개발패널 메인스레드 큐 제출" : "개발 호스트 또는 패널 명령 필요";
                    return true;
                case "owner_skill":
                    PlayerAttack attack = NetworkClient.localPlayer == null ? null
                        : NetworkClient.localPlayer.GetComponent<PlayerAttack>();
                    PlayerWeapon weapon = attack == null ? null : attack.GetComponent<PlayerWeapon>();
                    accepted = (attack != null) && ((weapon != null && weapon.Weapon == WeaponKind.Bell)
                        ? attack.RequestBeginBellPlacement() : attack.RequestWeaponSkill());
                    message = "로컬 R 스킬 요청";
                    return true;
                case "owner_attack":
                    PlayerAttack normal = NetworkClient.localPlayer == null ? null
                        : NetworkClient.localPlayer.GetComponent<PlayerAttack>();
                    accepted = (normal != null) && normal.RequestAttack();
                    message = "로컬 공격 요청";
                    return true;
                case "test_spirit_empty":
                    if (NetworkServer.active && NetworkServer.spawned.TryGetValue(command.uintValue, out NetworkIdentity actor) &&
                        actor.TryGetComponent(out PlayerStatus status))
                    {
                        accepted = status.TrySpendSpirit(status.Spirit);
                    }
                    message = accepted ? "검증 대상 신력 소진" : "서버 대상 필요";
                    return true;
                case "sound_request":
                    NetworkIdentity source;
                    if (NetworkClient.spawned.TryGetValue(command.uintValue, out source))
                    {
                        int before = SfxPlayer.PlayedCount;
                        SfxPlayer.Play((SoundKey)command.index, source.transform.position, source.isOwned, source.netId);
                        accepted = SfxPlayer.PlayedCount > before;
                    }
                    message = accepted ? "효과음 재생" : "소유권 또는 재생 조건으로 거절";
                    return true;
                default:
                    return false;
            }
        }

        private static void CaptureChatDevState(VerificationSnapshot snapshot)
        {
            var entries = LocalChatLog.Entries;
            snapshot.chatMessages = new ChatVerificationMessage[entries.Count];
            for (int i = 0; i < entries.Count; ++i)
            {
                snapshot.chatMessages[i] = new ChatVerificationMessage
                {
                    channel = (int)entries[i].Channel,
                    sender = entries[i].Sender,
                    text = entries[i].Text,
                };
            }

            PartyChatPanel panel = PartyChatPanel.Instance;
            snapshot.chatTyping = (panel != null) && panel.IsTyping;
            snapshot.chatCollapsed = (panel != null) && panel.IsCollapsed;
            snapshot.chatTab = panel == null ? 0 : panel.ActiveTab;
            snapshot.gameplayInputAllowed = LocalGameplayInputGate.AllowsGameplayInput;
            snapshot.devPanelJson = DevPanelHost.CurrentSnapshotJson;
            snapshot.soundPlayedCount = SfxPlayer.PlayedCount;
            snapshot.lastSound = SfxPlayer.Last;
            RunManager run = RunManager.ServerInstance != null ? RunManager.ServerInstance : RunManager.LocalInstance;
            snapshot.runPaused = (run != null) && run.IsPaused;
            snapshot.runPauseReasons = run == null ? string.Empty : run.PauseReasons.ToString();
        }
    }
}
#endif
