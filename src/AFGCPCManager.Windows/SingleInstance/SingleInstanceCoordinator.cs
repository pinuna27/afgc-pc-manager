using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace AFGCPCManager.Windows.SingleInstance;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex _mutex; private readonly string _pipeName; private readonly CancellationTokenSource _stop = new(); private Task? _server;
    public bool IsPrimaryInstance { get; }
    public SingleInstanceCoordinator(string applicationId)
    {
        string user = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        string suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(applicationId + "\0" + user)))[..24];
        _pipeName = $"AFGCPCManager-{suffix}"; _mutex = new Mutex(true, $"Local\\AFGCPCManager-{suffix}", out bool created); IsPrimaryInstance = created;
    }
    public void StartServer(Action<InstanceCommand> commandReceived)
    {
        if (!IsPrimaryInstance || _server is not null) throw new InvalidOperationException("Only the primary instance can start the command server.");
        _server = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(_stop.Token); int value = pipe.ReadByte(); if (Enum.IsDefined(typeof(InstanceCommand), (byte)value)) commandReceived((InstanceCommand)value);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            }
        });
    }
    public async Task<bool> SendAsync(InstanceCommand command, CancellationToken cancellationToken = default)
    {
        try { await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification); await pipe.ConnectAsync(2000, cancellationToken); pipe.WriteByte((byte)command); await pipe.FlushAsync(cancellationToken); return true; }
        catch (TimeoutException) { return false; }
    }
    public void Dispose() { _stop.Cancel(); if (IsPrimaryInstance) try { _mutex.ReleaseMutex(); } catch (ApplicationException) { } _mutex.Dispose(); _stop.Dispose(); }
}
