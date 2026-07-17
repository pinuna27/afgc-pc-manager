using AFGCPCManager.Core.Output;
using AFGCPCManager.VJoy.Native;

namespace AFGCPCManager.VJoy.Tests;

public sealed class VJoyBackendTests
{
    [Fact]
    public void AcquiresPreferredCompatibleDeviceAndWritesNeutral()
    {
        var api = new FakeVJoyNativeApi(); api.Statuses[2] = VJoyDeviceStatus.Free; api.Statuses[5] = VJoyDeviceStatus.Free;
        using var backend = new VJoyBackend(api);
        using var session = backend.TryAcquire(5);
        Assert.Equal((uint)5, session!.DeviceId);
        Assert.Equal([(uint)5], api.Acquired);
        Assert.Single(api.Updates);
        Assert.Equal(16384, api.Updates[0].AxisX);
    }

    [Fact]
    public void SkipsBusyPreferredDevice()
    {
        var api = new FakeVJoyNativeApi(); api.Statuses[1] = VJoyDeviceStatus.Free; api.Statuses[4] = VJoyDeviceStatus.Busy;
        using var backend = new VJoyBackend(api);
        using var session = backend.TryAcquire(4);
        Assert.Equal((uint)1, session!.DeviceId);
    }

    [Theory] [InlineData(10, 1)] [InlineData(11, 0)]
    public void RejectsInsufficientCapabilities(int buttons, int povs)
    {
        var api = new FakeVJoyNativeApi { Buttons = buttons, Povs = povs }; api.Statuses[1] = VJoyDeviceStatus.Free;
        using var backend = new VJoyBackend(api);
        Assert.Null(backend.TryAcquire());
        Assert.Null(backend.EnumerateDevices()[0].Capabilities);
    }

    [Fact]
    public void DisposeNeutralizesAndRelinquishes()
    {
        var api = new FakeVJoyNativeApi(); api.Statuses[1] = VJoyDeviceStatus.Free;
        using var backend = new VJoyBackend(api);
        var session = backend.TryAcquire()!;
        session.Dispose();
        Assert.Equal(2, api.Updates.Count);
        Assert.Equal([(uint)1], api.Relinquished);
    }
}
