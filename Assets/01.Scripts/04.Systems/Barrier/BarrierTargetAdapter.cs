// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    /// <summary>
    /// 플레이어 루트의 IDamageable/IRecoveryTarget을 초기화 시점에 한 번 캐싱해 결계 서버 registry에 등록한다.
    /// BarrierController의 서버 피해 tick이 매 틱 GetComponent/Find를 반복하지 않도록 이 어댑터가 대신 캐싱을 갖고 있는다
    /// </summary>
    public class BarrierTargetAdapter : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private IDamageable     _damageable;

        /****************************************
        *              Properties
        ****************************************/

        public IDamageable Damageable => _damageable;

        /****************************************
        *              Server Callbacks
        ****************************************/

        // registry는 서버 피해 판정 전용이라 서버에서만 등록한다, 클라이언트에는 이 목록 자체가 없다
        public override void OnStartServer()
        {
            _damageable = GetComponent<IDamageable>();

            if (_damageable == null)
            {
                Debug.LogError("[BarrierTargetAdapter] 같은 오브젝트에서 IDamageable 구현을 찾지 못했다", this);
                return;
            }

            BarrierTargetRegistry.Register(this);
        }

        public override void OnStopServer()
        {
            BarrierTargetRegistry.Unregister(this);
        }
    }
}
