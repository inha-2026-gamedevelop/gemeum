// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Network;

/// <summary> 서버 피해 결과 대기열의 순수 규칙(COMBAT_STYLE_DESIGN 8.1) </summary>
public class DamageResultQueueTests
{
    private const uint  ATTACKER = 7u;
    private const uint  OTHER_ATTACKER = 9u;
    private const uint  TARGET   = 42u;
    private const float DELTA    = 0.0001f;

    private static DamageInfo MakeDamage(uint attacker = ATTACKER)
    {
        return new DamageInfo(10f, attacker, new ParticipantId(70UL), HitStrength.Light);
    }

    private static bool Enqueue(DamageResultQueue queue, in DamageInfo damage, float applied,
        out DamageResult result, bool isKill = false, sbyte partIndex = DamageResult.NO_PART,
        bool targetIsBoss = false)
    {
        return queue.TryEnqueue(damage, TARGET, partIndex, targetIsBoss, applied, isKill,
            new Vector3(0f, 1f, 5f), Vector3.zero, 12.5d, 340.25d, out result);
    }

    /// <summary> 사건 번호는 결과마다 하나씩 늘어난다 </summary>
    [Test]
    public void EventIdIncreasesPerResult()
    {
        DamageResultQueue queue = new DamageResultQueue();

        Enqueue(queue, MakeDamage(), 5f, out DamageResult first);
        Enqueue(queue, MakeDamage(), 5f, out DamageResult second);
        Enqueue(queue, MakeDamage(OTHER_ATTACKER), 5f, out DamageResult third);

        Assert.AreEqual(1u, first.EventId);
        Assert.AreEqual(2u, second.EventId);
        Assert.AreEqual(3u, third.EventId);
    }

    /// <summary> 번호가 한 바퀴 돌아도 0("없음")은 발급하지 않는다 </summary>
    [Test]
    public void EventIdWrapsWithoutZero()
    {
        DamageResultQueue queue = new DamageResultQueue(8, uint.MaxValue);

        Enqueue(queue, MakeDamage(), 5f, out DamageResult last);
        Enqueue(queue, MakeDamage(), 5f, out DamageResult wrapped);

        Assert.AreEqual(uint.MaxValue, last.EventId);
        Assert.AreEqual(1u, wrapped.EventId);
    }

    /// <summary> 실제로 줄지 않은 피해(0, 오차 이하, 비정상 값)는 결과를 만들지 않고 번호도 쓰지 않는다 </summary>
    [Test]
    public void ZeroAppliedDamageIsIgnored()
    {
        DamageResultQueue queue = new DamageResultQueue();

        Assert.IsFalse(Enqueue(queue, MakeDamage(), 0f, out _));
        Assert.IsFalse(Enqueue(queue, MakeDamage(), Constants.Combat.DAMAGE_EPSILON, out _));
        Assert.IsFalse(Enqueue(queue, MakeDamage(), float.NaN, out _));
        Assert.IsFalse(Enqueue(queue, MakeDamage(), -3f, out _));

        Assert.AreEqual(0, queue.GetPendingCount(ATTACKER));
        Assert.AreEqual(1u, queue.NextEventId);
    }

    /// <summary> 가해자가 없는 환경 피해는 받을 사람이 없다 </summary>
    [Test]
    public void EnvironmentDamageIsIgnored()
    {
        DamageResultQueue queue = new DamageResultQueue();

        Assert.IsFalse(Enqueue(queue, DamageInfo.FromEnvironment(10f), 5f, out _));
        Assert.AreEqual(0, queue.GetPendingCount(DamageInfo.NO_ATTACKER));
    }

