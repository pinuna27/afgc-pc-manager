using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace AFGCPCManager.Windows.SingleInstance;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private Task? _server;
    private int _disposed;

    public bool IsPrimaryInstance { get; }

    public SingleInstanceCoordinator(string applicationId)
    {
        string user = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        string suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(applicationId + "\0" + user)))[..24];
        _pipeName = $"AFGCPCManager-{suffix}";
        _mutex = new Mutex(true, $"Local\\AFGCPCManager-{suffix}", out bool created);
        IsPrimaryInstance = created;
    }

    public void StartServer(Action<InstanceCommand> commandReceived)
    {
        ArgumentNullException.ThrowIfNull(commandReceived);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (!IsPrimaryInstance || _server is not null)
                throw new InvalidOperationException(
                    "Only the primary instance can start the command server once.");
            _server = Task.Run(() => RunServerAsync(commandReceived));
        }
    }

    private async Task RunServerAsync(Action<InstanceCommand> commandReceived)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    _pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                int value = pipe.ReadByte();
                if (Enum.IsDefined(typeof(InstanceCommand), (byte)value))
                {
                    try { commandReceived((InstanceCommand)value); }
                    catch (Exception ex)
                    {
                        Trace.TraceError(
                            $"Single-instance command handling failed: {ex}");
                    }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (Exception ex) when (IsUnavailable(ex))
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), _stop.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            }
        }
    }

    public async Task<bool> SendAsync(InstanceCommand command, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous,
                TokenImpersonationLevel.Identification);
            await pipe.ConnectAsync(2000, cancellationToken).ConfigureAwait(false);
            pipe.WriteByte((byte)command);
            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (IsUnavailable(ex)) { return false; }
    }

    internal static bool IsUnavailable(Exception exception) =>
        exception is TimeoutException or IOException or UnauthorizedAccessException;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Task? server;
        lock (_gate)
        {
            _stop.Cancel();
            server = _server;
        }
        if (server is not null)
        {
            try { server.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Trace.TraceError($"Single-instance command server stopped unexpectedly: {ex}");
            }
        }
        if (IsPrimaryInstance)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { }
        }
        _mutex.Dispose();
        _stop.Dispose();
    }
}
