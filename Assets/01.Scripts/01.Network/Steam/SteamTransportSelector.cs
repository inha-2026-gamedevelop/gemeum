// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;
using Mirror.FizzySteam;

#if !DISABLESTEAMWORKS
using Steamworks;
#endif

namespace Geumeum.Network
{
    /// <summary>
    /// 시작 시 한 번 트랜스포트 경로를 정한다. 기본은 KCP이고 -steam 인자가 있을 때만 FizzySteamworks로 바꾼다.
    /// 런타임 폴백과 MultiplexTransport를 쓰지 않는 이유는 접속 경로가 둘이면 인증기가 신원을
    /// 어느 쪽에서 얻을지 연결마다 갈리기 때문이다
    /// </summary>
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

        /// <summary> Steam 경로로 시작했다. 신원 소스와 부트 검사가 이 값으로 짝을 맞춘다 </summary>
        public bool IsSteamPath { get; private set; } = false;

        /****************************************
        *              Unity Event
        ****************************************/

        // NetworkLaunchArgs가 Start에서 접속을 시작하므로 그 전에 트랜스포트를 확정해야 한다.
        // NetworkManager.singleton은 이 시점에 아직 없을 수 있어 같은 오브젝트에서 직접 찾는다
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

            // NetworkManager.Awake가 transport 필드로 Transport.active를 덮으므로 둘 다 바꾼다.
            // 두 Awake의 실행 순서에 기대지 않기 위해서다
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

        // 0단계 스파이크의 관측 지점이다. 자기 SteamID를 알아야 다른 PC가 -client 인자로 접속할 수 있다
        private void LogSteamIdentity()
        {
#if DISABLESTEAMWORKS
            Debug.LogError("[SteamTransportSelector] 이 플랫폼에서는 Steamworks가 비활성이다", this);
#else
            if (!SteamManager.Initialized)
            {
                Debug.LogError("[SteamTransportSelector] Steam 초기화에 실패했다, Steam 클라이언트 실행과 steam_appid.txt를 확인할 것", this);
                return;
            }

            CSteamID steamId = SteamUser.GetSteamID();
            Debug.Log($"[SteamTransportSelector] Steam 초기화 성공, 내 SteamID64={steamId.m_SteamID}, 이름={SteamFriends.GetPersonaName()}");
            Debug.Log($"[SteamTransportSelector] 다른 PC에서 접속하려면 실행 인자에 -steam -client {steamId.m_SteamID} 를 준다");
#endif
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
