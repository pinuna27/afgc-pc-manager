using AFGCPCManager.Core.Output;

namespace AFGCPCManager.ViGEm;

internal sealed class ViGEmOutputSession(
    IXbox360TargetApi target, uint deviceId, Action released) : IGamepadOutputSession
{
    private bool _disposed;
    public uint DeviceId { get; } = deviceId;

    public ValueTask WriteAsync(VirtualGamepadState state,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        target.Submit(ViGEmStateConverter.Convert(state));
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        try { target.Submit(ViGEmStateConverter.Convert(VirtualGamepadState.Neutral)); }
        finally
        {
            try { target.Dispose(); }
            finally
            {
                released();
                _disposed = true;
            }
        }
    }
}
