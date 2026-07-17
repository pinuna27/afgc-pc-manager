using AFGCPCManager.Core.Output;
using AFGCPCManager.VJoy.Native;

namespace AFGCPCManager.VJoy;

internal sealed class VJoyOutputSession(IVJoyNativeApi api, uint deviceId, IReadOnlyDictionary<VirtualAxis, AxisRange> ranges, Action released) : IGamepadOutputSession
{
    private bool _disposed;
    public uint DeviceId { get; } = deviceId;

    public ValueTask WriteAsync(VirtualGamepadState state, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var position = VJoyStateConverter.Convert(DeviceId, state, ranges);
        if (!api.Update(DeviceId, ref position)) throw new VJoyException($"vJoy rejected an update for device {DeviceId}.");
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        try { var neutral = VJoyStateConverter.Convert(DeviceId, VirtualGamepadState.Neutral, ranges); api.Update(DeviceId, ref neutral); }
        finally { api.Relinquish(DeviceId); released(); _disposed = true; }
    }
}
