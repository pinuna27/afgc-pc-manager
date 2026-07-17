using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using System.Windows.Forms;
using AFGCPCManager.Core.Input;

namespace AFGCPCManager.Windows.RawInput;

public sealed class RawInputControllerInput : IRawControllerInput
{
    private static readonly Lazy<RawInputHost> SharedHost = new(() => new());
    private readonly Channel<RawControllerReport> _reports = Channel.CreateUnbounded<RawControllerReport>(new() { SingleReader = true, SingleWriter = true });
    private readonly Guid _subscription;
    private bool _disposed;

    public RawInputControllerInput(IEnumerable<string>? devicePaths = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Raw Input is available only on Windows.");
        HashSet<string>? paths = devicePaths?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (paths is { Count: 0 }) throw new ArgumentException("At least one device path is required.", nameof(devicePaths));
        _subscription = SharedHost.Value.Subscribe(paths, report => _reports.Writer.TryWrite(new(report)));
    }

    public async IAsyncEnumerable<RawControllerReport> ReadReportsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await foreach (var report in _reports.Reader.ReadAllAsync(cancellationToken)) yield return report;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true; SharedHost.Value.Unsubscribe(_subscription); _reports.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private sealed class RawInputHost
    {
        private readonly object _gate = new();
        private readonly Dictionary<Guid, Subscription> _subscriptions = [];
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RawInputHost()
        {
            var thread = new Thread(MessageThread) { IsBackground = true, Name = "AFGC Raw Input Router" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); _ready.Task.GetAwaiter().GetResult();
        }
        public Guid Subscribe(HashSet<string>? paths, Action<byte[]> publish)
        {
            Guid id = Guid.NewGuid(); lock (_gate) _subscriptions.Add(id, new(paths, publish)); return id;
        }
        public void Unsubscribe(Guid id) { lock (_gate) _subscriptions.Remove(id); }
        private void MessageThread()
        {
            try { using var window = new RawInputWindow(Route); _ready.TrySetResult(); Application.Run(); }
            catch (Exception ex) { _ready.TrySetException(ex); }
        }
        private void Route(string path, byte[] report)
        {
            Subscription[] targets; lock (_gate) targets = _subscriptions.Values.ToArray();
            foreach (var target in targets) if (target.Paths is null || target.Paths.Contains(path)) target.Publish(report);
        }
        private sealed record Subscription(HashSet<string>? Paths, Action<byte[]> Publish);
    }

    private sealed class RawInputWindow : NativeWindow, IDisposable
    {
        private const int WmInput = 0x00ff;
        private const uint RidInput = 0x10000003, RidiDeviceName = 0x20000007, RidevInputSink = 0x100;
        private readonly Action<string, byte[]> _publish;
        public RawInputWindow(Action<string, byte[]> publish)
        {
            _publish = publish; CreateHandle(new CreateParams { Caption = "AFGC PC Manager Raw Input Router" });
            RawInputDevice[] devices = [new(1, 5, RidevInputSink, Handle), new(1, 4, RidevInputSink, Handle), new(0x0c, 1, RidevInputSink, Handle), new(1, 6, RidevInputSink, Handle), new(1, 0x80, RidevInputSink, Handle)];
            if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>())) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not register for raw controller input.");
        }
        protected override void WndProc(ref Message message) { if (message.Msg == WmInput) Capture(message.LParam); base.WndProc(ref message); }
        private unsafe void Capture(nint inputHandle)
        {
            uint size = 0, headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
            if (GetRawInputData(inputHandle, RidInput, null, ref size, headerSize) != 0 || size < headerSize + 8) return;
            byte[] buffer = new byte[size]; fixed (byte* pointer = buffer)
            {
                if (GetRawInputData(inputHandle, RidInput, pointer, ref size, headerSize) != size) return;
                RawInputHeader header = Marshal.PtrToStructure<RawInputHeader>((nint)pointer); string path = GetDeviceName(header.Device);
                if (!FireDevicePathMatcher.IsMatch(path)) return;
                int offset = checked((int)headerSize); uint reportSize = *(uint*)(pointer + offset), reportCount = *(uint*)(pointer + offset + 4);
                foreach (byte[] report in RawHidReportSplitter.Split(buffer.AsSpan(offset + 8), reportSize, reportCount)) _publish(path, report);
            }
        }
        private static string GetDeviceName(nint device)
        {
            uint length = 0; GetRawInputDeviceInfo(device, RidiDeviceName, null, ref length); if (length == 0) return string.Empty;
            var value = new StringBuilder((int)length); return GetRawInputDeviceInfo(device, RidiDeviceName, value, ref length) == uint.MaxValue ? string.Empty : value.ToString();
        }
        public void Dispose() { if (Handle != 0) DestroyHandle(); }
        [StructLayout(LayoutKind.Sequential)] private readonly record struct RawInputDevice(ushort UsagePage, ushort Usage, uint Flags, nint Target);
        [StructLayout(LayoutKind.Sequential)] private struct RawInputHeader { public uint Type, Size; public nint Device, WParam; }
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint count, uint size);
        [DllImport("user32.dll", SetLastError = true)] private static extern unsafe uint GetRawInputData(nint input, uint command, void* data, ref uint size, uint headerSize);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetRawInputDeviceInfo(nint device, uint command, StringBuilder? data, ref uint size);
    }
}
