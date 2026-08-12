using AFGCPCManager.Core.Output;
using AFGCPCManager.Core.Settings;
using Xunit;

namespace AFGCPCManager.App.Tests;

public sealed class OutputBackendCacheTests
{
    [Fact]
    public void ReturningToModeReusesItsNativeBackend()
    {
        var created = new List<FakeBackend>();
        using var cache = new OutputBackendCache(_ =>
        {
            var backend = new FakeBackend();
            created.Add(backend);
            return backend;
        });

        IGamepadOutputBackend firstXInput = cache.GetOrCreate(GamepadOutputMode.XInput);
        IGamepadOutputBackend directInput = cache.GetOrCreate(GamepadOutputMode.DirectInput);
        IGamepadOutputBackend secondXInput = cache.GetOrCreate(GamepadOutputMode.XInput);

        Assert.Same(firstXInput, secondXInput);
        Assert.NotSame(firstXInput, directInput);
        Assert.Equal(2, created.Count);
        Assert.All(created, backend => Assert.False(backend.Disposed));
    }

    [Fact]
    public void DisposeReleasesEveryCachedBackendOnce()
    {
        var created = new List<FakeBackend>();
        var cache = new OutputBackendCache(_ =>
        {
            var backend = new FakeBackend();
            created.Add(backend);
            return backend;
        });
        cache.GetOrCreate(GamepadOutputMode.XInput);
        cache.GetOrCreate(GamepadOutputMode.DirectInput);

        cache.Dispose();
        cache.Dispose();

        Assert.All(created, backend => Assert.Equal(1, backend.DisposeCalls));
    }

    private sealed class FakeBackend : IGamepadOutputBackend
    {
        public int DisposeCalls { get; private set; }
        public bool Disposed => DisposeCalls > 0;
        public IReadOnlyList<OutputDeviceInfo> EnumerateDevices() => [];
        public IGamepadOutputSession? TryAcquire(uint? preferredDeviceId = null) => null;
        public void Dispose() => DisposeCalls++;
    }
}
