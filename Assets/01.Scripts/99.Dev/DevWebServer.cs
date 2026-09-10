// System
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Dev
{
    /// <summary>
    /// 개발 패널을 브라우저에 띄우기 위한 아주 작은 HTTP 서버.
    ///
    /// Unity 플레이어는 자기 프로세스에서 두 번째 OS 창을 열 수 없다.
    /// 그래서 게임이 로컬 주소를 하나 열고, 브라우저 창을 조작 창으로 쓴다 -
    /// 크기와 위치가 자유롭고, 3대로 테스트할 때 다른 기기에서 열어 볼 수도 있다.
    ///
    /// HttpListener 대신 TcpListener를 쓴다. HttpListener는 Windows에서 주소 등록(URL ACL)이
    /// 없으면 관리자 권한을 요구하는 경우가 있어 팀원 PC마다 다르게 실패한다
    /// </summary>
    public class DevWebServer : PersistentSingleton<DevWebServer>
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS

        /****************************************
        *              Constants
        ****************************************/

        private const string OBJECT_NAME = "@DevWebServer";

        private const int DEFAULT_PORT = 8787;
        private const int PORT_TRIES   = 10; // 한 PC에서 3인 테스트를 하면 포트가 겹친다

        private const int SOCKET_TIMEOUT_MS = 4000;

        private const int MAX_HEADER_BYTES = 8 * 1024;
        private const int MAX_BODY_BYTES   = 64 * 1024;

        private const string ARG_PORT = "-devpanelport"; // 뒤에 포트 번호가 온다
        private const string ARG_LAN  = "-devpanellan";  // 다른 기기에서도 접속할 수 있게 연다

        /****************************************
        *                Fields
        ****************************************/

        private TcpListener _listener = null;
        private Thread      _thread   = null;

        private volatile bool _isRunning = false;

        private int _port = DEFAULT_PORT;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 브라우저로 열 주소. 서버가 안 떠 있으면 빈 문자열 </summary>
        public static string Url { get; private set; } = string.Empty;

        /****************************************
        *                Preload
        ****************************************/

        /// <summary>
        /// 씬에 배치하지 않아도 실행 시작에 스스로 뜬다.
        /// 잠겨 있는 동안에는 만들지 않는다 - 이 서버는 게임을 조작할 수 있는 창구라
        /// 치트 모드를 켜기 전에는 포트를 열어 두면 안 된다
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Preload()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            DevAccess.RunWhenUnlocked(() => EnsureCreated(OBJECT_NAME));
        }

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnSingletonAwake()
        {
            bool isLanOpen = HasArg(ARG_LAN);

            // 기본은 이 PC만. 개발 패널은 게임을 조작할 수 있어 아무 기기에나 열어 둘 것이 아니다
            IPAddress address = isLanOpen ? IPAddress.Any : IPAddress.Loopback;

            int wanted = ResolvePort();

            for (int i = 0; i < PORT_TRIES; ++i)
            {
                if (TryListen(address, wanted + i))
                {
                    break;
                }
            }

            if (!_isRunning)
            {
                Debug.LogWarning($"[DevWebServer] 포트를 열지 못했다: {wanted}~{wanted + PORT_TRIES - 1}", this);
                return;
            }

            Url = $"http://{(isLanOpen ? GetLocalAddress() : "127.0.0.1")}:{_port}/";

            Debug.Log($"[DevWebServer] 개발 패널을 브라우저에서 연다: {Url}", this);
        }

        /// <summary> 종료와 파괴 모두 여기로 온다. 스레드를 남기면 에디터가 붙잡힌 채로 남는다 </summary>
        protected override void OnSingletonDestroy()
        {
            Stop();
        }

        /****************************************
        *                Methods
        ****************************************/

        private bool TryListen(IPAddress address, int port)
        {
            try
            {
                _listener = new TcpListener(address, port);
                _listener.Start();

                _port      = port;
                _isRunning = true;

                // 배경 스레드로 받는다. 메인 스레드에서 받으면 접속을 기다리는 동안 게임이 멈춘다
                _thread = new Thread(Serve)
                {
                    IsBackground = true,
                    Name         = "DevWebServer",
                };

                _thread.Start();
                return true;
            }
            catch (SocketException)
            {
                // 이미 쓰는 포트다. 다음 번호로 넘어간다
                _listener = null;
                return false;
            }
        }

        private void Stop()
        {
            _isRunning = false;

            if (_listener != null)
            {
                // Stop이 대기 중인 AcceptTcpClient를 깨워 스레드가 빠져나온다
                _listener.Stop();
                _listener = null;
            }

            if (_thread != null)
            {
                _thread.Join(SOCKET_TIMEOUT_MS);
                _thread = null;
            }

            Url = string.Empty;
        }

        /// <summary> 배경 스레드. 여기서 Unity API를 부르면 그 자리에서 예외가 난다 </summary>
        private void Serve()
        {
            while (_isRunning)
            {
                TcpClient client = null;

                try
                {
                    client = _listener.AcceptTcpClient();

                    client.ReceiveTimeout = SOCKET_TIMEOUT_MS;
                    client.SendTimeout    = SOCKET_TIMEOUT_MS;

                    Handle(client);
                }
                catch (Exception)
                {
                    // 접속이 끊기거나 서버를 닫는 중이다. 개발 도구가 로그로 콘솔을 덮지 않게 조용히 넘긴다
                }
                finally
                {
                    if (client != null)
                    {
                        client.Close();
                    }
                }
            }
        }

        private static void Handle(TcpClient client)
        {
            using (NetworkStream stream = client.GetStream())
            {
                if (!TryReadRequest(stream, out string method, out string path, out string body))
                {
                    return;
                }

                if ((method == "GET") && ((path == "/") || (path == "/index.html")))
                {
                    Respond(stream, "200 OK", "text/html; charset=utf-8", DevWebPage.HTML);
                    return;
                }

                if ((method == "GET") && (path == "/api/state"))
                {
                    Respond(stream, "200 OK", "application/json; charset=utf-8",
                        DevPanelHost.CurrentSnapshotJson);
                    return;
                }

                if ((method == "POST") && (path == "/api/command"))
                {
                    DevPanelHost.EnqueueCommandBody(body);
                    Respond(stream, "200 OK", "application/json; charset=utf-8", "{\"ok\":true}");
                    return;
                }

                Respond(stream, "404 Not Found", "text/plain; charset=utf-8", "없는 주소다");
            }
        }

        /// <summary>
        /// 요청 한 건을 읽는다. 개발용이라 우리가 만든 페이지가 보내는 형태만 다룬다 -
        /// 청크 전송이나 파이프라이닝은 받지 않는다
        /// </summary>
        private static bool TryReadRequest(NetworkStream stream, out string method, out string path,
                                           out string body)
        {
            method = string.Empty;
            path   = string.Empty;
            body   = string.Empty;

            var    header = new MemoryStream();
            byte[] one    = new byte[1];
            int    match  = 0;

            // 헤더 끝(빈 줄)까지 한 바이트씩 읽는다. 뒤에 오는 본문을 넘겨 읽지 않기 위한 것이다
            while (header.Length < MAX_HEADER_BYTES)
            {
                int read = stream.Read(one, 0, 1);
                if (read <= 0)
                {
                    return false;
                }

                header.WriteByte(one[0]);

                bool isExpected = ((match == 0) || (match == 2)) ? (one[0] == (byte) '\r')
                                                                 : (one[0] == (byte) '\n');
                match = isExpected ? (match + 1) : 0;

                if (match == 4)
                {
                    break;
                }
            }

            string text = Encoding.UTF8.GetString(header.ToArray());

            string[] lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0)
            {
                return false;
            }

            string[] request = lines[0].Split(' ');
            if (request.Length < 2)
            {
                return false;
            }

            method = request[0];
            path   = request[1];

            int length = ResolveContentLength(lines);
            if (length <= 0)
            {
                return true;
            }

            byte[] buffer = new byte[Mathf.Min(length, MAX_BODY_BYTES)];

            int filled = 0;
            while (filled < buffer.Length)
            {
                int read = stream.Read(buffer, filled, buffer.Length - filled);
                if (read <= 0)
                {
                    break;
                }

                filled += read;
            }

            body = Encoding.UTF8.GetString(buffer, 0, filled);
            return true;
        }

        private static int ResolveContentLength(string[] lines)
        {
            for (int i = 1; i < lines.Length; ++i)
            {
                string line = lines[i];

                if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (int.TryParse(line.Substring("Content-Length:".Length).Trim(), out int length))
                {
                    return length;
                }
            }

            return 0;
        }

        private static void Respond(NetworkStream stream, string status, string contentType, string content)
        {
            byte[] payload = Encoding.UTF8.GetBytes(content ?? string.Empty);

            var head = new StringBuilder();
            head.Append("HTTP/1.1 ").Append(status).Append("\r\n");
            head.Append("Content-Type: ").Append(contentType).Append("\r\n");
            head.Append("Content-Length: ").Append(payload.Length).Append("\r\n");
            head.Append("Cache-Control: no-store\r\n");
            head.Append("Connection: close\r\n\r\n");

            byte[] headBytes = Encoding.UTF8.GetBytes(head.ToString());

            stream.Write(headBytes, 0, headBytes.Length);
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        private static int ResolvePort()
        {
            // 이 프로젝트에는 Geumeum.Common.Environment가 따로 있어 이름이 겹친다
            string[] args = System.Environment.GetCommandLineArgs();

            for (int i = 0; i < (args.Length - 1); ++i)
            {
                if (!string.Equals(args[i], ARG_PORT, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (int.TryParse(args[i + 1], out int parsed))
                {
                    return parsed;
                }
            }

            return DEFAULT_PORT;
        }

        private static bool HasArg(string name)
        {
            // 이 프로젝트에는 Geumeum.Common.Environment가 따로 있어 이름이 겹친다
            string[] args = System.Environment.GetCommandLineArgs();

            for (int i = 0; i < args.Length; ++i)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary> 다른 기기에서 열 때 쓸 이 PC의 주소 </summary>
        private static string GetLocalAddress()
        {
            try
            {
                IPHostEntry entry = Dns.GetHostEntry(Dns.GetHostName());

                for (int i = 0; i < entry.AddressList.Length; ++i)
                {
                    IPAddress address = entry.AddressList[i];

                    if (address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return address.ToString();
                    }
                }
            }
            catch (Exception)
            {
                // 주소를 못 찾아도 서버는 이미 떠 있다. 표시만 못 하는 것이다
            }

            return "127.0.0.1";
        }

#endif
    }
}
