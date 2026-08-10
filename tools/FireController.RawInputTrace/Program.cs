using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;
using AFGCPCManager.Windows.RawInput;

internal static class Program
{
    [STAThread]
    private static async Task Main(string[] args)
    {
        int seconds = args.Length > 0 && int.TryParse(args[0], out int parsed)
            ? Math.Clamp(parsed, 1, 120) : 20;
        using var window = new RawInputTraceWindow();
        await using var directInput = new DirectHidControllerInput(
            window.ConfigurationManagerFirePaths);
        using var stop = new CancellationTokenSource();
        Task directReader = Task.Run(async () =>
        {
            try
            {
                await foreach (var report in directInput.ReadReportsAsync(stop.Token))
                {
                    Console.WriteLine($"DIRECT_REPORT data={Convert.ToHexString(report.Bytes.Span)}");
                    Console.Out.Flush();
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        });
        using var timer = new System.Windows.Forms.Timer { Interval = seconds * 1000 };
        timer.Tick += (_, _) => Application.Exit();
        timer.Start();
        Console.WriteLine($"TRACE_READY duration_seconds={seconds}");
        Application.Run();
        stop.Cancel();
        await directInput.DisposeAsync();
        try { await directReader; }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        Console.WriteLine($"TRACE_DONE fire_reports={window.FireReportCount}");
    }
}

internal sealed class RawInputTraceWindow : NativeWindow, IDisposable
{
    private const int WmInput = 0x00FF;
    private const uint RidInput = 0x10000003, RidiDeviceName = 0x20000007;
    private const uint InputSink = 0x00000100;
    public int FireReportCount { get; private set; }
    public IReadOnlyList<string> ConfigurationManagerFirePaths { get; }

    public RawInputTraceWindow()
    {
        CreateHandle(new CreateParams { Caption = "AFGC raw input trace" });
        RawInputDevice[] devices =
        [
            new() { UsagePage = 1, Usage = 5, Flags = InputSink, Target = Handle },
            new() { UsagePage = 12, Usage = 1, Flags = InputSink, Target = Handle }
        ];
        if (!RegisterRawInputDevices(devices, (uint)devices.Length,
                (uint)Marshal.SizeOf<RawInputDevice>()))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        EnumerateFireDevices();
        ConfigurationManagerFirePaths = EnumerateConfigurationManagerFireDevices();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmInput)
            Trace(message.LParam);
        base.WndProc(ref message);
    }

    private void Trace(nint rawInput)
    {
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        if (GetRawInputData(rawInput, RidInput, nint.Zero, ref size, headerSize) != 0
            || size < headerSize) return;
        nint memory = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            if (GetRawInputData(rawInput, RidInput, memory, ref size, headerSize) != size)
                return;
            RawInputHeader header = Marshal.PtrToStructure<RawInputHeader>(memory);
            string path = GetDeviceName(header.Device);
            if (!path.Contains("VID&00021949_PID&0402", StringComparison.OrdinalIgnoreCase)
                && !path.Contains("VID_1949&PID_0402", StringComparison.OrdinalIgnoreCase))
                return;
            int offset = Marshal.SizeOf<RawInputHeader>();
            if (size < offset + 8) return;
            int reportLength = Marshal.ReadInt32(memory, offset);
            int reportCount = Marshal.ReadInt32(memory, offset + 4);
            int bytes = checked(reportLength * reportCount);
            if (reportLength <= 0 || reportCount <= 0 || offset + 8 + bytes > size) return;
            byte[] report = new byte[bytes];
            Marshal.Copy(memory + offset + 8, report, 0, report.Length);
            FireReportCount++;
            Console.WriteLine($"FIRE_REPORT path={path} data={Convert.ToHexString(report)}");
            Console.Out.Flush();
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static string GetDeviceName(nint device)
    {
        uint length = 0;
        GetRawInputDeviceInfo(device, RidiDeviceName, null, ref length);
        if (length == 0) return string.Empty;
        var value = new StringBuilder(checked((int)length));
        return GetRawInputDeviceInfo(device, RidiDeviceName, value, ref length) == uint.MaxValue
            ? string.Empty : value.ToString();
    }

    private static void EnumerateFireDevices()
    {
        uint count = 0;
        uint size = (uint)Marshal.SizeOf<RawInputDeviceList>();
        if (GetRawInputDeviceList(null, ref count, size) != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var devices = new RawInputDeviceList[count];
        if (count > 0 && GetRawInputDeviceList(devices, ref count, size) == uint.MaxValue)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        int fireCount = 0;
        foreach (RawInputDeviceList device in devices)
        {
            if (device.Type != 2) continue;
            string path = GetDeviceName(device.Device);
            if (!path.Contains("VID&00021949_PID&0402", StringComparison.OrdinalIgnoreCase)
                && !path.Contains("VID_1949&PID_0402", StringComparison.OrdinalIgnoreCase))
                continue;
            fireCount++;
            using SafeFileHandle handle = CreateFile(path, 0, 1 | 2,
                nint.Zero, 3, 0, nint.Zero);
            int error = handle.IsInvalid ? Marshal.GetLastWin32Error() : 0;
            Console.WriteLine($"FIRE_DEVICE path={path} open={!handle.IsInvalid} error={error}");
        }
        Console.WriteLine($"FIRE_DEVICE_COUNT={fireCount}");
        Console.Out.Flush();
    }

    private static IReadOnlyList<string> EnumerateConfigurationManagerFireDevices()
    {
        Guid hidInterface = new("4D1E55B2-F16F-11CF-88CB-001111000030");
        if (CM_Get_Device_Interface_List_SizeW(out uint length, ref hidInterface,
                null, 0) != 0 || length <= 1)
        {
            Console.WriteLine("CM_FIRE_DEVICE_COUNT=0");
            return [];
        }
        var buffer = new char[length];
        if (CM_Get_Device_Interface_ListW(ref hidInterface, null, buffer, length, 0) != 0)
        {
            Console.WriteLine("CM_FIRE_DEVICE_COUNT=0");
            return [];
        }
        string[] paths = new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        int fireCount = 0;
        var firePaths = new List<string>();
        foreach (string path in paths)
        {
            if (!path.Contains("VID&00021949_PID&0402", StringComparison.OrdinalIgnoreCase)
                && !path.Contains("VID_1949&PID_0402", StringComparison.OrdinalIgnoreCase))
                continue;
            fireCount++;
            firePaths.Add(path);
            using SafeFileHandle handle = CreateFile(path, 0, 1 | 2,
                nint.Zero, 3, 0, nint.Zero);
            int error = handle.IsInvalid ? Marshal.GetLastWin32Error() : 0;
            Console.WriteLine($"CM_FIRE_DEVICE path={path} open={!handle.IsInvalid} error={error}");
        }
        Console.WriteLine($"CM_FIRE_DEVICE_COUNT={fireCount}");
        Console.Out.Flush();
        return firePaths;
    }

    public void Dispose() => DestroyHandle();

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public nint Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public nint Device;
        public nint WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDeviceList
    {
        public nint Device;
        public uint Type;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(
        [In] RawInputDevice[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        nint input, uint command, nint data, ref uint size, uint headerSize);
    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(
        nint device, uint command, StringBuilder? data, ref uint size);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList(
        [Out] RawInputDeviceList[]? devices, ref uint count, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share,
        nint security, uint creation, uint flags, nint template);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_Interface_List_SizeW(out uint length,
        ref Guid interfaceClassGuid, string? deviceId, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_Interface_ListW(ref Guid interfaceClassGuid,
        string? deviceId, [Out] char[] buffer, uint bufferLength, uint flags);
}
