using NUnit.Framework;
using UnityEngine;
using Geumeum.Systems;

public class BarrierJudgeTests
{
    /// <summary> 결계 밖 판정 기준선. 반경 10, 중심 원점일 때 15m 지점은 밖이다 </summary>
    [Test]
    public void IsOutsideDetectsPointBeyondRadius()
    {
        var state = new BarrierTransitionState
        {
            FromCenter      = Vector3.zero,
            TargetCenter    = Vector3.zero,
            FromRadius      = 10f,
            TargetRadius    = 10f,
            StartServerTime = 0.0,
            EndServerTime   = 1.0,
        };

        bool outside = BarrierJudge.IsOutside(new Vector3(0f, 0f, 15f), state, 5.0);

        Assert.IsTrue(outside);
    }
}