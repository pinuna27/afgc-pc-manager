using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using AFGCPCManager.Core.Input;
using Microsoft.Win32.SafeHandles;

namespace AFGCPCManager.Windows.RawInput;

public sealed class DirectHidControllerInput : IRawControllerInput
{
    private const uint GenericRead = 0x80000000;
    private const uint ShareRead = 1, ShareWrite = 2, OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<RawControllerReport> _reports = Channel.CreateUnbounded<RawControllerReport>(
        new() { SingleReader = true, SingleWriter = false });
    private readonly List<FileStream> _streams = [];
    private readonly List<Task> _readers = [];
    private readonly object _lifetimeGate = new();
    private int _failed;
    private Task? _disposeTask;

    public DirectHidControllerInput(IEnumerable<string> devicePaths)
    {
        ArgumentNullException.ThrowIfNull(devicePaths);
        string[] paths = devicePaths.Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0)
            throw new ArgumentException("At least one device path is required.", nameof(devicePaths));

        try
        {
            foreach (string path in paths)
            {
                SafeFileHandle? handle = CreateFile(path, GenericRead, ShareRead | ShareWrite,
                    nint.Zero, OpenExisting, FileFlagOverlapped, nint.Zero);
                try
                {
                    if (handle.IsInvalid)
                    {
                        int error = Marshal.GetLastWin32Error();
                        throw new Win32Exception(error,
                            "Could not open an allowlisted physical controller input endpoint.");
                    }
                    var stream = new FileStream(handle, FileAccess.Read, 64, isAsync: true);
                    handle = null; // FileStream now owns the SafeFileHandle.
                    _streams.Add(stream);
                    _readers.Add(ReadLoopAsync(stream, InputReportLengthForPath(path), _stop.Token));
                }
                finally { handle?.Dispose(); }
            }
        }
        catch
        {
            _stop.Cancel();
            try { Task.WhenAll(_readers).GetAwaiter().GetResult(); }
            catch (Exception ex) when (ex is OperationCanceledException
                or ObjectDisposedException)
            { }
            foreach (FileStream stream in _streams) stream.Dispose();
            _stop.Dispose();
            throw;
        }
    }

    public async IAsyncEnumerable<RawControllerReport> ReadReportsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        lock (_lifetimeGate)
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
        await foreach (RawControllerReport report in _reports.Reader
                           .ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return report;
    }

    private async Task ReadLoopAsync(
        FileStream stream, int reportLength, CancellationToken cancellationToken)
    {
        var buffer = new byte[reportLength];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int read = 0;
                while (read < buffer.Length)
                {
                    int count = await stream.ReadAsync(
                        buffer.AsMemory(read, buffer.Length - read), cancellationToken);
                    if (count == 0)
                        throw new EndOfStreamException(
                            "The physical controller input endpoint disconnected.");
                    read += count;
                }
                _reports.Writer.TryWrite(new(buffer.ToArray()));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref _failed, 1) == 0)
            {
                _reports.Writer.TryComplete(ex);
                await _stop.CancelAsync().ConfigureAwait(false);
            }
        }
    }

    internal static int InputReportLengthForPath(string path) =>
        path.Contains("&Col02", StringComparison.OrdinalIgnoreCase) ? 2 : 11;

    public ValueTask DisposeAsync()
    {
        lock (_lifetimeGate)
            return new(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        List<Exception> failures = [];
        try { await Task.WhenAll(_readers).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { failures.Add(ex); }
        foreach (FileStream stream in _streams)
        {
            try { await stream.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { failures.Add(ex); }
        }
        _reports.Writer.TryComplete();
        _stop.Dispose();
        if (failures.Count == 0) return;
        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        throw new AggregateException("Controller input cleanup failed.", failures);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share,
        nint security, uint creation, uint flags, nint template);
}
