// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 잡귀와 보스가 죽었을 때 서버가 발행한다.
    /// 드랍 · 경험치 · 점수가 이것을 구독하므로 몬스터 파트가 게임플레이 시스템 파트를 직접 부르지 않는다
    /// </summary>
    public readonly struct EnemyDiedEvent
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 죽은 대상의 netId </summary>
        public uint EnemyNetId { get; }

        /// <summary> 죽은 대상의 종류 </summary>
        public EnemyKind Kind { get; }

        /// <summary> 사망 위치, 드랍 스폰 지점이 된다 </summary>
        public Vector3 Position { get; }

        /// <summary> 마지막 타격을 넣은 플레이어의 netId, 없으면 DamageInfo.NO_ATTACKER </summary>
        public uint KillerNetId { get; }

        /// <summary> 보스인지, 드랍 테이블과 점수 배점이 갈린다 </summary>
        public bool IsBoss { get; }

        /****************************************
        *                Methods
        ****************************************/

        public EnemyDiedEvent(uint enemyNetId, EnemyKind kind, Vector3 position, uint killerNetId, bool isBoss)
        {
            EnemyNetId  = enemyNetId;
            Kind        = kind;
            Position    = position;
            KillerNetId = killerNetId;
            IsBoss      = isBoss;
        }
    }
}
