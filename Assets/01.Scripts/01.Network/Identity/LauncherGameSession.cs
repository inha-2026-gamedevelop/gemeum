using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Mirror;

namespace Geumeum.Network
{
    /// <summary> 런처 세션 수신/검증/갱신. 게임 서버의 신원 검증을 대신하지 않는다 </summary>
    public sealed class LauncherGameSession : MonoBehaviour
    {
        public enum SessionState { Disabled = 0, Loading = 1, Ready = 2, Failed = 3 }
        public static SessionState State { get; private set; }
        public static string AccountId { get; private set; }
        public static string Nickname { get; private set; }
        public static string RoomId { get; private set; }
        private static LauncherGameSession _instance;
        private readonly SemaphoreSlim _requests = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _lifetime;
        private HttpClient _http;
        private Envelope _session;
        private DateTimeOffset _accessExpires;
        private DateTimeOffset _sessionExpires;

        [Serializable] private sealed class Envelope
        {
            public int version;
            public string api_origin;
            public string access_token;
            public string refresh_token;
            public string account_id;
            public int expires_in;
            public string session_expires_at;
            public string room_id;
            public string address;
            public int port;
            public bool is_host;
        }
        [Serializable] private sealed class Profile { public string account_id; public string nickname; public string status; }
        [Serializable] private sealed class Tokens
        {
            public string access_token;
            public string refresh_token;
            public string session_kind;
            public string token_type;
            public int expires_in;
            public string session_expires_at;
            public Profile account;
        }
        [Serializable] private sealed class Refresh { public string refresh_token; }
        [Serializable] private sealed class TicketInput { public string challenge; public string build_id; }
        [Serializable] private sealed class TicketReply { public string ticket; }
        [Serializable] private sealed class HostReply { public string server_key; public int port; public AccountRelayCredentials relay; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState() { State = SessionState.Disabled; AccountId = null; Nickname = null; RoomId = null; _instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-account-pipe") < 0)
            {
                return;
            }

            State = SessionState.Loading;
            var owner = new GameObject("LauncherGameSession");
            DontDestroyOnLoad(owner);
            _instance = owner.AddComponent<LauncherGameSession>();
        }

        private async void Start()
        {
            _lifetime = new CancellationTokenSource();
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                string pipe = Value(args, "-account-pipe");
                if (!int.TryParse(Value(args, "-launcher-pid"), out int launcherPid))
                {
                    throw new IOException();
                }

                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(120));
                    string json = await GameSessionPipeClient.ReceiveAsync(pipe, launcherPid, deadline.Token);
                    _session = JsonUtility.FromJson<Envelope>(json);
                }
                if (_session == null || _session.version != 1 || !ValidToken(_session.access_token)
                    || !ValidToken(_session.refresh_token) || !ulong.TryParse(_session.account_id, out ulong id) || id == 0
                    || _session.expires_in < 1 || _session.expires_in > 3600
                    || !DateTimeOffset.TryParse(_session.session_expires_at, out _sessionExpires)
                    || _sessionExpires <= DateTimeOffset.UtcNow)
                {
                    throw new IOException();
                }

