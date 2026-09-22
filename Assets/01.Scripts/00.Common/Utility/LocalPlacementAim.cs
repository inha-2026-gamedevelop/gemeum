// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 설치 조준 중인 로컬 전투 상태
    ///
    /// 모달 UI 토큰(LocalGameplayInputGate)을 재사용하지 않는다 -
    /// 그 토큰은 이동까지 막는데, 설치 중에는 걸어 다닐 수 있어야 한다
    /// 여기서 막는 것은 카메라 회전과 전투 입력뿐이다
    /// </summary>
    public static class LocalPlacementAim
    {
        /****************************************
        *                Fields
        ****************************************/

        private static bool           G_Active;
        private static int            G_EndFrame = -1;
        private static bool           G_SettlePending;
        private static bool           G_PreviousCameraInput;
        private static CursorLockMode G_PreviousCursorLock;
        private static bool           G_PreviousCursorVisible;

        /****************************************
        *              Properties
        ****************************************/

        public static bool IsActive => G_Active;

        /// <summary>
        /// 이 프레임의 공격, 치유, 락온 입력을 삼켜야 하는가
        /// 조준을 끝낸 그 프레임도 포함한다 - 취소한 클릭이 곧바로 평타로 새면
        /// 사용자가 보기에는 취소가 오발이 된다
        /// </summary>
        public static bool SuppressesCombatInput =>
            (G_Active) || (G_EndFrame == Time.frameCount);

        /// <summary> 카메라 yaw/pitch와 락온 추적을 멈춰야 하는가 </summary>
        public static bool LocksCameraRotation => G_Active;

        /****************************************
        *                Methods
        ****************************************/

        public static void Begin()
        {
            if (G_Active)
            {
                return;
            }

            G_Active                = true;
            G_PreviousCameraInput   = CameraFrame.InputEnabled;
            G_PreviousCursorLock    = Cursor.lockState;
            G_PreviousCursorVisible = Cursor.visible;

            CameraFrame.InputEnabled = false;
            Cursor.lockState         = CursorLockMode.None;
            Cursor.visible           = true;
        }

        public static void End()
        {
            if (!G_Active)
            {
                return;
            }

            G_Active        = false;
            G_EndFrame      = Time.frameCount;
            G_SettlePending = true;

            CameraFrame.InputEnabled = G_PreviousCameraInput;
            Cursor.lockState         = G_PreviousCursorLock;
            Cursor.visible           = G_PreviousCursorVisible;
        }

        /// <summary>
        /// 조준을 끝낸 뒤 첫 프레임의 마우스 델타를 한 번 버린다
        /// 자유 커서로 화면을 가로질렀던 이동량이 그대로 들어오면 시점이 튄다
        /// </summary>
        public static bool ConsumeSettleFrame()
        {
            if (!G_SettlePending)
            {
                return false;
            }

            G_SettlePending = false;
            return true;
        }

        /// <summary> 도메인 리로드 OFF 대비 정적 상태 초기화 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            G_Active        = false;
            G_EndFrame      = -1;
            G_SettlePending = false;
        }
    }
}
