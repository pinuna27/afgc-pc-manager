using AFGCPCManager.Core.Input;
using AFGCPCManager.Core.Mapping;
using AFGCPCManager.Core.Output;

namespace AFGCPCManager.Core.Bridge;

public sealed class ControllerBridge(
    IRawControllerInput input,
    IGamepadOutputSession output,
    IConsumerActionEmitter consumerEmitter,
    ControllerMappingProfile profile) : IAsyncDisposable
{
    private readonly PhysicalStateAccumulator _accumulator = new();
    private readonly ControllerStateMapper _mapper = new();
    private ControllerMappingProfile _profile = profile ?? throw new ArgumentNullException(nameof(profile));
    private bool _disposed;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await output.WriteAsync(VirtualGamepadState.Neutral, cancellationToken);
        try
        {
            await foreach (var raw in input.ReadReportsAsync(cancellationToken).WithCancellation(cancellationToken))
            {
                bool changed = TryApply(raw.Bytes.Span);
                if (!changed) continue;
                MappingResult mapped = _mapper.Map(_accumulator.Current, _profile);
                await output.WriteAsync(mapped.Gamepad, cancellationToken);
                foreach (ConsumerAction action in mapped.ConsumerActions)
                    await consumerEmitter.EmitAsync(action, cancellationToken);
            }
        }
        finally
        {
            await output.WriteAsync(VirtualGamepadState.Neutral, CancellationToken.None);
        }
    }

    public async ValueTask ApplyProfileAsync(ControllerMappingProfile profile, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        MappingResult mapped = _mapper.Map(_accumulator.Current, _profile);
        await output.WriteAsync(mapped.Gamepad, cancellationToken);
    }

    private bool TryApply(ReadOnlySpan<byte> report)
    {
        if (FireReportDecoder.TryDecodeGamepad(report, out var gamepad)) return _accumulator.Apply(gamepad);
        if (FireReportDecoder.TryDecodeConsumer(report, out var consumer)) return _accumulator.Apply(consumer);
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try { await output.WriteAsync(VirtualGamepadState.Neutral); }
        finally { output.Dispose(); await input.DisposeAsync(); }
    }
}
