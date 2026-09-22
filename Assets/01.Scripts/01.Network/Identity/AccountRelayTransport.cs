using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using kcp2k;
using Mirror;
using Mirror.BouncyCastle.Crypto;
using Mirror.BouncyCastle.Crypto.Modes;
using Mirror.BouncyCastle.Crypto.Parameters;
using UnityEngine;

namespace Geumeum.Network
{
    [Serializable]
    public sealed class AccountRelayCredentials
    {
        public bool enabled;
        public string address;
        public int port;
        public string peer_id;
        public string key;
    }

    /// <summary>Outbound-only encrypted UDP relay; KCP and host gameplay authority are preserved</summary>
    public sealed class AccountRelayTransport : KcpTransport
    {
        private AccountRelayCredentials _credentials;

        public static AccountRelayTransport Create(NetworkManager manager, AccountRelayCredentials credentials)
        {
            if (credentials == null || !credentials.enabled)
            {
                throw new ArgumentException();
            }

            var owner = new GameObject("AccountRelayTransport");
            owner.transform.SetParent(manager.transform, false);
            var transport = owner.AddComponent<AccountRelayTransport>();
            transport._credentials = credentials;
            manager.transport = transport;
            Transport.active = transport;
            return transport;
        }

        protected override void Awake()
        {
            config = new KcpConfig(false, RecvBufferSize, SendBufferSize, 1200, NoDelay,
                Interval, FastResend, false, SendWindowSize, ReceiveWindowSize, Timeout, MaxRetransmit);
            client = new RelayClient(this, config);
            server = new RelayServer(this, config);
        }

        /// <summary>
        /// 자리마다 릴레이가 붙인 번호의 최대값을 들고, 자리 재발급 통지를 적용할지 정한다.
        ///
        /// 릴레이는 호스트로 보내는 모든 패킷에 증가하는 번호를 붙이고, 통지는 돌아온 peer가
        /// 입을 여는 순간 멈춘다. 그래서 돌아온 클라이언트의 첫 패킷은 마지막 통지보다 반드시
        /// 뒤 번호다. 그 자리에서 통지보다 뒤 번호를 이미 봤다면 새 연결이 이미 서 있다는
        /// 뜻이므로 통지를 흘려보낸다 - 늦게 도착하거나 중복된 사본이 새 연결을 끊지 못한다.
        ///
        /// 기준은 오르기만 한다. UDP는 재정렬되므로 늦게 도착한 낮은 번호가 기준을 끌어내리면,
        /// 그 사이 번호의 묵은 통지가 방금 선 연결을 끊어 버린다. 통지가 유실되면 아무 일도
        /// 일어나지 않고 예전처럼 transport timeout을 기다린다
        /// </summary>
        public sealed class SeatSequences
        {
            private readonly Dictionary<int, ulong> _highest = new Dictionary<int, ulong>();

            /// <summary> 그 자리에서 실제로 받은 데이터의 번호를 기록한다 </summary>
            public void Observe(int seat, ulong sequence)
            {
                if ((!_highest.TryGetValue(seat, out ulong highest)) || (sequence > highest))
                {
                    _highest[seat] = sequence;
                }
            }

            /// <summary> 이 자리에서 마지막으로 인정한 번호. 판정 근거를 밖에서 볼 때 쓴다 </summary>
            public ulong Highest(int seat)
            {
                return _highest.TryGetValue(seat, out ulong highest) ? highest : 0UL;
            }

            /// <summary> 통지를 적용할지 정한다. 적용하면 기준을 그 번호까지 올린다 </summary>
            public bool TryRelease(ArraySegment<byte> notice, ulong sequence, out int seat)
            {
                seat = 0;
                if (notice.Count != 4)
                {
                    return false;
                }

                seat = BitConverter.ToInt32(notice.Array, notice.Offset);
                if ((seat < 1) || (seat > 2) || (sequence <= Highest(seat)))
                {
                    return false;
                }

                _highest[seat] = sequence;
                return true;
            }

            public void Clear()
            {
                _highest.Clear();
            }
        }

        public override void ClientConnect(string address) => ((RelayClient)client).Open(_credentials);
        public override void ClientConnect(Uri uri) => ClientConnect(uri.Host);
        public override void ServerStart() => ((RelayServer)server).Open(_credentials);
        public override string ServerGetClientAddress(int connectionId) => "relay";
        public bool RelayReady => ((RelayServer)server).Ready;
        public override void Shutdown() { client.Disconnect(); server.Stop(); }
        private void OnDestroy() => Shutdown();

        private sealed class RelayClient : KcpClient
        {
            private RelaySocket _relay;
            public RelayClient(AccountRelayTransport owner, KcpConfig config) : base(
                () => owner.OnClientConnected?.Invoke(),
                (data, channel) => owner.OnClientDataReceived?.Invoke(data, FromKcpChannel(channel)),
                () => owner.OnClientDisconnected?.Invoke(),
                (error, reason) => owner.OnClientError?.Invoke(ToTransportError(error), reason), config) { }

