using System.ComponentModel;
using System.Runtime.CompilerServices;
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
    private int _failed;
    private bool _disposed;

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
                SafeFileHandle handle = CreateFile(path, GenericRead, ShareRead | ShareWrite,
                    nint.Zero, OpenExisting, FileFlagOverlapped, nint.Zero);
                if (handle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    handle.Dispose();
                    throw new Win32Exception(error,
                        "Could not open an allowlisted physical controller input endpoint.");
                }
                var stream = new FileStream(handle, FileAccess.Read, 64, isAsync: true);
                _streams.Add(stream);
                _readers.Add(ReadLoopAsync(stream, InputReportLengthForPath(path), _stop.Token));
            }
        }
        catch
        {
            _stop.Cancel();
            foreach (FileStream stream in _streams) stream.Dispose();
            _stop.Dispose();
            throw;
        }
    }

    public async IAsyncEnumerable<RawControllerReport> ReadReportsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await foreach (RawControllerReport report in _reports.Reader
                           .ReadAllAsync(cancellationToken))
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
                _stop.Cancel();
            }
        }
    }

    internal static int InputReportLengthForPath(string path) =>
        path.Contains("&Col02", StringComparison.OrdinalIgnoreCase) ? 2 : 11;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        foreach (FileStream stream in _streams)
        {
            try { await stream.DisposeAsync(); }
            catch { }
        }
        try { await Task.WhenAll(_readers); }
        catch { }
        _reports.Writer.TryComplete();
        _stop.Dispose();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share,
        nint security, uint creation, uint flags, nint template);
}
