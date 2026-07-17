namespace AFGCPCManager.Core.Output;

public interface IGamepadOutputSession : IDisposable
{
    uint DeviceId { get; }
    ValueTask WriteAsync(VirtualGamepadState state, CancellationToken cancellationToken = default);
}
