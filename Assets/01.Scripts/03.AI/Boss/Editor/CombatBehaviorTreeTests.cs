// 서드파티
using NUnit.Framework;

namespace Geumeum.AI.Tests
{
    /// <summary>
    /// Combat BT 최소 구성(SequenceNode/SelectorNode)의 BTResult 전파 규칙을 검증한다.
    /// 스텁 노드로 순수하게 전파 규칙만 보고, 실제 Phase1/2State의 게임플레이 로직은
    /// 건드리지 않는다
    /// </summary>
    public sealed class CombatBehaviorTreeTests
    {
        /****************************************
        *                Helpers
        ****************************************/

        private sealed class StubNode : ICombatBTNode
        {
            private readonly BTResult _result;

            public int EvaluateCount { get; private set; }

            public StubNode(BTResult result)
            {
                _result = result;
            }

            public BTResult Evaluate()
            {
                ++EvaluateCount;
                return _result;
            }
        }

        /****************************************
        *              Selector Tests
        ****************************************/

        [Test]
        public void SelectorTriesNextChildAfterFailure()
        {
            var first  = new StubNode(BTResult.Failure);
            var second = new StubNode(BTResult.Success);
            var selector = new SelectorNode(first, second);

            BTResult result = selector.Evaluate();

            Assert.AreEqual(BTResult.Success, result);
            Assert.AreEqual(1, first.EvaluateCount);
            Assert.AreEqual(1, second.EvaluateCount);
        }

        [Test]
        public void SelectorStopsAfterSuccess()
        {
            var first  = new StubNode(BTResult.Success);
            var second = new StubNode(BTResult.Success);
            var selector = new SelectorNode(first, second);

            BTResult result = selector.Evaluate();

            Assert.AreEqual(BTResult.Success, result);
            Assert.AreEqual(1, first.EvaluateCount);
            Assert.AreEqual(0, second.EvaluateCount);
        }

        [Test]
        public void SelectorStopsAfterBlocked()
        {
            var first  = new StubNode(BTResult.Blocked);
            var second = new StubNode(BTResult.Success);
            var selector = new SelectorNode(first, second);

            BTResult result = selector.Evaluate();

            Assert.AreEqual(BTResult.Blocked, result);
            Assert.AreEqual(1, first.EvaluateCount);
            Assert.AreEqual(0, second.EvaluateCount);
        }

        [Test]
        public void SelectorReturnsFailureWhenAllChildrenFail()
        {
            var first  = new StubNode(BTResult.Failure);
            var second = new StubNode(BTResult.Failure);
            var selector = new SelectorNode(first, second);

            BTResult result = selector.Evaluate();

            Assert.AreEqual(BTResult.Failure, result);
            Assert.AreEqual(1, first.EvaluateCount);
            Assert.AreEqual(1, second.EvaluateCount);
        }

        /****************************************
        *              Sequence Tests
        ****************************************/

        [Test]
        public void SequenceStopsAfterFailure()
        {
            var first  = new StubNode(BTResult.Failure);
            var second = new StubNode(BTResult.Success);
            var sequence = new SequenceNode(first, second);

            BTResult result = sequence.Evaluate();

            Assert.AreEqual(BTResult.Failure, result);
            Assert.AreEqual(1, first.EvaluateCount);
            Assert.AreEqual(0, second.EvaluateCount);
        }

        [Test]
        public void SequenceStopsAfterBlocked()
        {
            var first  = new StubNode(BTResult.Blocked);
            var second = new StubNode(BTResult.Success);
            var sequence = new SequenceNode(first, second);

            BTResult result = sequence.Evaluate();

            Assert.AreEqual(BTResult.Blocked, result);
            Assert.AreEqual(1, first.EvaluateCount);
            Assert.AreEqual(0, second.EvaluateCount);
        }

        [Test]
        public void SequenceReturnsSuccessWhenAllChildrenSucceed()
        {
            var first  = new StubNode(BTResult.Success);
            var second = new StubNode(BTResult.Success);
            var sequence = new SequenceNode(first, second);

            BTResult result = sequence.Evaluate();

            Assert.AreEqual(BTResult.Success, result);
            Assert.AreEqual(1, first.EvaluateCount);
            Assert.AreEqual(1, second.EvaluateCount);
        }

        /****************************************
        *   Phase1 트리 모양 재현 (Opening/ForcedFollowUp/GeneralAttack)
        ****************************************/

        // 실제 Combat BT의 PriorityBranch(Selector) 모양을 스텁으로 재현한다. Opening이
        // 데이터 조회 실패로 Blocked를 반환하면 ForcedFollowUp/GeneralAttack까지 내려가지
        // 않는다 - Stage 3 재검토에서 확정한 핵심 의미론(기존 TickIdle의 단순 return과 동일)
        [Test]
        public void OpeningBlockedDoesNotFallThroughToForcedFollowUpOrGeneralAttack()
        {
            var opening         = new StubNode(BTResult.Blocked);
            var forcedFollowUp  = new StubNode(BTResult.Success);
            var generalAttack   = new StubNode(BTResult.Success);
            var priorityBranch  = new SelectorNode(opening, forcedFollowUp, generalAttack);

            BTResult result = priorityBranch.Evaluate();

            Assert.AreEqual(BTResult.Blocked, result);
            Assert.AreEqual(1, opening.EvaluateCount);
            Assert.AreEqual(0, forcedFollowUp.EvaluateCount);
            Assert.AreEqual(0, generalAttack.EvaluateCount);
        }

        // 대조군: Opening이 Failure(오프닝 비활성)면 정상적으로 다음 branch로 흘러간다
        [Test]
        public void OpeningFailureFallsThroughToForcedFollowUp()
        {
            var opening         = new StubNode(BTResult.Failure);
            var forcedFollowUp  = new StubNode(BTResult.Success);
            var generalAttack   = new StubNode(BTResult.Success);
            var priorityBranch  = new SelectorNode(opening, forcedFollowUp, generalAttack);

            BTResult result = priorityBranch.Evaluate();

            Assert.AreEqual(BTResult.Success, result);
            Assert.AreEqual(1, opening.EvaluateCount);
            Assert.AreEqual(1, forcedFollowUp.EvaluateCount);
            Assert.AreEqual(0, generalAttack.EvaluateCount);
        }
    }
}
