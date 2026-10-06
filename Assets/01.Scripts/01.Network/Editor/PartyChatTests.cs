using System;
using System.Collections.Generic;
using System.Reflection;
using Mirror;
using NUnit.Framework;
using UnityEngine;
using Geumeum.Common;
using Geumeum.Combat;

namespace Geumeum.Network.EditorTests
{
    public class PartyChatTests
    {
        private readonly List<NetworkMessage> _sent = new List<NetworkMessage>();
        private Dictionary<int, NetworkConnectionToClient> _previousConnections;
        private Transport _previousTransport;
        private GameObject _transportObject;

        [SetUp]
        public void SetUp()
        {
            LocalChatLog.Clear();
            _sent.Clear();
            _previousConnections = new Dictionary<int, NetworkConnectionToClient>(NetworkServer.connections);
            NetworkServer.connections.Clear();
            _previousTransport = Transport.active;
            _transportObject = new GameObject("ChatTestTransport", typeof(PartyChatTestTransport));
            Transport.active = _transportObject.GetComponent<Transport>();
            Rates.Clear();
            NetworkDiagnostics.OutMessageEvent += Capture;
        }

        [TearDown]
        public void TearDown()
        {
            NetworkDiagnostics.OutMessageEvent -= Capture;
            NetworkServer.connections.Clear();
            foreach (var pair in _previousConnections) { NetworkServer.connections.Add(pair.Key, pair.Value); }
            Rates.Clear();
            Transport.active = _previousTransport;
            UnityEngine.Object.DestroyImmediate(_transportObject);
            LocalChatLog.Clear();
        }

        [TestCase("  가나다\r\n  ABC\t DEF  ", "가나다 ABC DEF")]
        [TestCase("<color=red>안녕</color>", "＜color=red＞안녕＜/color＞")]
        [TestCase("안녕 😀", "안녕 😀")]
        public void NormalizationKeepsOnePlainTextLine(string input, string expected)
        {
            Assert.IsTrue(Normalize(input, out string output));
            Assert.AreEqual(expected, output);
        }

        [TestCase("")]
        [TestCase(" \n\t ")]
        [TestCase("bad\u0000")]
        [TestCase("spoof\u202e")]
        public void InvalidUtfAndControlTextIsRejected(string input)
        {
            Assert.IsFalse(Normalize(input, out _));
        }

        // NUnit 속성 문자열은 메타데이터/XML을 거치면서 단독 surrogate가 대체문자로 바뀐다.
        // 코드 단위의 숫자값을 받아 테스트 본문에서 원래 입력을 만들어야 유효성 검사를 실제로 탄다.
        [TestCase(0xD800)]
        [TestCase(0xDC00)]
        public void UnpairedSurrogateConstructedAtRuntimeIsRejected(int codeUnit)
        {
            string input = new string((char)codeUnit, 1);
            Assert.AreEqual(codeUnit, (int)input[0]);
            Assert.IsFalse(Normalize(input, out _));
        }

        [Test]
        public void LengthLimitChecksRawUtf16BeforeTrimming()
        {
            Assert.IsTrue(Normalize(new string('가', PartyChatNetwork.MAX_MESSAGE_LENGTH), out _));
            Assert.IsFalse(Normalize(new string('가', PartyChatNetwork.MAX_MESSAGE_LENGTH + 1), out _));
            Assert.IsFalse(Normalize(new string(' ', PartyChatNetwork.MAX_MESSAGE_LENGTH) + "a", out _));
        }

        [Test]
        public void ClientRequestCannotChooseSenderOrChannel()
        {
            FieldInfo[] fields = typeof(PartyChatRequest).GetFields(BindingFlags.Public | BindingFlags.Instance);
            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual("Text", fields[0].Name);
        }

        [Test]
        public void BroadcastReachesThreeAuthenticatedSessionMembersOnce()
        {
            var sender = AddConnection(1, true);
            AddConnection(2, true);
            AddConnection(3, true);
            AddConnection(4, false);
            Request(sender, "안녕하세요");
            Assert.AreEqual(3, _sent.Count);
            foreach (NetworkMessage message in _sent)
            {
                Assert.IsInstanceOf<PartyChatBroadcast>(message);
                var chat = (PartyChatBroadcast)message;
                Assert.AreEqual("동료 1", chat.Sender);
                Assert.AreEqual("안녕하세요", chat.Text);
            }
            Assert.AreEqual(0, LocalChatLog.Entries.Count, "서버는 Host 로그를 직접 추가하지 않는다.");
        }

        [Test]
        public void StaleConnectionWithReusedIdCannotSend()
        {
            var old = AddConnection(1, true);
            NetworkServer.connections[1] = new NetworkConnectionToClient(1) { isAuthenticated = true };
            Request(old, "이전 방 입력");
            Assert.AreEqual(0, _sent.Count);
        }

        [Test]
        public void UnauthenticatedSenderCannotSend()
        {
            Request(AddConnection(1, false), "인증 전 입력");
            Assert.AreEqual(0, _sent.Count);
        }

        [Test]
        public void ServerEnforcesRateLimitAndBoundsRejectionNotice()
        {
            var sender = AddConnection(1, true);
            Request(sender, "첫 입력");
            Request(sender, "다음 입력");
            Request(sender, "반복 입력");
            Assert.AreEqual(2, _sent.Count);
            Assert.IsInstanceOf<PartyChatBroadcast>(_sent[0]);
            Assert.IsInstanceOf<PartyChatNotice>(_sent[1]);
        }

