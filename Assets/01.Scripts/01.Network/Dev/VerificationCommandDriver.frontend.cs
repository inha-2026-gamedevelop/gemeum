#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Geumeum.UI;

namespace Geumeum.Network
{
    /// <summary>
    /// 계정 첫 화면, 로비를 실제 EventSystem 레이캐스트와 submit 경로로 조작한다.
    /// account_* 명령과 달리 화면 표시, 가림, EventSystem 누락이 있으면 거절된다.
    /// </summary>
    public sealed partial class VerificationCommandDriver
    {
        // text 형식: front_click/front_probe/front_submit은 "조작ID", front_type은 "조작ID=값".
        private static bool ExecuteFrontendVerb(VerificationCommand command, out string message)
        {
            string text = command.text ?? string.Empty;
            string value = null;
            if (command.verb == "front_type")
            {
                int split = text.IndexOf('=');
                if (split <= 0)
                {
                    message = "front_type은 조작ID=값 형식이다";
                    return false;
                }
                value = text.Substring(split + 1);
                text = text.Substring(0, split);
            }

            Selectable target = FindFrontendControl(text);
            if (target == null)
            {
                message = "화면에 없는 조작 요소다: " + text;
                return false;
            }

            bool accepted;
            string failure;
            switch (command.verb)
            {
                case "front_click": accepted = UiPointerRoute.TryClick(target, out failure); break;
                case "front_probe": accepted = UiPointerRoute.TryProbe(target, out failure); break;
                case "front_submit": accepted = UiPointerRoute.TrySubmit(target, out failure); break;
                case "front_type":
                    if (!(target is TMP_InputField input))
                    {
                        message = "입력칸이 아니다: " + text;
                        return false;
                    }
                    accepted = UiPointerRoute.TryType(input, value, out failure);
                    break;
                default:
                    message = "허용되지 않은 화면 조작 명령이다";
                    return false;
            }
            message = accepted ? "EventSystem 경로로 전달했다: " + text : failure;
            return accepted;
        }

        private static Selectable FindFrontendControl(string id)
        {
            if (id.StartsWith("title.", StringComparison.Ordinal))
            {
                return AccountTitleMenu.Instance == null ? null : AccountTitleMenu.Instance.FindControl(id);
            }
            return AccountLobbyPanel.Instance == null ? null : AccountLobbyPanel.Instance.FindControl(id);
        }

        private static void CaptureFrontendInput(VerificationSnapshot snapshot)
        {
            snapshot.frontendActiveScene = SceneManager.GetActiveScene().name;
            EventSystem system = EventSystem.current;
            snapshot.frontendEventSystem = (system != null) && (system.isActiveAndEnabled) ? system.name : string.Empty;
            snapshot.frontendTitleState = AccountTitleMenu.Instance == null
                ? string.Empty : AccountTitleMenu.Instance.StateName;
            AccountLobbyPanel account = AccountLobbyPanel.Instance;
            snapshot.frontendVisible = (account != null) && (account.IsVisible);
            snapshot.frontendOwnInput = (account != null) && (account.FrontendInputActive);
        }
    }
}
#endif
