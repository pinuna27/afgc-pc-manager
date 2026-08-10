namespace AFGCPCManager.Windows.Consumer;

internal sealed class MediaSeekChain
{
    private static readonly TimeSpan ChainWindow = TimeSpan.FromSeconds(5);
    private static readonly long CatchUpToleranceTicks = TimeSpan.FromSeconds(2).Ticks;
    private string? _sessionId;
    private long? _lastRequestedPosition;
    private DateTimeOffset _lastRequestTime;

    public long Next(
        string sessionId,
        long reportedPosition,
        long minimum,
        long maximum,
        TimeSpan offset,
        DateTimeOffset now)
    {
        long basePosition = reportedPosition;
        if (_lastRequestedPosition is long previous &&
            string.Equals(_sessionId, sessionId, StringComparison.Ordinal) &&
            now >= _lastRequestTime && now - _lastRequestTime <= ChainWindow &&
            Math.Abs((double)reportedPosition - previous) > CatchUpToleranceTicks)
        {
            // Some players acknowledge a seek before publishing the new timeline.
            // Chain from our accepted target until their reported position catches up.
            basePosition = previous;
        }

        double requested = (double)basePosition + offset.Ticks;
        return (long)Math.Clamp(requested, minimum, maximum);
    }

    public void Commit(string sessionId, long requestedPosition, DateTimeOffset now)
    {
        _sessionId = sessionId;
        _lastRequestedPosition = requestedPosition;
        _lastRequestTime = now;
    }

    public void Reset()
    {
        _sessionId = null;
        _lastRequestedPosition = null;
        _lastRequestTime = default;
    }
}
