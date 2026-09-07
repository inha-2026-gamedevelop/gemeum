// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    /// <summary>
    /// 플레이어 루트의 IDamageable을 초기화 시점에 한 번 캐싱해 결계 서버 registry에 등록한다.
    /// BarrierController의 서버 피해 tick이 매 틱 GetComponent/Find를 반복하지 않도록 이 어댑터가 대신 캐싱을 갖고 있는다.
    ///
    /// NetworkBehaviour가 아니라 순수 MonoBehaviour다. 어차피 registry는 서버 프로세스 안에서만 의미가 있고,
    /// BarrierController가 서버에서 접속자 오브젝트에 직접 AddComponent로 붙인다(3.2.1절). NetworkBehaviour였다면
    /// Player.prefab에 미리 박아둬야 하고, 다른 파트 소유 프리팹을 승인 없이 건드리게 된다
    /// </summary>
    public class BarrierTargetAdapter : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private IDamageable _damageable;

        /****************************************
        *              Properties
        ****************************************/

        public IDamageable Damageable => _damageable;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _damageable = GetComponent<IDamageable>();

            if (_damageable == null)
            {
                Debug.LogError("[BarrierTargetAdapter] 같은 오브젝트에서 IDamageable 구현을 찾지 못했다", this);
                return;
            }

            BarrierTargetRegistry.Register(this);
        }

        private void OnDestroy()
        {
            BarrierTargetRegistry.Unregister(this);
        }
    }
}
