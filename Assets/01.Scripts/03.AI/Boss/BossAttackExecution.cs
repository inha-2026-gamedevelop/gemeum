// System
using System;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI
{
    /// <summary>
    /// 보스 공격 한 번의 패링 판정 source. 페이즈 State가 실행마다 새로 만들어 넘기고,
    /// 성공 시 콜백으로 결과를 돌려준다. 잡귀(ChanggwiAttackExecution)와 같은 이유로
    /// 공격마다 새로 만든다: 이전 공격의 늦은 OnParried가 이미 시작된 다음 공격을
    /// 잘못 취소시키는 것을 막는다
    /// </summary>
    internal sealed class BossAttackExecution : IParryable
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Action<BossAttackExecution, uint> _onParried;

        /****************************************
        *              Properties
        ****************************************/

        public float ParryWindowDuration { get; }

        /****************************************
        *              Constructor
        ****************************************/

        public BossAttackExecution(float parryWindowDuration, Action<BossAttackExecution, uint> onParried)
        {
            ParryWindowDuration = parryWindowDuration;
            _onParried          = onParried;
        }

        /****************************************
        *              IParryable
        ****************************************/

        public void OnParried(uint parriedByNetId)
        {
            _onParried?.Invoke(this, parriedByNetId);
        }
    }
}
