using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Nefarius.ViGEm.Client;

namespace AFGCPCManager.ViGEm;

internal interface IViGEmClientApi : IDisposable
{
    IXbox360TargetApi CreateXbox360Target();
}

internal interface IXbox360TargetApi : IDisposable
{
    bool TryConnect();
    void Submit(ViGEmReport report);
    void Disconnect();
}

internal interface INativeViGEmApi
{
    nint AllocateClient();
    void FreeClient(nint client);
    NativeViGEmError Connect(nint client);
    void Disconnect(nint client);
    nint AllocateXbox360Target();
    void FreeTarget(nint target);
    NativeViGEmError AddTarget(nint client, nint target);
    NativeViGEmError RemoveTarget(nint client, nint target);
    NativeViGEmError UpdateXbox360Target(
        nint client, nint target, NativeXbox360Report report);
}

internal enum NativeViGEmError : uint
{
    None = 0x20000000,
    BusNotFound = 0xE0000001,
    NoFreeSlot = 0xE0000002,
    InvalidTarget = 0xE0000003,
    RemovalFailed = 0xE0000004,
    AlreadyConnected = 0xE0000005,
    TargetUninitialized = 0xE0000006,
    TargetNotPluggedIn = 0xE0000007,
    BusVersionMismatch = 0xE0000008,
    BusAccessFailed = 0xE0000009,
    BusAlreadyConnected = 0xE0000012,
    BusInvalidHandle = 0xE0000013
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeXbox360Report(
    ushort Buttons,
    byte LeftTrigger,
    byte RightTrigger,
    short LeftThumbX,
    short LeftThumbY,
    short RightThumbX,
    short RightThumbY)
{
    public static NativeXbox360Report From(ViGEmReport report) => new(
        report.Buttons,
        report.LeftTrigger,
        report.RightTrigger,
        report.LeftThumbX,
        report.LeftThumbY,
        report.RightThumbX,
        report.RightThumbY);
}

internal sealed class NativeViGEmClient : IViGEmClientApi
{
    private readonly object _gate = new();
    private readonly INativeViGEmApi _api;
    private readonly HashSet<NativeXbox360Target> _targets = [];
    private nint _handle;
    private bool _disposed;

    public NativeViGEmClient() : this(CreateNativeApi()) { }

    internal NativeViGEmClient(INativeViGEmApi api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _handle = _api.AllocateClient();
        if (_handle == 0)
            throw new ViGEmException("ViGEmBus could not allocate a client.");

        try
        {
            NativeViGEmError error = _api.Connect(_handle);
            if (error is not (NativeViGEmError.None or
                NativeViGEmError.BusAlreadyConnected))
                throw ClientConnectionFailure(error);
        }
        catch
        {
            _api.FreeClient(_handle);
            _handle = 0;
            throw;
        }
    }

    public IXbox360TargetApi CreateXbox360Target()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            nint targetHandle = _api.AllocateXbox360Target();
            if (targetHandle == 0)
                throw new ViGEmException(
                    "ViGEmBus could not allocate an Xbox controller.");

            var target = new NativeXbox360Target(
                _api, _handle, targetHandle, _gate, OnTargetDisposed);
            _targets.Add(target);
            return target;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            List<Exception> failures = [];

            foreach (NativeXbox360Target target in _targets.ToArray())
            {
                try { target.Dispose(); }
                catch (Exception ex) { failures.Add(ex); }
            }

            if (_handle != 0)
            {
                try { _api.Disconnect(_handle); }
                catch (Exception ex) { failures.Add(ex); }
                finally
                {
                    try { _api.FreeClient(_handle); }
                    catch (Exception ex) { failures.Add(ex); }
                    _handle = 0;
                }
            }

