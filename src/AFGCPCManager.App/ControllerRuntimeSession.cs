using AFGCPCManager.Core.Bridge;

namespace AFGCPCManager.App;

internal sealed class ControllerRuntimeSession(
    ControllerBridge bridge,
    uint deviceId,
    string controllerId,
    Action<string, Exception?> stopped) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly object _lifetimeGate = new();
    private Task? _task;
    private Task? _disposeTask;

    public uint DeviceId { get; } = deviceId;
    public byte? BatteryPercentage => bridge.BatteryPercentage;
    public bool IsCompleted => _task?.IsCompleted == true;

    public void Start()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            _task ??= RunAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifetimeGate)
            return new(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task RunAsync()
    {
        try
        {
            await bridge.RunAsync(_stop.Token).ConfigureAwait(false);
            stopped(controllerId, null);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            stopped(controllerId, ex);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_task is not null)
            await _task.ConfigureAwait(false);
        await bridge.DisposeAsync().ConfigureAwait(false);
        _stop.Dispose();
    }
}
