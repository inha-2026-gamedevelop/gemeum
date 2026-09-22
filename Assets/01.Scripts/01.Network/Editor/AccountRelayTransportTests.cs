using System;
using NUnit.Framework;
using Geumeum.Network;

/// <summary>
/// 릴레이가 보낸 자리 재발급 통지를 호스트가 언제 적용하는지의 경계 규칙.
///
/// 통지는 UDP 한 발이라 유실 · 중복 · 지연이 모두 가능하다. 여기서 지키려는 것은
/// "이 통지가 끊는 것은 언제나 재발급 이전의 연결"이라는 한 가지다. 비교식만이 아니라
/// 번호를 실제로 쌓아 두는 객체를 그대로 돌려서, 기록 경로가 기준을 끌어내리지 않는지 본다
/// </summary>
public class AccountRelayTransportTests
{
    private static ArraySegment<byte> Notice(int seat)
    {
        // 앞뒤에 다른 바이트를 붙여 offset을 무시하고 읽지는 않는지도 같이 본다
        byte[] buffer = new byte[10];
        Buffer.BlockCopy(BitConverter.GetBytes(seat), 0, buffer, 3, 4);
        return new ArraySegment<byte>(buffer, 3, 4);
    }

    [Test]
    public void ReleasesTheSeatWhenNoNewerTrafficArrivedOnIt()
    {
        var seats = new AccountRelayTransport.SeatSequences();
        seats.Observe(1, 39UL);
        Assert.IsTrue(seats.TryRelease(Notice(1), 40UL, out int seat));
        Assert.AreEqual(1, seat);

        // 자리는 서로 독립이다. 1번을 놓아줬다고 2번 기준이 움직이지 않는다
        Assert.AreEqual(0UL, seats.Highest(2));
        Assert.IsTrue(seats.TryRelease(Notice(2), 5UL, out seat));
        Assert.AreEqual(2, seat);
    }

    [Test]
    public void LateLowNumberedDataNeverLowersTheBarForAStaleNotice()
    {
        // 새 연결이 100번으로 말을 걸었고, 그 뒤에 예전 연결의 80번 데이터가 늦게 도착한다.
        // 기준이 80으로 내려가면 그 사이 번호인 90번 통지가 방금 선 연결을 끊어 버린다
        var seats = new AccountRelayTransport.SeatSequences();
        seats.Observe(1, 100UL);
        seats.Observe(1, 80UL);
        Assert.AreEqual(100UL, seats.Highest(1));
        Assert.IsFalse(seats.TryRelease(Notice(1), 90UL, out int seat));
        Assert.AreEqual(1, seat);
        Assert.AreEqual(100UL, seats.Highest(1));
    }

    [Test]
    public void ReleasingRaisesTheBarInsteadOfResettingIt()
    {
        // 놓아준 뒤 기준이 0으로 돌아가면, 그보다 낮은 묵은 통지가 다시 통과한다
        var seats = new AccountRelayTransport.SeatSequences();
        seats.Observe(1, 80UL);
        Assert.IsTrue(seats.TryRelease(Notice(1), 90UL, out _));
        Assert.AreEqual(90UL, seats.Highest(1));
        Assert.IsFalse(seats.TryRelease(Notice(1), 85UL, out _));
        Assert.IsFalse(seats.TryRelease(Notice(1), 90UL, out _));

        // 돌아온 클라이언트가 말을 걸면 기준이 다시 오르고, 반복 사본은 전부 막힌다
        seats.Observe(1, 120UL);
        Assert.IsFalse(seats.TryRelease(Notice(1), 95UL, out _));
        Assert.AreEqual(120UL, seats.Highest(1));
    }

    [Test]
    public void IgnoresMalformedOrOutOfRangeSeats()
    {
        var seats = new AccountRelayTransport.SeatSequences();
        Assert.IsFalse(seats.TryRelease(Notice(0), 40UL, out _));
        Assert.IsFalse(seats.TryRelease(Notice(3), 40UL, out _));
        Assert.IsFalse(seats.TryRelease(Notice(-1), 40UL, out _));
        Assert.IsFalse(seats.TryRelease(new ArraySegment<byte>(new byte[8]), 40UL, out _));
        Assert.IsFalse(seats.TryRelease(default, 40UL, out _));
        Assert.AreEqual(0UL, seats.Highest(1));
    }

    [Test]
    public void LosingTheNoticeChangesNothingOnItsOwn()
    {
        // 유실은 판정이 아예 일어나지 않는 것과 같다. 그 자리의 낡은 연결은 예전처럼
        // transport timeout으로 정리되고, 잘못 끊기는 연결은 생기지 않는다
        var seats = new AccountRelayTransport.SeatSequences();
        seats.Observe(1, 100UL);
        Assert.AreEqual(100UL, seats.Highest(1));
        seats.Clear();
        Assert.AreEqual(0UL, seats.Highest(1));
    }
}
