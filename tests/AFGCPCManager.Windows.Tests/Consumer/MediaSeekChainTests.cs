using AFGCPCManager.Windows.Consumer;

namespace AFGCPCManager.Windows.Tests.Consumer;

public sealed class MediaSeekChainTests
{
    [Fact]
    public void RapidForwardPressesAccumulateWhilePlayerTimelineIsStale()
    {
        var chain = new MediaSeekChain();
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-10T19:00:00Z");
        long first = chain.Next("photos", Seconds(20), 0, Seconds(60),
            TimeSpan.FromSeconds(10), now);
        chain.Commit("photos", first, now);

        long second = chain.Next("photos", Seconds(20), 0, Seconds(60),
            TimeSpan.FromSeconds(10), now.AddMilliseconds(200));
        chain.Commit("photos", second, now.AddMilliseconds(200));
        long third = chain.Next("photos", Seconds(20), 0, Seconds(60),
            TimeSpan.FromSeconds(10), now.AddMilliseconds(400));

        Assert.Equal(Seconds(40), second);
        Assert.Equal(Seconds(50), third);
    }

    [Fact]
    public void RapidRewindPressesAccumulateWhilePlayerTimelineIsStale()
    {
        var chain = new MediaSeekChain();
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-10T19:00:00Z");
        long first = chain.Next("photos", Seconds(50), 0, Seconds(60),
            TimeSpan.FromSeconds(-10), now);
        chain.Commit("photos", first, now);

        long second = chain.Next("photos", Seconds(50), 0, Seconds(60),
            TimeSpan.FromSeconds(-10), now.AddMilliseconds(200));

        Assert.Equal(Seconds(30), second);
    }

    [Fact]
    public void OppositeDirectionStartsFromLastAcceptedTargetWhenTimelineIsStale()
    {
        var chain = new MediaSeekChain();
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-10T19:00:00Z");
        long forward = chain.Next("photos", Seconds(20), 0, Seconds(60),
            TimeSpan.FromSeconds(10), now);
        chain.Commit("photos", forward, now);

        long rewind = chain.Next("photos", Seconds(20), 0, Seconds(60),
            TimeSpan.FromSeconds(-10), now.AddMilliseconds(200));

        Assert.Equal(Seconds(20), rewind);
    }

    [Fact]
    public void FreshPlayerTimelineSupersedesTheAcceptedTarget()
    {
        var chain = new MediaSeekChain();
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-10T19:00:00Z");
        chain.Commit("photos", Seconds(30), now);

        long requested = chain.Next("photos", Seconds(31), 0, Seconds(60),
            TimeSpan.FromSeconds(10), now.AddSeconds(1));

        Assert.Equal(Seconds(41), requested);
    }

    [Fact]
    public void ExpiredChainUsesThePlayersReportedPosition()
    {
        var chain = new MediaSeekChain();
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-10T19:00:00Z");
        chain.Commit("photos", Seconds(40), now);

        long requested = chain.Next("photos", Seconds(15), 0, Seconds(60),
            TimeSpan.FromSeconds(10), now.AddSeconds(6));

        Assert.Equal(Seconds(25), requested);
    }

    private static long Seconds(int value) => TimeSpan.FromSeconds(value).Ticks;
}
