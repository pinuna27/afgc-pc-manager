using AFGCPCManager.Core.Output;
using AFGCPCManager.VJoy.Native;

namespace AFGCPCManager.VJoy;

internal sealed class VJoyOutputSession(IVJoyNativeApi api, uint deviceId, IReadOnlyDictionary<VirtualAxis, AxisRange> ranges, Action released) : IGamepadOutputSession
{
    private readonly object _gate = new();
    private bool _disposed;
    public uint DeviceId { get; } = deviceId;

    public ValueTask WriteAsync(VirtualGamepadState state, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            var position = VJoyStateConverter.Convert(DeviceId, state, ranges);
            if (!api.Update(DeviceId, ref position))
                throw new VJoyException(
                    $"vJoy rejected an update for device {DeviceId}.");
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
            try
            {
                var neutral = VJoyStateConverter.Convert(
                    DeviceId, VirtualGamepadState.Neutral, ranges);
                api.Update(DeviceId, ref neutral);
            }
            catch (Exception ex) { failures.Add(ex); }
            try { api.Reset(DeviceId); }
            catch (Exception ex) { failures.Add(ex); }
            try { api.Relinquish(DeviceId); }
            catch (Exception ex) { failures.Add(ex); }
            finally { released(); }

            if (failures.Count > 0)
            {
                if (failures.Count == 1)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo
                        .Capture(failures[0]).Throw();
                throw new AggregateException("vJoy output cleanup failed.", failures);
            }
        }
    }
}
