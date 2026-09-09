// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary> Combat의 생명 상태와 공용 보관 값을 잇는 어댑터 </summary>
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
                case PlayerLifeState.Swallowed:          return PlayerLifeSnapshotState.Swallowed;
                default:                                 return PlayerLifeSnapshotState.Alive;
            }
        }

        /// <summary> 보관용 값에서 되돌린다. 대응 값이 없으면 false다 </summary>
        public static bool TryToLifeState(this PlayerLifeSnapshotState snapshotState, out PlayerLifeState state)
        {
            switch (snapshotState)
            {
                case PlayerLifeSnapshotState.Alive:              state = PlayerLifeState.Alive;              return true;
                case PlayerLifeSnapshotState.Downed:             state = PlayerLifeState.Downed;             return true;
                case PlayerLifeSnapshotState.Spectating:         state = PlayerLifeState.Spectating;         return true;
                case PlayerLifeSnapshotState.ReviveInvulnerable: state = PlayerLifeState.ReviveInvulnerable; return true;
                case PlayerLifeSnapshotState.Swallowed:          state = PlayerLifeState.Swallowed;          return true;

                default:
                    state = PlayerLifeState.Alive;
                    return false;
            }
        }
    }
}
