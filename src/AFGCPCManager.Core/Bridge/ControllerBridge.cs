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
    private readonly object _lifetimeGate = new();
    private readonly CancellationTokenSource _stop = new();
    private ControllerMappingProfile _profile = profile ?? throw new ArgumentNullException(nameof(profile));
    private Task? _runTask;
    private Task? _disposeTask;
    private int _batteryPercentage = -1;

    public byte? BatteryPercentage
    {
        get
        {
            int percentage = Volatile.Read(ref _batteryPercentage);
            return percentage < 0 ? null : (byte)percentage;
        }
    }

    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            if (_runTask is not null)
                throw new InvalidOperationException("A controller bridge can only be run once.");
            return _runTask = RunCoreAsync(cancellationToken);
        }
    }

    private async Task RunCoreAsync(CancellationToken cancellationToken)
    {
        using var runStop = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _stop.Token);
        CancellationToken runToken = runStop.Token;
        await output.WriteAsync(VirtualGamepadState.Neutral, runToken)
            .ConfigureAwait(false);
        Exception? runFailure = null;
        try
        {
            await foreach (var raw in input.ReadReportsAsync(runToken)
                               .WithCancellation(runToken).ConfigureAwait(false))
            {
                bool changed = TryApply(raw.Bytes.Span);
                if (!changed) continue;
                MappingResult mapped = _mapper.Map(_accumulator.Current, _profile);
                await output.WriteAsync(mapped.Gamepad, runToken).ConfigureAwait(false);
                foreach (ConsumerAction action in mapped.ConsumerActions)
                    await consumerEmitter.EmitAsync(action, runToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) { runFailure = ex; }

        try
        {
            await output.WriteAsync(VirtualGamepadState.Neutral, CancellationToken.None)
                .ConfigureAwait(false);
        }
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
        ThrowIfDisposing();
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        MappingResult mapped = _mapper.Map(_accumulator.Current, _profile);
        await output.WriteAsync(mapped.Gamepad, cancellationToken).ConfigureAwait(false);
    }

    private bool TryApply(ReadOnlySpan<byte> report)
    {
        if (FireReportDecoder.TryDecodeGamepad(report, out var gamepad))
        {
            PublishBatteryPercentage(gamepad.BatteryPercentage);
            return _accumulator.Apply(gamepad);
        }
        if (FireReportDecoder.TryDecodeConsumer(report, out var consumer)) return _accumulator.Apply(consumer);
        return false;
    }

    private void PublishBatteryPercentage(byte percentage)
    {
        if (percentage > 100 || Volatile.Read(ref _batteryPercentage) == percentage)
            return;

        Volatile.Write(ref _batteryPercentage, percentage);
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifetimeGate)
            return new(_disposeTask ??= DisposeCoreAsync());
    }

    private void ThrowIfDisposing()
    {
        lock (_lifetimeGate)
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
    }

    private async Task DisposeCoreAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_runTask is not null)
        {
            try { await _runTask.ConfigureAwait(false); }
            catch { /* The RunAsync caller owns the run result; cleanup must continue. */ }
        }
        List<Exception> failures = [];
        try { await output.WriteAsync(VirtualGamepadState.Neutral).ConfigureAwait(false); }
        catch (Exception ex) { failures.Add(ex); }
        try { output.Dispose(); }
        catch (Exception ex) { failures.Add(ex); }
        try { await input.DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex) { failures.Add(ex); }
        _stop.Dispose();

        if (failures.Count == 0) return;
        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        throw new AggregateException("Controller bridge cleanup failed.", failures);
    }
}
