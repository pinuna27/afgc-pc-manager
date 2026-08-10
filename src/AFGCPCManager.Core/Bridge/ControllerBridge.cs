using AFGCPCManager.Core.Input;
using AFGCPCManager.Core.Mapping;
using AFGCPCManager.Core.Output;
using System.Runtime.ExceptionServices;

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
        Exception? runFailure = null;
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
        catch (Exception ex) { runFailure = ex; }

        try { await output.WriteAsync(VirtualGamepadState.Neutral, CancellationToken.None); }
        catch (Exception neutralFailure)
        {
            if (runFailure is null)
                ExceptionDispatchInfo.Capture(neutralFailure).Throw();
            throw new AggregateException(
                "Controller input stopped and virtual-output neutralization also failed.",
                runFailure, neutralFailure);
        }

        if (runFailure is not null)
            ExceptionDispatchInfo.Capture(runFailure).Throw();
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
        List<Exception>? failures = null;
        try { await output.WriteAsync(VirtualGamepadState.Neutral); }
        catch (Exception ex) { (failures ??= []).Add(ex); }
        try { output.Dispose(); }
        catch (Exception ex) { (failures ??= []).Add(ex); }
        try { await input.DisposeAsync(); }
        catch (Exception ex) { (failures ??= []).Add(ex); }

        if (failures is null) return;
        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        throw new AggregateException("Controller bridge cleanup failed.", failures);
    }
}
