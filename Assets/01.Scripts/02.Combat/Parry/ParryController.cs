// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary>
    /// 패링 입력을 서버로 보내고 모션은 즉시 재생한다.
    /// 결과는 호스트 응답 후 확정하며 롤백하지 않는다
    /// </summary>
    public class ParryController : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private double _windowStartTime = 0.0;  // 서버 전용, 공격 측이 OpenParryWindow로 채운다
        private float  _windowDuration  = 0f;   // 서버 전용

        /****************************************
        *              Unity Event
        ****************************************/

        // TODO(민성): 패링 모션과 연출 연결
        private void Update()
        {
            if (!isOwned)
            {
                return;
            }

            if (!Input.GetMouseButtonDown(Constants.Player.MOUSE_BUTTON_PARRY))
            {
                return;
            }

            CmdParry(NetworkTime.time);
        }

        /****************************************
        *              Commands
        ****************************************/

        // TODO(민성): 생명 상태와 장착 무기가 생기면 여기서 먼저 검증한다
        [Command]
        private void CmdParry(double clientEstimatedServerTime)
        {
            bool success = ParryJudge.IsSuccess(clientEstimatedServerTime, _windowStartTime, _windowDuration);
            TargetParryResult(success);
        }

        /****************************************
        *              Server API
        ****************************************/

        /// <summary> 이 플레이어를 노리는 공격의 판정 창을 연다 </summary>
        [Server]
        public void OpenParryWindow(float windowDuration)
        {
            _windowStartTime = NetworkTime.time;
            _windowDuration  = windowDuration;
        }

        /****************************************
        *              TargetRpc
        ****************************************/

        [TargetRpc]
        private void TargetParryResult(bool success)
        {
            Debug.Log(success ? "[ParryController] 패링 성공" : "[ParryController] 패링 실패");
        }
    }
}