            ThrowCleanupFailures(failures, "ViGEmBus client cleanup failed.");
        }
    }

    private void OnTargetDisposed(NativeXbox360Target target) =>
        _targets.Remove(target);

    private static NativeViGEmApi CreateNativeApi()
    {
        try { return NativeViGEmApi.Instance; }
        catch (Exception ex) when (ex is DllNotFoundException
            or BadImageFormatException or EntryPointNotFoundException
            or IOException or UnauthorizedAccessException)
        {
            throw new ViGEmException(
                "The ViGEm native client could not be loaded. Repair ViGEmBus or reinstall this app.",
                ex);
        }
    }

    internal static void ThrowCleanupFailures(
        List<Exception> failures, string message)
    {
        if (failures.Count == 0) return;
        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        throw new AggregateException(message, failures);
    }

    private static ViGEmException ClientConnectionFailure(NativeViGEmError error) =>
        error switch
        {
            NativeViGEmError.BusNotFound => new(
                "ViGEmBus is unavailable. Install or repair ViGEmBus, then try Xbox (XInput) again."),
            NativeViGEmError.BusAccessFailed => new(
                "ViGEmBus denied access to its client connection."),
            NativeViGEmError.BusVersionMismatch => new(
                "The installed ViGEmBus version is incompatible with this app."),
            _ => new($"ViGEmBus client connection failed with error 0x{(uint)error:X8}.")
        };
}

internal sealed class NativeXbox360Target : IXbox360TargetApi
{
    private readonly INativeViGEmApi _api;
    private readonly nint _client;
    private readonly object _gate;
    private readonly Action<NativeXbox360Target> _released;
    private nint _target;
    private bool _connected;
    private bool _disposed;

    public NativeXbox360Target(
        INativeViGEmApi api,
        nint client,
        nint target,
        object gate,
        Action<NativeXbox360Target> released)
    {
        _api = api;
        _client = client;
        _target = target;
        _gate = gate;
        _released = released;
    }

    public bool TryConnect()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_connected) return true;

            NativeViGEmError error = _api.AddTarget(_client, _target);
            if (error == NativeViGEmError.NoFreeSlot) return false;
            if (error != NativeViGEmError.None)
                throw new ViGEmException(
                    $"ViGEmBus could not create an Xbox controller (0x{(uint)error:X8}).");
            _connected = true;
            return true;
        }
    }

    public void Submit(ViGEmReport report)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_connected)
                throw new ViGEmException("The Xbox controller is not connected.");

            NativeViGEmError error = _api.UpdateXbox360Target(
                _client, _target, NativeXbox360Report.From(report));
            if (error != NativeViGEmError.None)
                throw new ViGEmException(
                    $"ViGEmBus rejected an Xbox controller update (0x{(uint)error:X8}).");
        }
    }

    public void Disconnect()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            DisconnectCore();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            List<Exception> failures = [];
            try { DisconnectCore(); }
            catch (Exception ex) { failures.Add(ex); }

            try
            {
                if (_target != 0)
                    _api.FreeTarget(_target);
            }
            catch (Exception ex) { failures.Add(ex); }
            finally
            {
                _target = 0;
                _released(this);
            }

            NativeViGEmClient.ThrowCleanupFailures(
                failures, "ViGEmBus could not remove an Xbox controller cleanly.");
        }
    }

    private void DisconnectCore()
    {
        if (!_connected) return;
        NativeViGEmError error = _api.RemoveTarget(_client, _target);
        _connected = false;
        if (error is not (NativeViGEmError.None or
            NativeViGEmError.TargetNotPluggedIn))
            throw new ViGEmException(
                $"ViGEmBus could not remove an Xbox controller (0x{(uint)error:X8}).");
    }
}

internal sealed class NativeViGEmApi : INativeViGEmApi
{
    private const string NativeResource32 = "costura32.vigemclient.dll";
    private const string NativeResource64 = "costura64.vigemclient.dll";
    private static readonly Lazy<NativeViGEmApi> Shared = new(() => new());

