// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary>
    /// Combat의 생명 상태와 공용 보관 값을 잇는 어댑터.
    ///
    /// Common이 Combat의 enum을 참조하면 의존 방향이 뒤집히므로 변환을 이쪽에 둔다.
    /// 두 enum의 값은 같게 맞춰 두었지만 캐스팅으로 넘기지 않는다 -
    /// 한쪽에 값이 추가되는 순간 조용히 어긋나기 때문이다
    /// </summary>
    public static class PlayerLifeStateAdapter
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 보관용 값으로 바꾼다 </summary>
        public static PlayerLifeSnapshotState ToSnapshotState(this PlayerLifeState state)
        {
            switch (state)
            {
                case PlayerLifeState.Alive:              return PlayerLifeSnapshotState.Alive;
                case PlayerLifeState.Downed:             return PlayerLifeSnapshotState.Downed;
                case PlayerLifeState.Spectating:         return PlayerLifeSnapshotState.Spectating;
                case PlayerLifeState.ReviveInvulnerable: return PlayerLifeSnapshotState.ReviveInvulnerable;
                default:                                 return PlayerLifeSnapshotState.Alive;
            }
        }

        /// <summary>
        /// 보관용 값에서 되돌린다. 대응 값이 없으면 false다.
        ///
        /// 삼켜짐은 보스 최종 구간 전용이고 Combat에는 아직 대응 상태가 없다.
        /// 없는 것을 Downed로 슬쩍 바꾸면 전멸 판정이 그 사람을 사망자로 세므로 실패로 알린다
        /// </summary>
        public static bool TryToLifeState(this PlayerLifeSnapshotState snapshotState, out PlayerLifeState state)
        {
            switch (snapshotState)
            {
                case PlayerLifeSnapshotState.Alive:              state = PlayerLifeState.Alive;              return true;
                case PlayerLifeSnapshotState.Downed:             state = PlayerLifeState.Downed;             return true;
                case PlayerLifeSnapshotState.Spectating:         state = PlayerLifeState.Spectating;         return true;
                case PlayerLifeSnapshotState.ReviveInvulnerable: state = PlayerLifeState.ReviveInvulnerable; return true;

                default:
                    state = PlayerLifeState.Alive;
                    return false;
            }
        }
    }
}