                Uri origin = ValidateOrigin(_session.api_origin, Debug.isDebugBuild);
                _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = !origin.IsLoopback })
                    { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(10) };
                _accessExpires = DateTimeOffset.UtcNow.AddSeconds(_session.expires_in);
                await VerifyAsync();
                if (!string.IsNullOrEmpty(_session.room_id))
                {
                    if (!Guid.TryParse(_session.room_id, out _) || !System.Net.IPAddress.TryParse(_session.address, out _)
                        || _session.port < 1024 || _session.port > 65535)
                    {
                        throw new IOException();
                    }

                    RoomId = _session.room_id;
                }
                State = SessionState.Ready;
                Debug.Log("[LauncherGameSession] 계정 세션 확인: accountId=" + AccountId);
                if (RoomId != null)
                {
                    var manager = NetworkManager.singleton;
                    var transport = manager == null ? null : manager.GetComponent<kcp2k.KcpTransport>();
                    if (transport == null)
                    {
                        throw new IOException();
                    }

                    manager.transport = transport;
                    transport.enabled = true;
                    transport.Port = (ushort)_session.port;
                    Transport.active = transport;
                    manager.networkAddress = _session.address;
                    if (_session.is_host)
                    {
                        HostReply host = JsonUtility.FromJson<HostReply>(await RequestAsync(HttpMethod.Post, "v1/rooms/" + RoomId + "/host", "{}"));
                        if (host == null || host.port != _session.port || !ValidToken(host.server_key))
                        {
                            throw new IOException();
                        }

                        AccountGameServer.StartListen(RoomId, host.server_key, host.port, origin, host.relay);
                    }
                    else
                    {
                        AccountRelayCredentials relay = JsonUtility.FromJson<AccountRelayCredentials>(
                            await RequestAsync(HttpMethod.Post, "v1/rooms/" + RoomId + "/relay", "{}"));
                        if (relay != null && relay.enabled)
                        {
                            AccountRelayTransport.Create(manager, relay);
                        }

                        manager.StartClient();
                    }
                }
                while (!_lifetime.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), _lifetime.Token);
                    if (RoomId != null && !NetworkClient.isConnected)
                    {
                        throw new IOException();
                    }

                    if (_sessionExpires <= DateTimeOffset.UtcNow)
                    {
                        throw new IOException();
                    }

                    await _requests.WaitAsync(_lifetime.Token);
                    try
                    {
                        if (_accessExpires <= DateTimeOffset.UtcNow.AddSeconds(40))
                        {
                            await RefreshAsync();
                        }

                        await VerifyAsync();
                    }
                    finally { _requests.Release(); }
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception)
            {
                // Neither exception text nor HTTP bodies are safe to log here
                State = SessionState.Failed;
                AccountId = null;
                Nickname = null;
                _session = null;
                Debug.LogError("[LauncherGameSession] 계정 세션을 확인할 수 없어 게임을 종료합니다. 런처에서 다시 로그인해 주세요.");
                if (NetworkClient.active)
                {
                    NetworkClient.Disconnect();
                }

                Application.Quit(12);
            }
        }

        private async Task VerifyAsync()
        {
            Profile profile = JsonUtility.FromJson<Profile>(await RequestAsync(HttpMethod.Get, "v1/me", null));
            if (profile == null || profile.account_id != _session.account_id || profile.status != "active"
                || string.IsNullOrWhiteSpace(profile.nickname))
            {
                throw new IOException();
            }

            AccountId = profile.account_id;
            Nickname = profile.nickname;
        }

        public static async Task<string> JoinTicketAsync(string room, string challenge)
        {
            if (_instance == null || State != SessionState.Ready || RoomId == null || room != RoomId
                || challenge == null || challenge.Length != 64)
            {
                throw new IOException();
            }

            foreach (char c in challenge)
            {
                if (!((c >= 'a' && c <= 'f') || (c >= '0' && c <= '9')))
                {
                    throw new IOException();
                }
            }

            var self = _instance;
            await self._requests.WaitAsync(self._lifetime.Token);
            try
            {
                if (self._accessExpires <= DateTimeOffset.UtcNow.AddSeconds(40))
                {
                    await self.RefreshAsync();
                }

                string body = JsonUtility.ToJson(new TicketInput { challenge = challenge, build_id = BuildIdentity.ReleaseId });
                TicketReply result = JsonUtility.FromJson<TicketReply>(await self.RequestAsync(HttpMethod.Post, "v1/rooms/" + room + "/tickets", body));
                if (result == null || !ValidToken(result.ticket))
                {
                    throw new IOException();
                }

                return result.ticket;
            }
            finally { self._requests.Release(); }
        }

        private async Task RefreshAsync()
        {
            string refresh = _session.refresh_token;
            _session.refresh_token = null; // never retry a one-use token after an ambiguous result
            string json = JsonUtility.ToJson(new Refresh { refresh_token = refresh });
            Tokens tokens = JsonUtility.FromJson<Tokens>(await RequestAsync(HttpMethod.Post, "v1/auth/refresh", json));
            if (tokens == null || tokens.session_kind != "game" || tokens.token_type != "Bearer"
                || !ValidToken(tokens.access_token) || !ValidToken(tokens.refresh_token)
                || tokens.expires_in < 1 || tokens.expires_in > 3600 || tokens.account == null
                || tokens.account.account_id != AccountId || tokens.account.status != "active"
                || !DateTimeOffset.TryParse(tokens.session_expires_at, out DateTimeOffset expiry)
                || expiry > _sessionExpires || expiry <= DateTimeOffset.UtcNow)
            {
                throw new IOException();
            }

            _session.access_token = tokens.access_token;
            _session.refresh_token = tokens.refresh_token;
            _accessExpires = DateTimeOffset.UtcNow.AddSeconds(tokens.expires_in);
            Debug.Log("[LauncherGameSession] 게임 세션 갱신 확인");
        }

        private async Task<string> RequestAsync(HttpMethod method, string path, string json)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
            using (var request = new HttpRequestMessage(method, path))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                if (json != null)
                {
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                if (path != "v1/auth/refresh")
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.access_token);
                }

                using (HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new IOException();
                    }

                    using (Stream stream = await response.Content.ReadAsStreamAsync())
                    using (var buffer = new MemoryStream())
                    {
                        byte[] chunk = new byte[4096];
                        int read;
                        while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, timeout.Token)) > 0)
                        {
                            if (buffer.Length + read > 32768)
                            {
                                throw new IOException();
                            }

                            buffer.Write(chunk, 0, read);
                        }
                        return Encoding.UTF8.GetString(buffer.ToArray());
                    }
                }
            }
        }

        public static Uri ValidateOrigin(string value, bool local)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) || uri.UserInfo.Length != 0
                || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/"
                || (uri.Scheme != "https" && !(local && uri.Scheme == "http"
                    && (uri.Host == "127.0.0.1" || uri.Host == "localhost" || uri.Host == "[::1]"))))
            {
                throw new IOException("Invalid account origin");
            }

            return uri;
        }

        public static bool ValidToken(string value)
        {
            if (value == null || value.Length < 40 || value.Length > 128)
            {
                return false;
            }

            foreach (char c in value)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-'))
                {
                    return false;
                }
            }

            return true;
        }

        private static string Value(string[] args, string key)
        {
            int index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private void OnDestroy()
        {
            _lifetime?.Cancel();
            _http?.Dispose();
            _session = null;
            State = SessionState.Failed;
            RoomId = null;
            _instance = null;
            AccountId = null;
            Nickname = null;
        }
    }
}
