using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary> 요청자에게만 안내한다. 중복 패킷 같은 내부 사유는 채팅으로 보내지 않는다 </summary>
    public static class ActionRejectionChat
    {
        public static void ShowParry(ParryResultCode result)
        {
            switch (result)
            {
                case ParryResultCode.RejectedStamina: Show(ActionRejection.Resource); break;
                case ParryResultCode.RejectedState: Show(ActionRejection.State); break;
                case ParryResultCode.RejectedBusy: Show(ActionRejection.Busy); break;
                case ParryResultCode.RejectedDependency: Show(ActionRejection.NotReady); break;
                case ParryResultCode.RejectedWeapon: Show(ActionRejection.NoWeapon); break;
            }
        }

        public static void Show(ActionRejection rejection, bool usesSpirit = false,
            bool cooldown = false, bool placement = false)
        {
            string text = GetText(rejection, usesSpirit, cooldown, placement);
            if (text != null) { LocalChatLog.AddSystem(text); }
        }

        public static string GetText(ActionRejection rejection, bool usesSpirit = false,
            bool cooldown = false, bool placement = false)
        {
            switch (rejection)
            {
                case ActionRejection.Resource: return usesSpirit ? "신력이 부족합니다" : "스태미나가 부족합니다";
                case ActionRejection.Busy: return cooldown ? "아직 재사용 대기시간입니다." : "다른 행동이 진행 중입니다.";
                case ActionRejection.State: return "현재 상태에서는 행동할 수 없습니다.";
                case ActionRejection.NotReady: return "아직 행동할 준비가 되지 않았습니다.";
                case ActionRejection.NoWeapon: return "사용할 수 있는 무기가 없습니다.";
                case ActionRejection.InvalidInput: return placement ? "이 위치에는 설치할 수 없습니다." : "행동할 방향이나 대상을 확인해 주세요.";
                default: return null;
            }
        }
    }
}
