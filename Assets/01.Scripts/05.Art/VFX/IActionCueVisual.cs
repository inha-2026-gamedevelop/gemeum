// Project
using Geumeum.Common;

namespace Geumeum.Art
{
    /// <summary> 풀에서 꺼낸 연출이 서버 cue 시각표를 받아 스스로 진행할 때 구현한다 </summary>
    public interface IActionCueVisual
    {
        /// <summary> blade는 행동한 플레이어의 무기. 무기를 따라가지 않는 연출은 무시한다 </summary>
        void PlayCue(in PlayerActionCue cue, IWeaponBladeSource blade);

        /// <summary> 다음 행동이 시작되거나 취소됐다. 새로 그리기는 멈추되 이미 남긴 잔상은 스스로 지운다 </summary>
        void ReleaseCue();
    }
}
