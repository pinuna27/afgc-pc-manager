using AFGCPCManager.Windows.Consumer;

namespace AFGCPCManager.Windows.Tests.Consumer;

public sealed class WindowsMediaSessionSeekerTests
{
    [Fact]
    public void RewindUsesCurrentPlayingPositionAndClampsToStart()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-10T19:00:05Z");

        long current = WindowsMediaSessionSeeker.EstimateCurrentPositionTicks(
            TimeSpan.FromSeconds(3), now.AddSeconds(-2), TimeSpan.Zero,
            TimeSpan.FromMinutes(2), true, 1, now);
        var chain = new MediaSeekChain();
        long requested = chain.Next("player", current, TimeSpan.Zero.Ticks,
            TimeSpan.FromMinutes(2).Ticks, TimeSpan.FromSeconds(-10), now);

        Assert.Equal(TimeSpan.Zero.Ticks, requested);
    }

    [Fact]
    public void FastForwardUsesElapsedPlaybackAndClampsToEnd()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-10T19:00:05Z");

        long current = WindowsMediaSessionSeeker.EstimateCurrentPositionTicks(
            TimeSpan.FromSeconds(45), now.AddSeconds(-5), TimeSpan.Zero,
            TimeSpan.FromSeconds(55), true, 1, now);
        var chain = new MediaSeekChain();
        long requested = chain.Next("player", current, TimeSpan.Zero.Ticks,
            TimeSpan.FromSeconds(55).Ticks, TimeSpan.FromSeconds(10), now);

        Assert.Equal(TimeSpan.FromSeconds(55).Ticks, requested);
    }

    [Fact]
    public void PausedPlaybackDoesNotAccumulateElapsedTime()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-10T19:00:05Z");

        long current = WindowsMediaSessionSeeker.EstimateCurrentPositionTicks(
            TimeSpan.FromSeconds(30), now.AddMinutes(-1), TimeSpan.Zero,
            TimeSpan.FromMinutes(2), false, null, now);
        var chain = new MediaSeekChain();
        long requested = chain.Next("player", current, TimeSpan.Zero.Ticks,
            TimeSpan.FromMinutes(2).Ticks, TimeSpan.FromSeconds(10), now);

        Assert.Equal(TimeSpan.FromSeconds(40).Ticks, requested);
    }
}