    /// <summary> 처치·부위·보스 여부와 실제 감소량이 그대로 실린다 </summary>
    [Test]
    public void KillFlagAndTargetFieldsAreCarried()
    {
        DamageResultQueue queue = new DamageResultQueue();

        Enqueue(queue, MakeDamage(), 3.25f, out DamageResult killed, isKill: true);
        Enqueue(queue, MakeDamage(), 4f, out DamageResult tail, isKill: false, partIndex: 2, targetIsBoss: true);

        Assert.IsTrue(killed.IsKill);
        Assert.AreEqual(3.25f, killed.Amount, DELTA);
        Assert.AreEqual(DamageResult.NO_PART, killed.PartIndex);
        Assert.IsFalse(killed.TargetIsBoss);

        Assert.IsFalse(tail.IsKill);
        Assert.AreEqual((sbyte) 2, tail.PartIndex);
        Assert.IsTrue(tail.TargetIsBoss);
        Assert.AreEqual(TARGET, tail.TargetNetId);
        Assert.AreEqual(ATTACKER, tail.AttackerNetId);
        Assert.AreEqual(12.5d, tail.GameplayTime);
    }

    /// <summary> 늦은 결과 판정에 쓰는 서버 시각은 게임 시각과 따로 실린다 </summary>
    [Test]
    public void ServerTimeIsCarriedSeparatelyFromGameplayTime()
    {
        DamageResultQueue queue = new DamageResultQueue();

        Enqueue(queue, MakeDamage(), 5f, out DamageResult result);

        Assert.AreEqual(12.5d, result.GameplayTime);
        Assert.AreEqual(340.25d, result.ServerTime);
    }

    /// <summary> 가해 지점이 붙인 행동 정보가 결과로 옮겨진다 </summary>
    [Test]
    public void SourceMetadataIsCarried()
    {
        DamageResultQueue queue = new DamageResultQueue();
        DamageInfo damage = MakeDamage().WithSource(DamageSourceKind.CounterSlash, 55u, 2, true);

        Enqueue(queue, damage, 5f, out DamageResult result);

        Assert.AreEqual(DamageSourceKind.CounterSlash, result.Source);
        Assert.AreEqual(55u, result.ExecutionId);
        Assert.AreEqual((byte) 2, result.Detail);
        Assert.IsTrue(result.IsCritical);
    }

    /// <summary> 기존 생성자는 선택 정보를 비워 둔다 </summary>
    [Test]
    public void DamageInfoDefaultsAreEmpty()
    {
        DamageInfo damage = MakeDamage();

        Assert.AreEqual(DamageSourceKind.Unknown, damage.Source);
        Assert.AreEqual(0u, damage.ExecutionId);
        Assert.AreEqual((byte) 0, damage.Detail);
        Assert.IsFalse(damage.IsCritical);
        Assert.IsFalse(damage.HasHitPoint);
        Assert.IsFalse(MakeDamage().WithHitPoint(new Vector3(float.NaN, 0f, 0f)).HasHitPoint);
    }

    /// <summary> 적중점이 있으면 그 값을, 없으면 대상 중심을 쓴다 </summary>
    [Test]
    public void HitPointFallsBackToTargetCenter()
    {
        DamageResultQueue queue = new DamageResultQueue();
        Vector3 contact = new Vector3(1f, 2f, 3f);

        Enqueue(queue, MakeDamage().WithHitPoint(contact), 5f, out DamageResult withPoint);
        Enqueue(queue, MakeDamage(), 5f, out DamageResult withoutPoint);

        Assert.AreEqual(contact, withPoint.HitPoint);
        Assert.AreEqual(new Vector3(0f, 1f, 5f), withoutPoint.HitPoint);
    }

    /// <summary> 방향은 가해자에서 대상으로 향하는 수평 단위 벡터다 </summary>
    [Test]
    public void HitDirectionIsHorizontalAndNormalized()
    {
        Vector3 direction = DamageResultQueue.ResolveHitDirection(Vector3.zero, new Vector3(3f, 5f, 4f));

        Assert.AreEqual(0.6f, direction.x, DELTA);
        Assert.AreEqual(0f, direction.y, DELTA);
        Assert.AreEqual(0.8f, direction.z, DELTA);
        Assert.AreEqual(Vector3.zero, DamageResultQueue.ResolveHitDirection(Vector3.zero, Vector3.up * 2f));
        Assert.AreEqual(Vector3.zero, DamageResultQueue.ResolveHitDirection(Vector3.one, Vector3.one));
    }