            public void Open(AccountRelayCredentials credentials)
            {
                _relay = new RelaySocket(credentials);
                Connect(credentials.address, (ushort)credentials.port);
            }
            protected override void RawSend(ArraySegment<byte> data) => _relay?.Send(0, data);
            protected override bool RawReceive(out ArraySegment<byte> data)
            {
                data = default;
                // 자리 재발급 통지는 호스트만 쓴다. 클라이언트는 본문 있는 것만 KCP로 넘긴다
                while (_relay != null && _relay.Receive(out data, out _))
                {
                    if (data.Count > 0)
                    {
                        return true;
                    }
                }
                return false;
            }
            public override void TickOutgoing() { _relay?.Ping(); base.TickOutgoing(); }
            protected override void OnDisconnected()
            {
                base.OnDisconnected();
                _relay?.Dispose();
                _relay = null;
            }
        }

        private sealed class RelayServer : KcpServer
        {
            private RelaySocket _relay;

            /// <summary> 자리마다 인정한 릴레이 번호. 통지 순서 판정에만 쓴다 </summary>
            private readonly SeatSequences _seats = new SeatSequences();

            /// <summary> 지금 놓아주는 중인 자리. 그 자리로 나가는 작별 인사를 막는다 </summary>
            private int _releasing;
            public bool Ready => _relay != null && _relay.Ready;
            public RelayServer(AccountRelayTransport owner, KcpConfig config) : base(
                (id, endpoint) => owner.OnServerConnectedWithAddress?.Invoke(id, "relay"),
                (id, data, channel) => owner.OnServerDataReceived?.Invoke(id, data, FromKcpChannel(channel)),
                id => owner.OnServerDisconnected?.Invoke(id),
                (id, error, reason) => owner.OnServerError?.Invoke(id, ToTransportError(error), reason), config) { }

            public void Open(AccountRelayCredentials credentials) { _relay = new RelaySocket(credentials); _relay.Ping(); }
            public override bool IsActive() => _relay != null;
            protected override void RawSend(int id, ArraySegment<byte> data)
            {
                // 놓아주는 자리로 나가는 것은 낡은 연결의 작별 인사다. 그 자리는 이미 주인이
                // 바뀌었으므로, 내보내면 방금 돌아온 사람이 붙자마자 끊긴다
                if (id == _releasing)
                {
                    return;
                }

                _relay?.Send(id, data);
            }
            protected override bool RawReceiveFrom(out ArraySegment<byte> data, out int id)
            {
                data = default; id = 0;
                while (_relay != null && _relay.Receive(out data, out id))
                {
                    if (id != 0)
                    {
                        _seats.Observe(id, _relay.LastSequence);
                        return true;
                    }

                    // source 0은 릴레이 자신이다. 손님은 흉내 낼 수 없다. 같은 번호로 남아 있는
                    // 낡은 연결을 지금 끊지 않으면, 돌아온 클라이언트의 handshake가 이전 cookie에
                    // 막혀 버려지고 timeout까지 기다린 뒤에야 겨우 붙는다
                    if (_seats.TryRelease(data, _relay.LastSequence, out int seat))
                    {
                        _releasing = seat;
                        // kcp2k는 작별 인사를 이 자리에서 곧바로 내보낸다. 그 사이만 막는다
                        try { Disconnect(seat); }
                        finally { _releasing = 0; }
                    }
                }
                return false;
            }

            public override void TickOutgoing() { _relay?.Ping(); base.TickOutgoing(); }
            public override void Stop() { base.Stop(); _seats.Clear(); _relay?.Dispose(); _relay = null; }
        }

        // One instance per one-use credential; never reset sequence while retaining a key
        private sealed class RelaySocket : IDisposable
        {
            private readonly Socket _socket;
            private readonly byte[] _identity;
            private readonly KeyParameter _key;
            private readonly GcmBlockCipher _cipher = new GcmBlockCipher(AesUtilities.CreateEngine());
            private readonly byte[] _receive = new byte[1249];
            private readonly ulong[] _seen = new ulong[1024];
            private ulong _sent;
            private ulong _maximum;
            private double _lastPing = -1;
            public bool Ready { get; private set; }

            /// <summary> 마지막으로 통과시킨 패킷의 릴레이 번호. 릴레이가 보낸 순서를 그대로 담는다 </summary>
            public ulong LastSequence { get; private set; }

