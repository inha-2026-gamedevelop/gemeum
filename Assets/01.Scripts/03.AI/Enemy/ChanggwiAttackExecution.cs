// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI
{
    /// <summary>
    /// 창귀 공격 한 번의 실행 단위, 그 공격의 패링 판정 source다. ChanggwiBrain 자신을 source로
    /// 재사용하면 이전 공격의 늦은 OnParried가 이미 시작된 다음 공격을 잘못 넉백시킬 수 있다 -
    /// 공격마다 새로 만들어 그 경합을 막는다
    /// </summary>
    internal sealed class ChanggwiAttackExecution : IParryable
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly ChanggwiBrain _owner;

        /****************************************
        *              Properties
        ****************************************/

        public float ParryWindowDuration { get; }

        /****************************************
        *              Constructor
        ****************************************/

        public ChanggwiAttackExecution(ChanggwiBrain owner, float parryWindowDuration)
        {
            _owner              = owner;
            ParryWindowDuration = parryWindowDuration;
        }

        /****************************************
        *              IParryable
        ****************************************/

        public void OnParried(uint parriedByNetId)
        {
            _owner.HandleExecutionParried(this, parriedByNetId);
        }
    }
}
