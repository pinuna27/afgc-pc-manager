using AFGCPCManager.Core.Output;
using AFGCPCManager.Windows.Consumer;

namespace AFGCPCManager.Windows.Tests.Consumer;

public sealed class WindowsConsumerActionEmitterTests
{
    [Theory]
    [InlineData(ConsumerAction.Rewind, -10)]
    [InlineData(ConsumerAction.FastForward, 10)]
    public async Task TransportScanButtonsSeekTheActiveMediaSession(
        ConsumerAction action,
        int expectedSeconds)
    {
        var virtualKeys = new List<ushort>();
        var seeks = new List<TimeSpan>();
        var emitter = new WindowsConsumerActionEmitter(
            key => { virtualKeys.Add(key); return true; },
            (offset, _) => { seeks.Add(offset); return ValueTask.FromResult(true); });

        await emitter.EmitAsync(action, TestContext.Current.CancellationToken);

        Assert.Empty(virtualKeys);
        Assert.Equal([TimeSpan.FromSeconds(expectedSeconds)], seeks);
    }

    [Theory]
    [InlineData(ConsumerAction.PlayPause, 0xB3)]
    [InlineData(ConsumerAction.BrowserHome, 0xAC)]
    public async Task AuthenticVirtualKeysRemainVirtualKeys(
        ConsumerAction action,
        ushort expectedKey)
    {
        var virtualKeys = new List<ushort>();
        var seeks = new List<TimeSpan>();
        var emitter = new WindowsConsumerActionEmitter(
            key => { virtualKeys.Add(key); return true; },
            (offset, _) => { seeks.Add(offset); return ValueTask.FromResult(true); });

        await emitter.EmitAsync(action, TestContext.Current.CancellationToken);

        Assert.Equal([expectedKey], virtualKeys);
        Assert.Empty(seeks);
    }
}
