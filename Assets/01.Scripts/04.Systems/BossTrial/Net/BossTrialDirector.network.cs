// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    public partial class BossTrialDirector
    {
        /****************************************
        *                Fields
        ****************************************/

        [SyncVar(hook = nameof(OnCollectingChanged))]     private bool _isCollecting;
        [SyncVar(hook = nameof(OnSubmittedCountChanged))] private int  _submittedCount;
        [SyncVar(hook = nameof(OnRequiredCountChanged))]  private int  _requiredCount;
        [SyncVar(hook = nameof(OnTrialRequestedChanged))] private bool _isTrialRequested;

        /****************************************
        *                Commands
        ****************************************/

        /// <summary> 전실에서 고른 편성을 낸다. 예산과 카탈로그 검증은 서버가 한다 </summary>
        [Command(requiresAuthority = false)]
        public void CmdSubmitLoadout(BossTrialLoadout loadout, NetworkConnectionToClient sender = null)
        {
            ServerHandleSubmit(sender, loadout);
        }

        /// <summary> 다음 런을 보스 트라이로 시작한다. 호스트만 보낼 수 있다 </summary>
        [Command(requiresAuthority = false)]
        public void CmdSetTrialMode(bool enabled, NetworkConnectionToClient sender = null)
        {
            ServerHandleTrialModeRequest(sender, enabled);
        }

        /// <summary> 끝난 판을 한 번 더 한다. 호스트만 보낼 수 있다 </summary>
        [Command(requiresAuthority = false)]
        public void CmdRequestRetry(NetworkConnectionToClient sender = null)
        {
            ServerHandleRetryRequest(sender);
        }
    }
}
