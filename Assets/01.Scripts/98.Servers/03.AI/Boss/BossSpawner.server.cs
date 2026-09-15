// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Network;

namespace Geumeum.AI
{
    public partial class BossSpawner
    {
        /****************************************
        *              Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            _hasSpawned = false;

            // 먼저 구독한 뒤 현재 phase를 읽는다 — 구독이 늦으면 그 사이의 전환을 놓친다
            GameEventBus.Instance.Subscribe<RunPhaseChangedEvent>(HandlePhaseChanged);

            // 스폰 여부를 서버 시작에만 되돌리면 같은 세션의 두 번째 런에서 보스가 나오지 않는다
            GameEventBus.Instance.Subscribe<RunResetEvent>(HandleRunReset);

            if ((RunManager.ServerInstance != null) && (RunManager.ServerInstance.Phase == RunPhase.Boss))
            {
                TrySpawnBoss(); // 이미 Boss phase에서 이 컴포넌트가 시작된 경우(재시작 등)의 bootstrap 경로
            }
        }

        public override void OnStopServer()
        {
            // HasInstance→Instance 순차 조회는 앱 종료 중 경쟁 조건이 있다(_isQuitting을
            // HasInstance는 안 보고 Instance는 본다). Instance 한 번만 받아 그 결과로 null 체크한다
            GameEventBus bus = GameEventBus.Instance;
            if (bus == null)
            {
                return;
            }

            bus.Unsubscribe<RunPhaseChangedEvent>(HandlePhaseChanged);
            bus.Unsubscribe<RunResetEvent>(HandleRunReset);
        }

        /****************************************
        *                Methods
        ****************************************/

        // 런이 끝나 다음 판을 열 수 있게 되면 스폰 여부를 되돌린다
        [Server]
        private void HandleRunReset(RunResetEvent evt)
        {
            _hasSpawned = false;
        }

        [Server]
        private void HandlePhaseChanged(RunPhaseChangedEvent evt)
        {
            if (evt.Current != RunPhase.Boss)
            {
                return;
            }

            TrySpawnBoss();
        }

        [Server]
        private void TrySpawnBoss()
        {
            if (_hasSpawned)
            {
                return; // 이벤트 구독과 bootstrap 체크가 겹쳐도 한 번만 스폰한다
            }

            if (_bossPrefab == null)
            {
                Debug.LogError("[BossSpawner] 보스 프리팹이 배정되지 않았다", this);
                return;
            }

            Vector3 spawnPosition = Vector3.zero;

            GameObject marker = GameObject.FindWithTag(Constants.Tag.BOSS);
            if (marker == null)
            {
                Debug.LogWarning("[BossSpawner] Boss 태그 마커를 찾지 못했다, 원점에 스폰한다", this);
            }
            else
            {
                spawnPosition = marker.transform.position;
            }

            GameObject instance = Instantiate(_bossPrefab, spawnPosition, Quaternion.identity);
            NetworkServer.Spawn(instance);

            _hasSpawned = true;
        }
    }
}
