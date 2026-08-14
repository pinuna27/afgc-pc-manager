using System.Runtime.CompilerServices;
using AFGCPCManager.Core.Bridge;
using AFGCPCManager.Core.Input;
using AFGCPCManager.Core.Mapping;
using AFGCPCManager.Core.Output;

namespace AFGCPCManager.Core.Tests.Bridge;

public sealed class ControllerBridgeTests
{
    [Fact]
    public async Task ForwardsGamepadAndConsumerReportsAsOneState()
    {
        var input = new FakeInput([
            new byte[] { 1, 128, 127, 128, 127, 255, 0, 1, 0, 0, 96 },
            new byte[] { 2, 0x10 }, new byte[] { 2, 0 }]);
        var output = new FakeOutput(); var consumer = new FakeConsumer();
        await using var bridge = new ControllerBridge(input, output, consumer, new ControllerMappingProfile());
        await bridge.RunAsync(TestContext.Current.CancellationToken);
        Assert.Contains(output.States, s => s.LeftTrigger == 255 && s.IsButtonPressed(1));
        Assert.Contains(output.States, s => s.IsButtonPressed(11));
        Assert.Equal(VirtualGamepadState.Neutral, output.States[^1]);
    }

    [Fact]
    public async Task IgnoresMalformedAndDuplicateReports()
    {
        byte[] valid = [1, 128, 127, 128, 127, 0, 0, 1, 0, 0, 96];
        var input = new FakeInput([new byte[] { 99 }, valid, valid]); var output = new FakeOutput();
        await using var bridge = new ControllerBridge(input, output, new FakeConsumer(), new ControllerMappingProfile());
        await bridge.RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, output.States.Count); // initial neutral, changed state, final neutral
    }

    [Fact]
    public async Task EmitsMediaActionOnlyOnPressEdge()
    {
        var input = new FakeInput([new byte[] { 2, 0x08 }, new byte[] { 2, 0x08 }, new byte[] { 2, 0 }, new byte[] { 2, 0x08 }]);
        var consumer = new FakeConsumer();
        await using var bridge = new ControllerBridge(input, new FakeOutput(), consumer, new ControllerMappingProfile());
        await bridge.RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal([ConsumerAction.PlayPause, ConsumerAction.PlayPause], consumer.Actions);
    }

    [Fact]
    public async Task InputFailureStillNeutralizesOutput()
    {
        var input = new ThrowingInput();
        var output = new FakeOutput();
        await using var bridge = new ControllerBridge(input, output,
            new FakeConsumer(), new ControllerMappingProfile());

        await Assert.ThrowsAsync<IOException>(() =>
            bridge.RunAsync(TestContext.Current.CancellationToken));

        Assert.Contains(output.States, state => state.IsButtonPressed(1));
        Assert.Equal(VirtualGamepadState.Neutral, output.States[^1]);
    }

    [Fact]
    public async Task InputAndEmergencyNeutralFailuresAreBothReported()
    {
        var input = new ThrowingInput();
        var output = new FakeOutput { ThrowOnWriteCall = 3 };
        await using var bridge = new ControllerBridge(input, output,
            new FakeConsumer(), new ControllerMappingProfile());

        AggregateException error = await Assert.ThrowsAsync<AggregateException>(
            () => bridge.RunAsync(TestContext.Current.CancellationToken));

        Assert.Contains(error.InnerExceptions,
            ex => ex is IOException { Message: "simulated disconnect failure" });
        Assert.Contains(error.InnerExceptions,
            ex => ex is IOException { Message: "simulated neutral write failure" });
    }

    [Fact]
    public async Task OutputCleanupFailureStillDisposesInputSubscription()
    {
        var input = new FakeInput([]);
        var output = new FakeOutput { ThrowOnDispose = true };
        var bridge = new ControllerBridge(input, output,
            new FakeConsumer(), new ControllerMappingProfile());

        IOException error = await Assert.ThrowsAsync<IOException>(async () =>
            await bridge.DisposeAsync());

        Assert.Equal("simulated output cleanup failure", error.Message);
        Assert.True(input.Disposed);
    }

    [Fact]
    public async Task AllCleanupStepsRunAndMultipleFailuresAreReported()
    {
        var input = new FakeInput([]) { ThrowOnDispose = true };
        var output = new FakeOutput
        {
            ThrowOnWrite = true,
            ThrowOnDispose = true
        };
        var bridge = new ControllerBridge(input, output,
            new FakeConsumer(), new ControllerMappingProfile());

        AggregateException error = await Assert.ThrowsAsync<AggregateException>(async () =>
            await bridge.DisposeAsync());

        Assert.Equal(3, error.InnerExceptions.Count);
        Assert.True(output.DisposeAttempted);
        Assert.True(input.Disposed);
    }

    [Fact]
    public async Task ConcurrentCleanupRunsEachResourceTeardownOnlyOnce()
    {
        var input = new FakeInput([]);
        var output = new FakeOutput();
        var bridge = new ControllerBridge(input, output,
            new FakeConsumer(), new ControllerMappingProfile());

        await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(_ => bridge.DisposeAsync().AsTask()));

        Assert.Equal(1, input.DisposeCalls);
        Assert.Equal(1, output.DisposeCalls);
    }

    [Fact]
    public async Task CleanupCancelsAndJoinsAnActiveRunBeforeDisposingOutput()
    {
        var input = new WaitingInput();
        var output = new FakeOutput();
        var bridge = new ControllerBridge(input, output,
            new FakeConsumer(), new ControllerMappingProfile());
        Task run = bridge.RunAsync(TestContext.Current.CancellationToken);
        await input.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        await bridge.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(1, output.DisposeCalls);
        Assert.True(input.Disposed);
    }

    private sealed class FakeInput(IEnumerable<byte[]> reports) : IRawControllerInput
    {
        public bool Disposed { get; private set; }
        public int DisposeCalls { get; private set; }
        public bool ThrowOnDispose { get; init; }
        public async IAsyncEnumerable<RawControllerReport> ReadReportsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        { foreach (byte[] report in reports) { cancellationToken.ThrowIfCancellationRequested(); yield return new(report); await Task.Yield(); } }
        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            Disposed = true;
            return ThrowOnDispose
                ? ValueTask.FromException(new IOException("simulated input cleanup failure"))
                : ValueTask.CompletedTask;
        }
    }
    private sealed class ThrowingInput : IRawControllerInput
    {
        public async IAsyncEnumerable<RawControllerReport> ReadReportsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new(new byte[] { 1, 128, 127, 128, 127, 0, 0, 1, 0, 0, 92 });
            await Task.Yield();
            throw new IOException("simulated disconnect failure");
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class WaitingInput : IRawControllerInput
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public async IAsyncEnumerable<RawControllerReport> ReadReportsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
    private sealed class FakeOutput : IGamepadOutputSession
    {
        public uint DeviceId => 1; public List<VirtualGamepadState> States { get; } = [];
        public bool ThrowOnWrite { get; init; }
        public int? ThrowOnWriteCall { get; init; }
        public bool ThrowOnDispose { get; init; }
        public bool DisposeAttempted { get; private set; }
        public int DisposeCalls { get; private set; }
        public ValueTask WriteAsync(VirtualGamepadState state, CancellationToken cancellationToken = default)
        {
            States.Add(state);
            return ThrowOnWrite || States.Count == ThrowOnWriteCall
                ? ValueTask.FromException(new IOException("simulated neutral write failure"))
                : ValueTask.CompletedTask;
        }
        public void Dispose()
        {
            DisposeCalls++;
            DisposeAttempted = true;
            if (ThrowOnDispose) throw new IOException("simulated output cleanup failure");
        }
    }
    private sealed class FakeConsumer : IConsumerActionEmitter
    {
        public List<ConsumerAction> Actions { get; } = [];
        public ValueTask EmitAsync(ConsumerAction action, CancellationToken cancellationToken = default) { Actions.Add(action); return ValueTask.CompletedTask; }
    }
}
