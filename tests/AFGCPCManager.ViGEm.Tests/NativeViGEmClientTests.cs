using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace AFGCPCManager.ViGEm.Tests;

public sealed class NativeViGEmClientTests
{
    [Fact]
    public void TargetAndClientCleanupAreIdempotentUnderConcurrency()
    {
        var api = new FakeNativeViGEmApi();
        var client = new NativeViGEmClient(api);
        IXbox360TargetApi target = client.CreateXbox360Target();
        Assert.True(target.TryConnect());
        var report = new ViGEmReport(0x1234, 7, 8, 9, 10, 11, 12);

        target.Submit(report);
        Parallel.For(0, 32, _ => target.Dispose());
        Parallel.For(0, 32, _ => client.Dispose());

        Assert.Equal(1, api.RemoveTargetCalls);
        Assert.Equal(1, api.FreeTargetCalls);
        Assert.Equal(1, api.DisconnectClientCalls);
        Assert.Equal(1, api.FreeClientCalls);
        Assert.Equal(NativeXbox360Report.From(report), Assert.Single(api.Reports));
        Assert.Equal(12, Marshal.SizeOf<NativeXbox360Report>());
    }

    [Fact]
    public void ClientCleanupContinuesAfterOneTargetRemovalFails()
    {
        var api = new FakeNativeViGEmApi();
        var client = new NativeViGEmClient(api);
        IXbox360TargetApi first = client.CreateXbox360Target();
        IXbox360TargetApi second = client.CreateXbox360Target();
        Assert.True(first.TryConnect());
        Assert.True(second.TryConnect());
        api.FailingRemovalTarget = 101;

        Assert.ThrowsAny<Exception>(() => client.Dispose());

        Assert.Equal(2, api.RemoveTargetCalls);
        Assert.Equal(2, api.FreeTargetCalls);
        Assert.Equal(1, api.DisconnectClientCalls);
        Assert.Equal(1, api.FreeClientCalls);
    }

    [Fact]
    public void FailedClientConnectionStillFreesAllocation()
    {
        var api = new FakeNativeViGEmApi
        {
            ConnectResult = NativeViGEmError.BusNotFound
        };

        Assert.Throws<ViGEmException>(() => new NativeViGEmClient(api));

        Assert.Equal(1, api.FreeClientCalls);
    }

    private sealed class FakeNativeViGEmApi : INativeViGEmApi
    {
        private int _nextTarget = 100;
        public NativeViGEmError ConnectResult { get; init; } = NativeViGEmError.None;
        public nint FailingRemovalTarget { get; set; }
        public int RemoveTargetCalls;
        public int FreeTargetCalls;
        public int DisconnectClientCalls;
        public int FreeClientCalls;
        public ConcurrentQueue<NativeXbox360Report> Reports { get; } = new();

        public nint AllocateClient() => 1;
        public void FreeClient(nint client) => Interlocked.Increment(ref FreeClientCalls);
        public NativeViGEmError Connect(nint client) => ConnectResult;
        public void Disconnect(nint client) => Interlocked.Increment(ref DisconnectClientCalls);
        public nint AllocateXbox360Target() => Interlocked.Increment(ref _nextTarget);
        public void FreeTarget(nint target) => Interlocked.Increment(ref FreeTargetCalls);
        public NativeViGEmError AddTarget(nint client, nint target) => NativeViGEmError.None;
        public NativeViGEmError RemoveTarget(nint client, nint target)
        {
            Interlocked.Increment(ref RemoveTargetCalls);
            return target == FailingRemovalTarget
                ? NativeViGEmError.RemovalFailed
                : NativeViGEmError.None;
        }
        public NativeViGEmError UpdateXbox360Target(
            nint client, nint target, NativeXbox360Report report)
        {
            Reports.Enqueue(report);
            return NativeViGEmError.None;
        }
    }
}
