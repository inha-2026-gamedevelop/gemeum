// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Network;

namespace Geumeum.Combat
{
    /// <summary>
    /// 서버가 확정한 "내가 준 피해" 결과를 소유 클라이언트에게 전달한다(COMBAT_STYLE_DESIGN 8.1)
    /// 서버는 프레임 끝에 ServerDamageResultHub의 내 몫을 꺼내 TargetRpc 한 번으로 보내고, 수신 쪽은 LocalDamageResultFeed로 흘린다
    /// 호스트도 같은 TargetRpc 경로를 탄다. 표현(히트스톱·숫자)은 피드 구독자가 맡고 여기서는 판정을 다시 하지 않는다
    /// </summary>
    public partial class PlayerAttack
    {
        /****************************************
        *                Fields
        ****************************************/

        // TargetRpc마다 새 배열을 만들지 않도록 재사용한다
        private readonly DamageResult[] _damageResultBatch =
            new DamageResult[ServerDamageResultHub.MAX_RESULTS_PER_SEND];

        // 마지막으로 결과를 보낸 런·씬 상태. 바뀌면 남은 결과를 버리고 클라이언트 표현도 비운다
        private bool _hasDamageResultContext;
        private uint _damageResultRunId;
        private uint _damageResultSceneRevision;
        private bool _damageResultTransitioning;

        /****************************************
        *              Unity Event
        ****************************************/

        // 같은 프레임의 근접·화살·스킬 피해가 모두 확정된 뒤 한 번에 보낸다
        private void LateUpdate()
        {
            if (!isServer)
            {
                return;
            }

            ServerFlushDamageResults();
        }

        /****************************************
        *            Network Callbacks
        ****************************************/

        public override void OnStartLocalPlayer()
        {
            // 새 연결에서 이전 세션의 사건 번호·대기 표현이 남지 않게 한다
            LocalDamageResultFeed.Clear();
        }

        public override void OnStopLocalPlayer()
        {
            LocalDamageResultFeed.Clear();
        }

        /****************************************
        *              Server Side
        ****************************************/

        [Server]
        private void ServerFlushDamageResults()
        {
            // 봇은 받을 화면이 없다. 허브도 거르지만 남은 것이 있으면 여기서 버린다
            if (connectionToClient == null)
            {
                ServerDamageResultHub.ClearAttacker(netId);
                return;
            }

            bool isTransitioning = RunSceneDirector.IsServerTransitioning;
            if (ServerUpdateDamageResultContext(isTransitioning))
            {
                // 이전 런·씬의 결과가 새 런·씬에서 재생되면 안 된다
                ServerDamageResultHub.ClearAttacker(netId);
                TargetClearDamageResults();
                return;
            }

            if (isTransitioning)
            {
                ServerDamageResultHub.ClearAttacker(netId);
                return;
            }

            int count = ServerDamageResultHub.Drain(netId, _damageResultBatch, out int dropped);
            if (dropped > 0)
            {
                Debug.LogWarning($"[PlayerAttack] 피해 결과 대기열이 넘쳐 오래된 결과 {dropped}건을 버렸다. "
                    + "피해 자체는 이미 적용됐고 표현만 빠진다", this);
            }

            if (count <= 0)
            {
                return;
            }

            // 넘친 것은 허브에 남아 다음 프레임에 이어서 나간다
            TargetDamageResults(new ArraySegment<DamageResult>(_damageResultBatch, 0, count));
        }

        /// <summary> 연결 해제·파괴 때 이 공격자의 남은 결과를 버린다 </summary>
        [Server]
        private void ServerDiscardDamageResults()
        {
            ServerDamageResultHub.ClearAttacker(netId);
            _hasDamageResultContext = false;
        }

        /// <summary> 런 번호·씬 리비전·전환 여부가 지난 전송 이후 바뀌었으면 true. 처음 관찰은 바뀐 것으로 치지 않는다 </summary>
        [Server]
        private bool ServerUpdateDamageResultContext(bool isTransitioning)
        {
            RunManager       run      = RunManager.ServerInstance;
            RunSceneDirector director = RunSceneDirector.ServerInstance;
            uint runId          = (run == null) ? 0u : run.RunId.Value;
            uint sceneRevision  = (director == null) ? 0u : director.Revision.Value;

            if (!_hasDamageResultContext)
            {
                _hasDamageResultContext    = true;
                _damageResultRunId         = runId;
                _damageResultSceneRevision = sceneRevision;
                _damageResultTransitioning = isTransitioning;
                return false;
            }

            if ((runId == _damageResultRunId) && (sceneRevision == _damageResultSceneRevision) &&
                (isTransitioning == _damageResultTransitioning))
            {
                return false;
            }

            _damageResultRunId         = runId;
            _damageResultSceneRevision = sceneRevision;
            _damageResultTransitioning = isTransitioning;
            return true;
        }

        /****************************************
        *              Target Rpc
        ****************************************/

        /// <summary> 내가 준 피해 결과 묶음. 도착 순서대로 피드에 흘린다. 같은 사건은 피드가 한 번만 통과시킨다 </summary>
        [TargetRpc]
        private void TargetDamageResults(ArraySegment<DamageResult> results)
        {
            DamageResult[] array = results.Array;
            if (array == null)
            {
                return;
            }

            int end = results.Offset + results.Count;
            for (int i = results.Offset; i < end; ++i)
            {
                LocalDamageResultFeed.Publish(in array[i]);
            }
        }

        /// <summary> 런·씬이 바뀌어 남은 피해 표현을 모두 버린다 </summary>
        [TargetRpc]
        private void TargetClearDamageResults()
        {
            LocalDamageResultFeed.Clear();
        }
    }
}
