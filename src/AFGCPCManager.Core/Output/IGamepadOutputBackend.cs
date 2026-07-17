namespace AFGCPCManager.Core.Output;

public interface IGamepadOutputBackend : IDisposable
{
    IReadOnlyList<OutputDeviceInfo> EnumerateDevices();
    IGamepadOutputSession? TryAcquire(uint? preferredDeviceId = null);
}
