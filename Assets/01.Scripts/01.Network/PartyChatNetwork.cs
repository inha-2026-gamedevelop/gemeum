// System
using System;
using System.Collections.Generic;
using System.Text;

// Unity
using UnityEngine;

// Third party
using Mirror;

// Project
using Geumeum.Common;

namespace Geumeum.Network
{
    // 요청에 이름/채널을 넣지 않는다. 서버가 연결 신원에서 이름을 정한다.
    public struct PartyChatRequest : NetworkMessage { public string Text; }
    public struct PartyChatBroadcast : NetworkMessage { public string Sender; public string Text; }
    public struct PartyChatNotice : NetworkMessage { public string Text; }

    /// <summary> Mirror 서버 세션 안에서만 전달하는 파티 채팅. 씬 오브젝트 수명과 분리한다 </summary>
    public static class PartyChatNetwork
    {
        public const int MAX_MESSAGE_LENGTH = 200;
        private const double SEND_INTERVAL = 1d;
        private static readonly Dictionary<NetworkConnectionToClient, RateState> _rates =
            new Dictionary<NetworkConnectionToClient, RateState>();
        private static double _lastLocalSend = double.NegativeInfinity;

        private sealed class RateState
        {
            public double LastSend = double.NegativeInfinity;
            public double LastNotice = double.NegativeInfinity;
        }

        public static bool Send(string text)
        {
            if (!TryNormalize(text, out string normalized, out string error))
            {
                LocalChatLog.AddSystem(error);
                return false;
            }
            if (!NetworkClient.isConnected || NetworkClient.connection == null ||
                !NetworkClient.connection.isAuthenticated)
            {
                LocalChatLog.AddSystem("파티에 연결된 후 채팅을 보낼 수 있습니다.");
                return false;
            }
            double now = Time.realtimeSinceStartupAsDouble;
            if (now - _lastLocalSend < SEND_INTERVAL)
            {
                LocalChatLog.AddSystem("채팅은 1초에 한 번 보낼 수 있습니다.");
                return false;
            }
            _lastLocalSend = now;
            NetworkClient.Send(new PartyChatRequest { Text = normalized });
            // Host도 동일한 서버 응답 한 번만 표시한다.
            return true;
        }

        internal static void StartServer()
        {
            _rates.Clear();
            NetworkServer.RegisterHandler<PartyChatRequest>(OnServerRequest);
        }

        internal static void StopServer()
        {
            NetworkServer.UnregisterHandler<PartyChatRequest>();
            _rates.Clear();
        }

        internal static void Forget(NetworkConnectionToClient connection) { _rates.Remove(connection); }

        internal static void StartClient()
        {
            ClearClientHistory();
            NetworkClient.RegisterHandler<PartyChatBroadcast>(message =>
                LocalChatLog.AddParty(message.Sender, message.Text));
            NetworkClient.RegisterHandler<PartyChatNotice>(message => LocalChatLog.AddSystem(message.Text));
        }

        internal static void StopClient()
        {
            NetworkClient.UnregisterHandler<PartyChatBroadcast>();
            NetworkClient.UnregisterHandler<PartyChatNotice>();
            ClearClientHistory();
        }

        internal static void ClearClientHistory()
        {
            _lastLocalSend = double.NegativeInfinity;
            LocalChatLog.Clear();
        }

        private static void OnServerRequest(NetworkConnectionToClient sender, PartyChatRequest message)
        {
            if (!IsSessionMember(sender)) { return; }
            if (!_rates.TryGetValue(sender, out RateState rate))
            {
                rate = new RateState();
                _rates.Add(sender, rate);
            }
            double now = Time.realtimeSinceStartupAsDouble;
            // 잘못된 요청도 간격을 소비해야 변조 클라이언트가 서버를 계속 호출할 수 없다.
            if (now - rate.LastSend < SEND_INTERVAL)
            {
                SendNotice(sender, rate, now, "채팅은 1초에 한 번 보낼 수 있습니다.");
                return;
            }
            rate.LastSend = now;
            if (!TryNormalize(message.Text, out string text, out string error))
            {
                SendNotice(sender, rate, now, error);
                return;
            }
            var broadcast = new PartyChatBroadcast { Sender = ResolveSender(sender), Text = text };
            foreach (NetworkConnectionToClient recipient in NetworkServer.connections.Values)
            {
                if (IsSessionMember(recipient)) { recipient.Send(broadcast); }
            }
        }

        internal static bool IsSessionMember(NetworkConnectionToClient connection)
        {
            return connection != null && connection.isAuthenticated &&
                NetworkServer.connections.TryGetValue(connection.connectionId, out NetworkConnectionToClient current) &&
                ReferenceEquals(current, connection);
        }

        private static void SendNotice(NetworkConnectionToClient connection, RateState rate, double now, string text)
        {
            if (now - rate.LastNotice < SEND_INTERVAL) { return; }
            rate.LastNotice = now;
            connection.Send(new PartyChatNotice { Text = text });
        }

        private static string ResolveSender(NetworkConnectionToClient connection)
        {
            if (AccountGameServer.Enabled && AccountGameServer.Instance != null &&
                AccountGameServer.Instance.TryGetNickname(connection.connectionId, out string nickname) &&
                TryNormalizeName(nickname, out string accountName)) { return accountName; }
            if (connection.identity != null && connection.identity.TryGetComponent(out IDisplayNamed named) &&
                TryNormalizeName(named.DisplayName, out string name)) { return name; }
            foreach (NetworkIdentity identity in connection.owned)
            {
                if (identity != null && identity.TryGetComponent(out IDisplayNamed ownedName) &&
                    TryNormalizeName(ownedName.DisplayName, out name)) { return name; }
            }
            return $"동료 {connection.connectionId}";
        }

        private static bool TryNormalizeName(string input, out string name)
        {
            name = null;
            if (string.IsNullOrWhiteSpace(input)) { return false; }
            if (input.Length > 64) { input = input.Substring(0, 64); }
            return TryNormalize(input, out name, out _);
        }

        /// <summary> UTF-16 경계와 길이를 검증하고 줄바꿈/공백/태그를 한 줄 평문으로 만든다 </summary>
        internal static bool TryNormalize(string input, out string text, out string error)
        {
            text = null;
            error = "채팅 내용을 입력해 주세요.";
            if (string.IsNullOrWhiteSpace(input)) { return false; }
            if (input.Length > MAX_MESSAGE_LENGTH)
            {
                error = $"채팅은 {MAX_MESSAGE_LENGTH}자까지 보낼 수 있습니다.";
                return false;
            }
            var buffer = new StringBuilder(input.Length);
            bool pendingSpace = false;
            for (int i = 0; i < input.Length; ++i)
            {
                char c = input[i];
                if (char.IsWhiteSpace(c)) { pendingSpace = buffer.Length > 0; continue; }
                if (char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format ||
                    char.IsLowSurrogate(c) || (char.IsHighSurrogate(c) &&
                    (i + 1 >= input.Length || !char.IsLowSurrogate(input[i + 1]))))
                {
                    error = "채팅에 사용할 수 없는 문자가 포함되어 있습니다.";
                    return false;
                }
                if (pendingSpace) { buffer.Append(' '); pendingSpace = false; }
                buffer.Append(c == '<' ? '＜' : c == '>' ? '＞' : c);
                if (char.IsHighSurrogate(c)) { buffer.Append(input[++i]); }
            }
            text = buffer.ToString();
            return text.Length > 0;
        }
    }
}
