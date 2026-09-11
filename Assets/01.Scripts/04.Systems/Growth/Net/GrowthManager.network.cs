// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    public partial class GrowthManager
    {
        /****************************************
        *                Fields
        ****************************************/

        // 매니저가 네트워크 오브젝트일 때 클라이언트 HUD가 공용 상태를 읽는 값
        [SyncVar] private int   _networkLevel;
        [SyncVar] private int   _networkExperience;
        [SyncVar] private float _networkMaxHealth;
        [SyncVar] private int   _networkTalentPoints;
        [SyncVar(hook = nameof(OnNetworkGrowthRevisionChanged))] private uint  _networkRevision;

        /****************************************
        *           Client Callbacks
        ****************************************/

        public override void OnStartClient()
        {
            GrowthProviderLocator.Register(this);
        }

        public override void OnStopClient()
        {
            if (!isServer)
            {
                GrowthProviderLocator.Unregister(this);
            }
        }

        /****************************************
        *             SyncVar Hooks
        ****************************************/

        private void OnNetworkGrowthRevisionChanged(uint _, uint __)
        {
            if (!isServer)
            {
                GrowthChanged?.Invoke(State);
            }
        }
    }
}