        [Test]
        public void InvalidMessageNeverBroadcastsAndConsumesRateWindow()
        {
            var sender = AddConnection(1, true);
            AddConnection(2, true);
            Request(sender, new string('a', PartyChatNetwork.MAX_MESSAGE_LENGTH + 1));
            Request(sender, "즉시 재시도");
            Assert.AreEqual(1, _sent.Count);
            Assert.IsInstanceOf<PartyChatNotice>(_sent[0]);
        }

        [Test]
        public void LogEvictsOldestAndExposesReadOnlyView()
        {
            for (int i = 0; i < 150; ++i) { LocalChatLog.AddParty("동료", i.ToString()); }
            Assert.AreEqual(100, LocalChatLog.Entries.Count);
            Assert.AreEqual("50", LocalChatLog.Entries[0].Text);
            Assert.IsTrue(((IList<ChatMessage>)LocalChatLog.Entries).IsReadOnly);
        }

        [Test]
        public void SameSystemErrorIsSuppressedAcrossInterleavedMessages()
        {
            LocalChatLog.AddSystem("신력이 부족합니다");
            LocalChatLog.AddParty("동료", "안녕");
            LocalChatLog.AddSystem("스태미나가 부족합니다");
            LocalChatLog.AddSystem("신력이 부족합니다");
            Assert.AreEqual(3, LocalChatLog.Entries.Count);
        }

        [Test]
        public void ClearRemovesHistoryAndResetsSystemSuppression()
        {
            LocalChatLog.AddSystem("신력이 부족합니다");
            Invoke("ClearClientHistory");
            Assert.AreEqual(0, LocalChatLog.Entries.Count);
            LocalChatLog.AddSystem("신력이 부족합니다");
            Assert.AreEqual(1, LocalChatLog.Entries.Count);
        }

        [Test]
        public void FailedLocalSendExplainsMissingConnection()
        {
            if (NetworkClient.isConnected) { Assert.Ignore("이미 연결된 Editor에서는 로컬 오프라인 검사를 생략한다."); }
            Assert.IsFalse(PartyChatNetwork.Send("안녕"));
            Assert.AreEqual(ChatChannel.System, LocalChatLog.Entries[0].Channel);
        }

        [Test]
        public void FailureMessagesDistinguishSpiritStaminaCooldownAndPlacement()
        {
            Assert.AreEqual("신력이 부족합니다", ActionRejectionChat.GetText(ActionRejection.Resource, true));
            Assert.AreEqual("스태미나가 부족합니다", ActionRejectionChat.GetText(ActionRejection.Resource));
            Assert.AreEqual("아직 재사용 대기시간입니다.", ActionRejectionChat.GetText(ActionRejection.Busy, cooldown: true));
            Assert.AreEqual("이 위치에는 설치할 수 없습니다.", ActionRejectionChat.GetText(ActionRejection.InvalidInput, placement: true));
            ActionRejectionChat.Show(ActionRejection.None);
            ActionRejectionChat.Show(ActionRejection.Duplicate);
            Assert.AreEqual(0, LocalChatLog.Entries.Count);
        }

        private static System.Collections.IDictionary Rates => (System.Collections.IDictionary)
            typeof(PartyChatNetwork).GetField("_rates", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        [Test]
        public void ParryResourceFailureUsesStaminaAndInternalWindowFailuresStayQuiet()
        {
            ActionRejectionChat.ShowParry(ParryResultCode.RejectedDuplicate);
            ActionRejectionChat.ShowParry(ParryResultCode.RejectedWindowMismatch);
            ActionRejectionChat.ShowParry(ParryResultCode.RejectedResolutionClosed);
            Assert.AreEqual(0, LocalChatLog.Entries.Count);
            ActionRejectionChat.ShowParry(ParryResultCode.RejectedStamina);
            Assert.AreEqual("스태미나가 부족합니다", LocalChatLog.Entries[0].Text);
        }

        private static bool Normalize(string input, out string text)
        {
            object[] arguments = { input, null, null };
            bool result = (bool)Invoke("TryNormalize", arguments);
            text = (string)arguments[1];
            return result;
        }

        private static object Invoke(string method, params object[] arguments) =>
            typeof(PartyChatNetwork).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, arguments);

        private static void Request(NetworkConnectionToClient sender, string text) =>
            Invoke("OnServerRequest", sender, new PartyChatRequest { Text = text });

        private static NetworkConnectionToClient AddConnection(int id, bool authenticated)
        {
            var connection = new NetworkConnectionToClient(id) { isAuthenticated = authenticated };
            NetworkServer.connections.Add(id, connection);
            return connection;
        }

        private void Capture(NetworkDiagnostics.MessageInfo info) { _sent.Add(info.message); }
    }

    /// <summary> EditMode에서는 KCP Awake가 실행되지 않아 config가 없다. 크기만 제공하는 무소켓 전송부 </summary>
    public sealed class PartyChatTestTransport : Transport
    {
        public override bool Available() => true;
        public override bool ClientConnected() => false;
        public override void ClientConnect(string address) { }
        public override void ClientSend(ArraySegment<byte> segment, int channelId = Channels.Reliable) { }
        public override void ClientDisconnect() { }
        public override Uri ServerUri() => new Uri("memory://localhost");
        public override bool ServerActive() => false;
        public override void ServerStart() { }
        public override void ServerSend(int connectionId, ArraySegment<byte> segment, int channelId = Channels.Reliable) { }
        public override void ServerDisconnect(int connectionId) { }
        public override string ServerGetClientAddress(int connectionId) => "test";
        public override void ServerStop() { }
        public override int GetMaxPacketSize(int channelId = Channels.Reliable) => 4096;
        public override void Shutdown() { }
    }
}
