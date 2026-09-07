// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Network;

namespace Geumeum.Combat
{
    /// <summary>
    /// 화면에 띄울 사람 이름. Steam 경로면 Steam 이름이고, KCP 개발 경로면 신원 번호다.
    /// 서버는 남의 Steam 이름을 알 수 없으므로 각자 자기 이름을 한 번 올려 보낸다
    /// </summary>
    public class PlayerNameTag : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int MAX_NAME_LENGTH = 24; // 이름표가 화면을 가리지 않는 길이

        [SyncVar] private string _displayName = string.Empty;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 비어 있을 수 있다. 그때는 부르는 쪽이 대체 문구를 정한다 </summary>
        public string DisplayName => _displayName;

        /****************************************
        *              Client Callbacks
        ****************************************/

        public override void OnStartAuthority()
        {
            CmdSetDisplayName(ResolveLocalName());
        }

        /****************************************
        *                Commands
        ****************************************/

        // 자기 오브젝트에만 쓰므로 권한을 요구한다. requiresAuthority = false로 열면
        // 남이 남의 이름을 바꿀 수 있게 된다
        [Command]
        private void CmdSetDisplayName(string requested)
        {
            if (string.IsNullOrWhiteSpace(requested))
            {
                return;
            }

            string trimmed = requested.Trim();
            if (trimmed.Length > MAX_NAME_LENGTH)
            {
                trimmed = trimmed.Substring(0, MAX_NAME_LENGTH);
            }

            _displayName = trimmed;
        }

        /****************************************
        *                Methods
        ****************************************/

        private static string ResolveLocalName()
        {
            if (SteamLocalUser.TryGetPersonaName(out string personaName))
            {
                return personaName;
            }

            // KCP 개발 경로에는 Steam 이름이 없다. 창을 구분할 수 있는 값이면 충분하다
            ulong platformId = NetworkLaunchArgs.RequestedPlatformId;
            return (platformId != 0UL) ? ("개발 " + platformId) : "이름 없음";
        }
    }
}