            public RelaySocket(AccountRelayCredentials credentials)
            {
                if (!IPAddress.TryParse(credentials.address, out IPAddress address)
                    || credentials.port < 1024 || credentials.port > 65535)
                {
                    throw new ArgumentException();
                }

                _identity = Hex(credentials.peer_id, 16);
                _key = new KeyParameter(Hex(credentials.key, 32));
                _socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                _socket.Connect(new IPEndPoint(address, credentials.port));
                _socket.Blocking = false;
                kcp2k.Common.ConfigureSocketBuffers(_socket, 1024 * 1024, 1024 * 1024);
            }

            private static byte[] Hex(string value, int count)
            {
                if (value == null || value.Length != count * 2)
                {
                    throw new ArgumentException();
                }

                var bytes = new byte[count];
                for (int i = 0; i < count; ++i)
                {
                    bytes[i] = Convert.ToByte(value.Substring(i * 2, 2), 16);
                }

                return bytes;
            }

            public void Send(int route, ArraySegment<byte> data)
            {
                if (data.Count > 1200 || _sent == ulong.MaxValue)
                {
                    throw new InvalidOperationException();
                }

                var header = new byte[28];
                header[0] = 71; header[1] = 82; header[2] = 48; header[3] = 49;
                Buffer.BlockCopy(_identity, 0, header, 4, 16);
                Buffer.BlockCopy(BitConverter.GetBytes(++_sent), 0, header, 20, 8);
                var nonce = new byte[12] { 85, 80, 48, 49, 0, 0, 0, 0, 0, 0, 0, 0 };
                Buffer.BlockCopy(header, 20, nonce, 4, 8);
                var plain = new byte[data.Count + 4];
                Buffer.BlockCopy(BitConverter.GetBytes(route), 0, plain, 0, 4);
                if (data.Count > 0)
                {
                    Buffer.BlockCopy(data.Array, data.Offset, plain, 4, data.Count);
                }

                var packet = new byte[plain.Length + 44];
                Buffer.BlockCopy(header, 0, packet, 0, 28);
                _cipher.Init(true, new AeadParameters(_key, 128, nonce, header));
                int length = _cipher.ProcessBytes(plain, 0, plain.Length, packet, 28);
                _cipher.DoFinal(packet, 28 + length);
                try { _socket.SendNonBlocking(new ArraySegment<byte>(packet)); }
                catch (SocketException) { /* KCP retries and reports its bounded timeout. */ }
            }

            public bool Receive(out ArraySegment<byte> data, out int route)
            {
                data = default; route = 0;
                for (int attempt = 0; attempt < 256; ++attempt)
                {
                    ArraySegment<byte> packet;
                    try { if (!_socket.ReceiveNonBlocking(_receive, out packet))
                        {
                            return false;
                        }
                    }
                    catch (SocketException) { return false; }
                    if (packet.Count < 48 || packet.Count > 1248 || _receive[0] != 71 || _receive[1] != 82
                        || _receive[2] != 48 || _receive[3] != 49)
                    {
                        continue;
                    }

                    bool identityMatches = true;
                    for (int i = 0; i < 16; ++i)
                    {
                        identityMatches &= _identity[i] == _receive[i + 4];
                    }

                    if (!identityMatches)
                    {
                        continue;
                    }

                    ulong sequence = BitConverter.ToUInt64(_receive, 20);
                    if (sequence == 0 || (_maximum >= 1024 && sequence <= _maximum - 1024)
                        || _seen[sequence % 1024] == sequence)
                    {
                        continue;
                    }

                    var header = new byte[28];
                    Buffer.BlockCopy(_receive, 0, header, 0, 28);
                    var nonce = new byte[12] { 68, 78, 48, 49, 0, 0, 0, 0, 0, 0, 0, 0 };
                    Buffer.BlockCopy(header, 20, nonce, 4, 8);
                    var plain = new byte[packet.Count - 44];
                    try
                    {
                        _cipher.Init(false, new AeadParameters(_key, 128, nonce, header));
                        int length = _cipher.ProcessBytes(_receive, 28, packet.Count - 28, plain, 0);
                        _cipher.DoFinal(plain, length);
                    }
                    catch (InvalidCipherTextException) { continue; }
                    _maximum = Math.Max(_maximum, sequence);
                    _seen[sequence % 1024] = sequence;
                    Ready = true;
                    route = BitConverter.ToInt32(plain, 0);
                    // 본문이 없으면 유지 신호다. 나머지는 상대의 데이터이거나,
                    // source가 0이면 릴레이가 자리 재발급을 알려 온 통지다
                    if (route < 0 || route > 2 || plain.Length == 4)
                    {
                        continue;
                    }

                    LastSequence = sequence;
                    data = new ArraySegment<byte>(plain, 4, plain.Length - 4);
                    return true;
                }
                return false;
            }

            public void Ping()
            {
                if (NetworkTime.localTime - _lastPing < 0.5)
                {
                    return;
                }

                _lastPing = NetworkTime.localTime;
                Send(0, default);
            }
            public void Dispose() => _socket.Dispose();
        }
    }
}
