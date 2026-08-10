using Windows.Media.Control;

namespace AFGCPCManager.Windows.Consumer;

internal sealed class WindowsMediaSessionSeeker
{
    private readonly object _managerLock = new();
    private readonly SemaphoreSlim _seekGate = new(1, 1);
    private readonly MediaSeekChain _seekChain = new();
    private Task<GlobalSystemMediaTransportControlsSessionManager>? _managerTask;

    public async ValueTask<bool> SeekByAsync(
        TimeSpan offset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _seekGate.WaitAsync(cancellationToken);
        try
        {
            GlobalSystemMediaTransportControlsSessionManager manager =
                await GetManagerAsync().WaitAsync(cancellationToken);
            GlobalSystemMediaTransportControlsSession? session = manager.GetCurrentSession();
            if (session is null) return false;

            GlobalSystemMediaTransportControlsSessionTimelineProperties timeline =
                session.GetTimelineProperties();
            if (timeline.MaxSeekTime <= timeline.MinSeekTime) return false;

            GlobalSystemMediaTransportControlsSessionPlaybackInfo playback =
                session.GetPlaybackInfo();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            long currentPosition = EstimateCurrentPositionTicks(
                timeline.Position,
                timeline.LastUpdatedTime,
                timeline.MinSeekTime,
                timeline.MaxSeekTime,
                playback.PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                playback.PlaybackRate,
                now);
            long requestedPosition = _seekChain.Next(
                session.SourceAppUserModelId,
                currentPosition,
                timeline.MinSeekTime.Ticks,
                timeline.MaxSeekTime.Ticks,
                offset,
                now);

            bool changed = await session.TryChangePlaybackPositionAsync(requestedPosition);
            if (changed)
                _seekChain.Commit(session.SourceAppUserModelId, requestedPosition, now);
            else
                _seekChain.Reset();
            return changed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            _seekChain.Reset();
            ResetFailedManagerRequest();
            return false;
        }
        finally
        {
            _seekGate.Release();
        }
    }

    internal static long EstimateCurrentPositionTicks(
        TimeSpan reportedPosition,
        DateTimeOffset lastUpdatedTime,
        TimeSpan minimum,
        TimeSpan maximum,
        bool isPlaying,
        double? playbackRate,
        DateTimeOffset now)
    {
        double currentTicks = reportedPosition.Ticks;
        if (isPlaying && lastUpdatedTime != default && now > lastUpdatedTime)
        {
            double rate = playbackRate is > 0 and <= 16 ? playbackRate.Value : 1;
            currentTicks += (now - lastUpdatedTime).Ticks * rate;
        }

        return (long)Math.Clamp(currentTicks, minimum.Ticks, maximum.Ticks);
    }

    private Task<GlobalSystemMediaTransportControlsSessionManager> GetManagerAsync()
    {
        lock (_managerLock)
            return _managerTask ??= RequestManagerAsync();
    }

    private void ResetFailedManagerRequest()
    {
        lock (_managerLock)
        {
            if (_managerTask?.IsFaulted == true || _managerTask?.IsCanceled == true)
                _managerTask = null;
        }
    }

    private static async Task<GlobalSystemMediaTransportControlsSessionManager>
        RequestManagerAsync() =>
        await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
}
