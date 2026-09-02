#if !DISABLESTEAMWORKS
// 서드파티
using Steamworks;
#endif

namespace Geumeum.Network
{
    /// <summary> 자기 SteamID를 읽는 유일한 창구. Steamworks 타입이 다른 곳으로 새지 않게 여기서 끝낸다 </summary>
    public static class SteamLocalUser
    {
        /// <summary> Steam이 초기화되어 있으면 자기 SteamID64를 준다 </summary>
        public static bool TryGetSteamId(out ulong steamId)
        {
#if DISABLESTEAMWORKS
            steamId = 0UL;
            return false;
#else
            if (!SteamManager.Initialized)
            {
                steamId = 0UL;
                return false;
            }

            steamId = SteamUser.GetSteamID().m_SteamID;
            return (steamId != 0UL);
#endif
        }
    }
}
