// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

namespace Geumeum.Network
{
    /// <summary> 실행 인자로 호스트, 클라이언트를 바로 시작한다 </summary>
    public class NetworkLaunchArgs : MonoBehaviour
    {
        /// <summary> 개발 경로에서 인증기가 실어 보낼 신원 </summary>
        public static ulong RequestedPlatformId { get; private set; } = 0UL;

#if UNITY_EDITOR || DEVELOPMENT_BUILD

        /****************************************
        *                Fields
        ****************************************/

        private const string ARG_HOST   = "-host";
        private const string ARG_SERVER = "-server";
        private const string ARG_CLIENT = "-client"; // 뒤에 접속할 주소가 온다
        private const string ARG_PORT   = "-port";   // 뒤에 포트 번호가 온다
        private const string ARG_PLATFORM_ID = "-platformid"; // 뒤에 신원으로 쓸 숫자가 온다

        /****************************************
        *              Unity Event
        ****************************************/

        private void Start()
        {
            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Debug.LogWarning("[NetworkLaunchArgs] NetworkManager가 없어 실행 인자를 적용하지 않는다", this);
                return;
            }

            string[] args = Environment.GetCommandLineArgs();

            ApplyPlatformId(args);
            ApplyPort(args);

            if (TryFindValue(args, ARG_CLIENT, out string address))
            {
                manager.networkAddress = address;
                Debug.Log($"[NetworkLaunchArgs] 클라이언트로 {address}:{ResolvePort()}에 접속한다");
                manager.StartClient();
                return;
            }

            if (HasFlag(args, ARG_HOST))
            {
                Debug.Log($"[NetworkLaunchArgs] 호스트를 {ResolvePort()} 포트로 시작한다");
                manager.StartHost();
                return;
            }

            if (HasFlag(args, ARG_SERVER))
            {
                Debug.Log($"[NetworkLaunchArgs] 서버를 {ResolvePort()} 포트로 시작한다");
                manager.StartServer();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void ApplyPlatformId(string[] args)
        {
            if (!TryFindValue(args, ARG_PLATFORM_ID, out string raw))
            {
                return;
            }

            if (!ulong.TryParse(raw, out ulong value))
            {
                Debug.LogWarning($"[NetworkLaunchArgs] {ARG_PLATFORM_ID} 값이 숫자가 아니다: {raw}", this);
                return;
            }

            RequestedPlatformId = value;
            Debug.Log($"[NetworkLaunchArgs] 신원을 {value}로 지정했다");
        }

        private void ApplyPort(string[] args)
        {
            if (!TryFindValue(args, ARG_PORT, out string rawPort))
            {
                return;
            }

            if (!ushort.TryParse(rawPort, out ushort port))
            {
                Debug.LogWarning($"[NetworkLaunchArgs] 포트 값을 읽지 못해 무시한다: {rawPort}", this);
                return;
            }

            if (Transport.active is PortTransport portTransport)
            {
                portTransport.Port = port;
                return;
            }

            Debug.LogWarning("[NetworkLaunchArgs] 활성 트랜스포트가 포트를 노출하지 않아 인자를 무시한다", this);
        }

        private static ushort ResolvePort()
        {
            return (Transport.active is PortTransport portTransport) ? portTransport.Port : (ushort) 0;
        }

        private static bool HasFlag(string[] args, string flag)
        {
            for (int i = 0; i < args.Length; ++i)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindValue(string[] args, string flag, out string value)
        {
            value = null;

            for (int i = 0; i < (args.Length - 1); ++i)
            {
                if (!string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string candidate = args[i + 1];
                if ((string.IsNullOrWhiteSpace(candidate)) || (candidate.StartsWith("-")))
                {
                    return false;
                }

                value = candidate;
                return true;
            }

            return false;
        }

#endif
    }
}
