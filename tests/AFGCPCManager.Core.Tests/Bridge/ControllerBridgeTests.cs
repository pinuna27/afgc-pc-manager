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
        await bridge.RunAsync();
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
        await bridge.RunAsync();
        Assert.Equal(3, output.States.Count); // initial neutral, changed state, final neutral
    }

    [Fact]
    public async Task EmitsMediaActionOnlyOnPressEdge()
    {
        var input = new FakeInput([new byte[] { 2, 0x08 }, new byte[] { 2, 0x08 }, new byte[] { 2, 0 }, new byte[] { 2, 0x08 }]);
        var consumer = new FakeConsumer();
        await using var bridge = new ControllerBridge(input, new FakeOutput(), consumer, new ControllerMappingProfile());
        await bridge.RunAsync();
        Assert.Equal([ConsumerAction.PlayPause, ConsumerAction.PlayPause], consumer.Actions);
    }

    private sealed class FakeInput(IEnumerable<byte[]> reports) : IRawControllerInput
    {
        public async IAsyncEnumerable<RawControllerReport> ReadReportsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        { foreach (byte[] report in reports) { cancellationToken.ThrowIfCancellationRequested(); yield return new(report); await Task.Yield(); } }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakeOutput : IGamepadOutputSession
    {
        public uint DeviceId => 1; public List<VirtualGamepadState> States { get; } = [];
        public ValueTask WriteAsync(VirtualGamepadState state, CancellationToken cancellationToken = default) { States.Add(state); return ValueTask.CompletedTask; }
        public void Dispose() { }
    }
    private sealed class FakeConsumer : IConsumerActionEmitter
    {
        public List<ConsumerAction> Actions { get; } = [];
        public ValueTask EmitAsync(ConsumerAction action, CancellationToken cancellationToken = default) { Actions.Add(action); return ValueTask.CompletedTask; }
    }
}
