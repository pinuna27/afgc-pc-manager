using AFGCPCManager.Core.Output;

namespace AFGCPCManager.ViGEm;

internal sealed class ViGEmOutputSession(
    IXbox360TargetApi target, uint deviceId, Action released) : IGamepadOutputSession
{
    private readonly object _gate = new();
    private bool _disposed;
    public uint DeviceId { get; } = deviceId;

    public ValueTask WriteAsync(VirtualGamepadState state,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            target.Submit(ViGEmStateConverter.Convert(state));
            return ValueTask.CompletedTask;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            List<Exception> failures = [];
            try { target.Submit(ViGEmStateConverter.Convert(VirtualGamepadState.Neutral)); }
            catch (Exception ex) { failures.Add(ex); }
            try { target.Dispose(); }
            catch (Exception ex) { failures.Add(ex); }
            finally { released(); }
            NativeViGEmClient.ThrowCleanupFailures(
                failures, "ViGEm output cleanup failed.");
        }
    }
}