    /// <summary> 결과에 실린 방향은 대상 중심 기준이다 </summary>
    [Test]
    public void EnqueuedResultCarriesDirection()
    {
        DamageResultQueue queue = new DamageResultQueue();

        Enqueue(queue, MakeDamage(), 5f, out DamageResult result);

        Assert.AreEqual(0f, result.HitDirection.x, DELTA);
        Assert.AreEqual(0f, result.HitDirection.y, DELTA);
        Assert.AreEqual(1f, result.HitDirection.z, DELTA);
    }

    /// <summary> 상한을 넘으면 가장 오래된 결과부터 버리고 버린 수를 한 번 알려 준다 </summary>
    [Test]
    public void CapacityDropsOldestAndReportsOnce()
    {
        DamageResultQueue queue = new DamageResultQueue(4, 1u);
        for (int i = 0; i < 6; ++i)
        {
            Enqueue(queue, MakeDamage(), 5f, out _);
        }

        DamageResult[] buffer = new DamageResult[8];
        int count = queue.Drain(ATTACKER, buffer, out int dropped);

        Assert.AreEqual(4, count);
        Assert.AreEqual(2, dropped);
        Assert.AreEqual(3u, buffer[0].EventId);
        Assert.AreEqual(6u, buffer[3].EventId);

        queue.Drain(ATTACKER, buffer, out int droppedAgain);
        Assert.AreEqual(0, droppedAgain);
    }

    /// <summary> 한 번에 버퍼 크기만큼만 나가고 나머지는 다음 전송에 순서대로 나간다 </summary>
    [Test]
    public void DrainIsLimitedByBufferSize()
    {
        DamageResultQueue queue = new DamageResultQueue();
        for (int i = 0; i < 5; ++i)
        {
            Enqueue(queue, MakeDamage(), 5f, out _);
        }

        DamageResult[] buffer = new DamageResult[2];

        Assert.AreEqual(2, queue.Drain(ATTACKER, buffer, out _));
        Assert.AreEqual(2u, buffer[1].EventId);
        Assert.AreEqual(2, queue.Drain(ATTACKER, buffer, out _));
        Assert.AreEqual(4u, buffer[1].EventId);
        Assert.AreEqual(1, queue.Drain(ATTACKER, buffer, out _));
        Assert.AreEqual(5u, buffer[0].EventId);
        Assert.AreEqual(0, queue.Drain(ATTACKER, buffer, out _));
    }

    /// <summary> 공격자별 대기열은 서로 섞이지 않고 한 명만 비울 수 있다. 번호는 되돌아가지 않는다 </summary>
    [Test]
    public void ClearAttackerKeepsOthersAndEventIds()
    {
        DamageResultQueue queue = new DamageResultQueue();
        Enqueue(queue, MakeDamage(), 5f, out _);
        Enqueue(queue, MakeDamage(OTHER_ATTACKER), 5f, out _);

        queue.ClearAttacker(ATTACKER);

        Assert.AreEqual(0, queue.GetPendingCount(ATTACKER));
        Assert.AreEqual(1, queue.GetPendingCount(OTHER_ATTACKER));

        queue.Clear();
        Enqueue(queue, MakeDamage(), 5f, out DamageResult afterClear);

        Assert.AreEqual(0, queue.GetPendingCount(OTHER_ATTACKER));
        Assert.AreEqual(3u, afterClear.EventId);
    }

    /// <summary> 서버가 돌지 않으면 허브는 아무것도 쌓지 않는다 </summary>
    [Test]
    public void HubIgnoresReportsWithoutActiveServer()
    {
        ServerDamageResultHub.ResetStatic();
        try
        {
            ServerDamageResultHub.ServerReport(MakeDamage(), TARGET, DamageResult.NO_PART, false,
                5f, false, Vector3.zero);

            Assert.AreEqual(0, ServerDamageResultHub.GetPendingCount(ATTACKER));
        }
        finally
        {
            ServerDamageResultHub.ResetStatic();
        }
    }
}
