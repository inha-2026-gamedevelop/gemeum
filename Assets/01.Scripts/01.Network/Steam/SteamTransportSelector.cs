// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;
using Mirror.FizzySteam;

namespace Geumeum.Network
{
    /// <summary> 시작 시 트랜스포트 경로를 정한다. 기본은 KCP, -steam 인자가 있으면 FizzySteamworks다 </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class SteamTransportSelector : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string ARG_STEAM = "-steam";

        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("Steam 경로에서 쓸 트랜스포트. 같은 오브젝트에 붙여 두고 필드는 KCP로 남긴다")]
        [SerializeField] private FizzySteamworks _steamTransport;

        private NetworkManager _manager;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> Steam 경로로 시작했는지 </summary>
        public bool IsSteamPath { get; private set; } = false;

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 이 트랜스포트가 Steam 경로인지 </summary>
        public static bool IsSteamTransport(Transport transport)
        {
            return (transport is FizzySteamworks);
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _manager = GetComponent<NetworkManager>();

            if (!HasFlag(Environment.GetCommandLineArgs(), ARG_STEAM))
            {
                return;
            }

            if (_steamTransport == null)
            {
                Debug.LogError("[SteamTransportSelector] Steam 트랜스포트가 연결되지 않아 KCP로 진행한다", this);
                return;
            }

            _manager.transport = _steamTransport;
            Transport.active   = _steamTransport;
            IsSteamPath        = true;

            Debug.Log("[SteamTransportSelector] Steam 경로로 시작한다, 트랜스포트를 FizzySteamworks로 바꿨다");
        }

        private void Start()
        {
            if (!IsSteamPath)
            {
                Debug.Log("[SteamTransportSelector] KCP 경로로 시작한다");
                return;
            }

            LogSteamIdentity();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void LogSteamIdentity()
        {
            if (!SteamLocalUser.TryGetSteamId(out ulong steamId))
            {
                Debug.LogError("[SteamTransportSelector] Steam 초기화에 실패했다, Steam 클라이언트 실행과 steam_appid.txt를 확인할 것", this);
                return;
            }

            Debug.Log($"[SteamTransportSelector] Steam 초기화 성공, 내 SteamID64={steamId}");
            Debug.Log($"[SteamTransportSelector] 다른 PC에서 접속하려면 실행 인자에 -steam -client {steamId} 를 준다");
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
    }
}