    private readonly AllocateDelegate _allocateClient;
    private readonly FreeDelegate _freeClient;
    private readonly ClientOperationDelegate _connect;
    private readonly FreeDelegate _disconnect;
    private readonly AllocateDelegate _allocateTarget;
    private readonly FreeDelegate _freeTarget;
    private readonly TargetOperationDelegate _addTarget;
    private readonly TargetOperationDelegate _removeTarget;
    private readonly UpdateDelegate _update;

    public static NativeViGEmApi Instance => Shared.Value;

    private NativeViGEmApi()
    {
        nint library = NativeViGEmLibrary.Load();
        _allocateClient = Load<AllocateDelegate>(library, "vigem_alloc");
        _freeClient = Load<FreeDelegate>(library, "vigem_free");
        _connect = Load<ClientOperationDelegate>(library, "vigem_connect");
        _disconnect = Load<FreeDelegate>(library, "vigem_disconnect");
        _allocateTarget = Load<AllocateDelegate>(library, "vigem_target_x360_alloc");
        _freeTarget = Load<FreeDelegate>(library, "vigem_target_free");
        _addTarget = Load<TargetOperationDelegate>(library, "vigem_target_add");
        _removeTarget = Load<TargetOperationDelegate>(library, "vigem_target_remove");
        _update = Load<UpdateDelegate>(library, "vigem_target_x360_update");
    }

    public nint AllocateClient() => _allocateClient();
    public void FreeClient(nint client) => _freeClient(client);
    public NativeViGEmError Connect(nint client) => _connect(client);
    public void Disconnect(nint client) => _disconnect(client);
    public nint AllocateXbox360Target() => _allocateTarget();
    public void FreeTarget(nint target) => _freeTarget(target);
    public NativeViGEmError AddTarget(nint client, nint target) =>
        _addTarget(client, target);
    public NativeViGEmError RemoveTarget(nint client, nint target) =>
        _removeTarget(client, target);
    public NativeViGEmError UpdateXbox360Target(
        nint client, nint target, NativeXbox360Report report) =>
        _update(client, target, report);

    private static T Load<T>(nint library, string export) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(
            NativeLibrary.GetExport(library, export));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint AllocateDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreeDelegate(nint handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeViGEmError ClientOperationDelegate(nint client);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeViGEmError TargetOperationDelegate(nint client, nint target);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeViGEmError UpdateDelegate(
        nint client, nint target, NativeXbox360Report report);

    private static class NativeViGEmLibrary
    {
        private static nint _handle;

        public static nint Load()
        {
            if (_handle != 0) return _handle;

            Assembly assembly = typeof(ViGEmClient).Assembly;
            string resourceName = Environment.Is64BitProcess
                ? NativeResource64
                : NativeResource32;
            using Stream resource = assembly.GetManifestResourceStream(resourceName)
                ?? throw new DllNotFoundException(
                    $"The embedded ViGEm native SDK resource '{resourceName}' is missing.");
            using var memory = new MemoryStream();
            resource.CopyTo(memory);
            byte[] image = memory.ToArray();
            string digest = Convert.ToHexString(SHA256.HashData(image))[..20];
            string directory = Path.Combine(
                Path.GetTempPath(), "AFGC PC Manager", "ViGEm", digest,
                Environment.Is64BitProcess ? "x64" : "x86");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "vigemclient.dll");
            WriteVerifiedImage(path, image);

            // Keep the module loaded for process lifetime. Every delegate below
            // points into it, so unloading it during shutdown would itself create
            // an invalid-indirect-call crash opportunity.
            _handle = NativeLibrary.Load(path);
            return _handle;
        }

        private static void WriteVerifiedImage(string path, byte[] expected)
        {
            if (File.Exists(path)
                && File.ReadAllBytes(path).AsSpan().SequenceEqual(expected))
                return;

            string temporary = path + $".{Environment.ProcessId}.tmp";
            File.WriteAllBytes(temporary, expected);
            try { File.Move(temporary, path, overwrite: true); }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
