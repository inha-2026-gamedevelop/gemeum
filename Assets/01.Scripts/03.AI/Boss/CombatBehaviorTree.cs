// System
using System;

namespace Geumeum.AI
{
    /// <summary>
    /// Combat BT 평가 결과. Running은 없다 - Combat BT는 AttackState.Idle일 때만 매번
    /// 처음부터 재평가되고, Windup/Recovery 동안 여러 틱에 걸친 진행 상태는 Pattern
    /// Execution 계층(AttackState)이 전담한다
    /// </summary>
    public enum BTResult
    {
        /// <summary> 행동을 실제로 실행했다, 이번 평가를 종료한다 </summary>
        Success,

        /// <summary> 이 branch가 적용되지 않는다, Selector가 다음 형제를 시도할 수 있다 </summary>
        Failure,

        /// <summary>
        /// 이 branch가 이번 평가를 점유했지만 행동을 시작하지 못했다(방어적 상황) -
        /// 더 낮은 우선순위 branch로 넘어가지 않고 평가를 종료한다
        /// </summary>
        Blocked,
    }

    /// <summary> Combat BT 노드 공통 인터페이스. 매 호출마다 즉시 평가만 한다 </summary>
    public interface ICombatBTNode
    {
        BTResult Evaluate();
    }

    /// <summary>
    /// 자식을 순서대로 평가하다 Success가 아닌 결과(Failure/Blocked)가 나오면 그 결과를
    /// 그대로 돌려주고 즉시 멈춘다. 자식이 전부 Success면 Success
    /// </summary>
    public sealed class SequenceNode : ICombatBTNode
    {
        private readonly ICombatBTNode[] _children;

        public SequenceNode(params ICombatBTNode[] children)
        {
            _children = children;
        }

        public BTResult Evaluate()
        {
            for (int i = 0; i < _children.Length; ++i)
            {
                BTResult result = _children[i].Evaluate();
                if (result != BTResult.Success)
                {
                    return result;
                }
            }

            return BTResult.Success;
        }
    }

    /// <summary>
    /// 자식을 순서대로 시도한다. Success면 즉시 Success를 돌려주고 멈춘다.
    /// Blocked도 즉시 그 결과를 돌려주고 남은 형제는 시도하지 않는다.
    /// Failure일 때만 다음 형제를 시도하고, 전부 Failure면 Failure를 돌려준다
    /// </summary>
    public sealed class SelectorNode : ICombatBTNode
    {
        private readonly ICombatBTNode[] _children;

        public SelectorNode(params ICombatBTNode[] children)
        {
            _children = children;
        }

        public BTResult Evaluate()
        {
            for (int i = 0; i < _children.Length; ++i)
            {
                BTResult result = _children[i].Evaluate();
                if (result == BTResult.Success)
                {
                    return BTResult.Success;
                }

                if (result == BTResult.Blocked)
                {
                    return BTResult.Blocked;
                }
            }

            return BTResult.Failure;
        }
    }

    /// <summary>
    /// 델리게이트 하나를 리프 노드로 감싼다. Condition/Action을 별도 클래스로 나누지 않고
    /// 이 하나로 둘 다 표현한다 - 순수 판단만 하면 Condition처럼, 부수효과를 내면 Action처럼 쓰인다
    /// </summary>
    public sealed class DelegateNode : ICombatBTNode
    {
        private readonly Func<BTResult> _evaluate;

        public DelegateNode(Func<BTResult> evaluate)
        {
            _evaluate = evaluate;
        }

        public BTResult Evaluate()
        {
            return _evaluate();
        }
    }
}
