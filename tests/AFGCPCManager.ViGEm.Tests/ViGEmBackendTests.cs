using AFGCPCManager.Core.Output;

namespace AFGCPCManager.ViGEm.Tests;

public sealed class ViGEmBackendTests
{
    [Fact]
    public void ExposesExactlyFourXboxOutputs()
    {
        using var backend = new ViGEmBackend(new FakeViGEmClient());

        IReadOnlyList<OutputDeviceInfo> outputs = backend.EnumerateDevices();

        Assert.Equal(4, outputs.Count);
        Assert.All(outputs, output =>
        {
            Assert.Equal(OutputDeviceStatus.Free, output.Status);
            Assert.NotNull(output.Capabilities);
        });
    }

    [Fact]
    public void PreferredSlotIsStableAndSessionStartsNeutral()
    {
        var client = new FakeViGEmClient();
        using var backend = new ViGEmBackend(client);

        using IGamepadOutputSession session = Assert.IsAssignableFrom<IGamepadOutputSession>(
            backend.TryAcquire(3));

        Assert.Equal((uint)3, session.DeviceId);
        Assert.Equal(ViGEmStateConverter.Convert(VirtualGamepadState.Neutral),
            Assert.Single(client.Targets).Reports[0]);
        Assert.Equal(OutputDeviceStatus.Owned, backend.EnumerateDevices()[2].Status);
    }

    [Fact]
    public void FifthControllerIsRejected()
    {
        var client = new FakeViGEmClient();
        using var backend = new ViGEmBackend(client);
        var sessions = Enumerable.Range(0, 4)
            .Select(_ => backend.TryAcquire())
            .OfType<IGamepadOutputSession>()
            .ToArray();
        try { Assert.Null(backend.TryAcquire()); }
        finally { foreach (IGamepadOutputSession session in sessions) session.Dispose(); }
    }

    [Fact]
    public void BusWithoutFreeXInputSlotReturnsNoSession()
    {
        var client = new FakeViGEmClient { CanConnect = false };
        using var backend = new ViGEmBackend(client);

        Assert.Null(backend.TryAcquire());
        Assert.True(Assert.Single(client.Targets).Disposed);
    }

    [Fact]
    public async Task SessionWritesAndNeutralizesBeforeDisconnect()
    {
        var client = new FakeViGEmClient();
        using var backend = new ViGEmBackend(client);
        IGamepadOutputSession session = backend.TryAcquire()!;
        var state = VirtualGamepadState.Neutral with { Buttons = 1 };

        await session.WriteAsync(state, TestContext.Current.CancellationToken);
        session.Dispose();

        FakeXbox360Target target = Assert.Single(client.Targets);
        Assert.Equal(ViGEmStateConverter.Convert(state), target.Reports[1]);
        Assert.Equal(ViGEmStateConverter.Convert(VirtualGamepadState.Neutral), target.Reports[^1]);
        Assert.True(target.Disposed);
    }

    [Fact]
    public void BackendCannotInvalidateALiveNativeSession()
    {
        var client = new FakeViGEmClient();
        var backend = new ViGEmBackend(client);
        IGamepadOutputSession session = backend.TryAcquire()!;

        backend.Dispose();
        Assert.False(client.Disposed);

        session.Dispose();
        Assert.True(client.Disposed);
    }
}
