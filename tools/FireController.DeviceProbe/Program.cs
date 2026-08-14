using System.ComponentModel;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        bool testLed = args.Any(a => a.Equals("--led", StringComparison.OrdinalIgnoreCase));
        string? outputArgument = args.FirstOrDefault(a => !a.StartsWith("--"));
        string output = Path.GetFullPath(outputArgument ??
            Path.Combine("captures", $"fire-controller-probe-{DateTime.Now:yyyyMMdd-HHmmss}.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        Console.WriteLine("Amazon Fire Game Controller device probe");
        Console.WriteLine("This phase is read-only: it will not send output reports.");
        Console.WriteLine("Tap and release A so Windows supplies the controller device path.");

        using var window = new PathCaptureWindow((path, inputReport) =>
        {
            try
            {
                ProbeResult result = HidProbe.Inspect(path);
                File.WriteAllText(output, JsonSerializer.Serialize(result,
                    new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine();
                Console.WriteLine($"Collection: {result.Collection}");
                Console.WriteLine($"Input report length:   {result.Capabilities.InputReportBytes} bytes");
                Console.WriteLine($"Output report length:  {result.Capabilities.OutputReportBytes} bytes");
                Console.WriteLine($"Feature report length: {result.Capabilities.FeatureReportBytes} bytes");
                Console.WriteLine($"Readable feature reports: {result.FeatureReports.Count}");
                SaveBatterySnapshot(inputReport);
                if (inputReport.Length > 10)
                    Console.WriteLine($"Battery level (input byte 10): {inputReport[10]}% (0x{inputReport[10]:X2})");
                Console.WriteLine(result.Capabilities.OutputReportBytes > 0
                    ? "The device advertises a two-byte LED output report; it does not advertise rumble."
                    : "This collection advertises no output report.");
                if (testLed)
                {
                    Console.WriteLine("Testing the four descriptor-confirmed LED bits...");
                    bool sent = HidProbe.TestLeds(path);
                    Console.WriteLine(sent ? "LED walking-bit test completed successfully." : "Windows rejected an LED output report.");
                }
                Console.WriteLine($"Saved: {output}");
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); }
            finally { Application.Exit(); }
        });
        Application.Run();
    }

    private static void SaveBatterySnapshot(byte[] report)
    {
        if (report.Length <= 10) return;
        string path = Path.GetFullPath(Path.Combine("captures", "battery-history.csv"));
        bool addHeader = !File.Exists(path);
        using var writer = new StreamWriter(path, append: true, new UTF8Encoding(false));
        if (addHeader) writer.WriteLine("utc_time,candidate_hex,candidate_decimal,raw_report");
        writer.WriteLine($"{DateTimeOffset.UtcNow:O},0x{report[10]:X2},{report[10]},{Convert.ToHexString(report)}");
    }
}

internal sealed class PathCaptureWindow : NativeWindow, IDisposable
{
    private const int WmInput = 0xFF;
    private const uint RidInput = 0x10000003, RidiDeviceName = 0x20000007, InputSink = 0x100;
    private readonly Action<string, byte[]> _found;
    private bool _done;

    public PathCaptureWindow(Action<string, byte[]> found)
    {
        _found = found;
        CreateHandle(new CreateParams { Caption = "Fire Controller Device Probe" });
        var device = new RawInputDevice { UsagePage = 1, Usage = 5, Flags = InputSink, Target = Handle };
        if (!RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RawInputDevice>()))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    protected override void WndProc(ref Message message)
    {
        if (!_done && message.Msg == WmInput)
        {
            uint size = 0, headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
            GetRawInputData(message.LParam, RidInput, nint.Zero, ref size, headerSize);
            nint memory = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(message.LParam, RidInput, memory, ref size, headerSize) == size)
                {
                    var header = Marshal.PtrToStructure<RawInputHeader>(memory);
                    string path = GetName(header.Device);
                    if (path.Contains("VID&00021949_PID&0402", StringComparison.OrdinalIgnoreCase))
                    {
                        int offset = Marshal.SizeOf<RawInputHeader>();
                        uint reportSize = (uint)Marshal.ReadInt32(memory, offset);
                        byte[] report = new byte[reportSize];
                        Marshal.Copy(memory + offset + 8, report, 0, report.Length);
                        _done = true;
                        Task.Run(() => _found(path, report));
                    }
                }
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        base.WndProc(ref message);
    }

    private static string GetName(nint device)
    {
        uint length = 0; GetRawInputDeviceInfo(device, RidiDeviceName, null, ref length);
        var text = new StringBuilder((int)length);
        GetRawInputDeviceInfo(device, RidiDeviceName, text, ref length);
        return text.ToString();
    }

    public void Dispose() => DestroyHandle();
    [StructLayout(LayoutKind.Sequential)] private struct RawInputDevice { public ushort UsagePage, Usage; public uint Flags; public nint Target; }
    [StructLayout(LayoutKind.Sequential)] private struct RawInputHeader { public uint Type, Size; public nint Device, WParam; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(nint input, uint command, nint data, ref uint size, uint headerSize);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint GetRawInputDeviceInfo(nint device, uint command, StringBuilder? data, ref uint size);
}

internal static class HidProbe
{
    private const uint GenericWrite = 0x40000000, ShareRead = 1, ShareWrite = 2, OpenExisting = 3;

    public static ProbeResult Inspect(string path)
    {
        using SafeFileHandle handle = CreateFile(path, 0, ShareRead | ShareWrite,
            nint.Zero, OpenExisting, 0, nint.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open HID collection.");
        if (!HidD_GetPreparsedData(handle, out nint preparsed))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read HID preparsed data.");
        try
        {
            int status = HidP_GetCaps(preparsed, out HidCaps caps);
            if (status < 0) throw new InvalidOperationException($"HidP_GetCaps failed: 0x{status:X8}");

            var features = new List<FeatureReport>();
            if (caps.FeatureReportByteLength > 0)
            {
                // GET_FEATURE is read-only. Unsupported report IDs simply fail.
                for (int id = 0; id <= byte.MaxValue; id++)
                {
                    byte[] buffer = new byte[caps.FeatureReportByteLength];
                    buffer[0] = (byte)id;
                    if (HidD_GetFeature(handle, buffer, buffer.Length))
                        features.Add(new FeatureReport(id, Convert.ToHexString(buffer)));
                }
            }

            var outputCaps = ReadOutputButtonCaps(preparsed, caps.NumberOutputButtonCaps);
            return new ProbeResult(path, Collection(path), ReadString(handle, HidD_GetManufacturerString),
                ReadString(handle, HidD_GetProductString), ReadString(handle, HidD_GetSerialNumberString),
                new Capabilities(caps.UsagePage, caps.Usage, caps.InputReportByteLength,
                    caps.OutputReportByteLength, caps.FeatureReportByteLength,
                    caps.NumberInputButtonCaps, caps.NumberInputValueCaps,
                    caps.NumberOutputButtonCaps, caps.NumberOutputValueCaps,
                    caps.NumberFeatureButtonCaps, caps.NumberFeatureValueCaps), outputCaps, features);
        }
        finally { HidD_FreePreparsedData(preparsed); }
    }

    public static bool TestLeds(string path)
    {
        using SafeFileHandle handle = CreateFile(path, GenericWrite, ShareRead | ShareWrite,
            nint.Zero, OpenExisting, 0, nint.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the LED output path.");
        byte[] off = { 0x01, 0x00 };
        bool success = true;
        try
        {
            for (byte mask = 0x00; mask <= 0x0F; mask++)
            {
                string binary = Convert.ToString(mask, 2).PadLeft(4, '0');
                Console.WriteLine($"  LED mask 0x{mask:X1} (0b{binary})");
                byte[] report = { 0x01, mask };
                success &= HidD_SetOutputReport(handle, report, report.Length);
                Thread.Sleep(500);
            }
        }
        finally { HidD_SetOutputReport(handle, off, off.Length); }
        return success;
    }

    private static string Collection(string path) =>
        System.Text.RegularExpressions.Regex.Match(path, "(?i)&Col[0-9A-F]{2}").Value;
    private static unsafe List<OutputButtonCapability> ReadOutputButtonCaps(nint preparsed, ushort requested)
    {
        var result = new List<OutputButtonCapability>();
        if (requested == 0) return result;
        const int capSize = 72;
        nint memory = Marshal.AllocHGlobal(capSize * requested);
        try
        {
            Span<byte> cleared = new((void*)memory, capSize * requested);
            cleared.Clear();
            ushort count = requested;
            int status = HidP_GetButtonCaps(1, memory, ref count, preparsed); // 1 = HidP_Output
            if (status < 0) return result;
            for (int index = 0; index < count; index++)
            {
                nint cap = memory + index * capSize;
                byte[] raw = new byte[capSize];
                Marshal.Copy(cap, raw, 0, raw.Length);
                result.Add(new OutputButtonCapability(
                    Marshal.ReadInt16(cap, 0) & 0xFFFF,
                    Marshal.ReadByte(cap, 2),
                    Marshal.ReadInt16(cap, 4) & 0xFFFF,
                    Marshal.ReadInt16(cap, 6) & 0xFFFF,
                    Marshal.ReadInt16(cap, 8) & 0xFFFF,
                    Marshal.ReadInt16(cap, 10) & 0xFFFF,
                    Convert.ToHexString(raw)));
            }
            return result;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    private delegate bool StringReader(SafeFileHandle handle, nint buffer, int length);
    private static string? ReadString(SafeFileHandle handle, StringReader read)
    {
        nint memory = Marshal.AllocHGlobal(512);
        try { return read(handle, memory, 512) ? Marshal.PtrToStringUni(memory) : null; }
        finally { Marshal.FreeHGlobal(memory); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct HidCaps
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        public fixed ushort Reserved[17];
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps,
            NumberInputDataIndices, NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
            NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out nint data);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(nint data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(nint data, out HidCaps capabilities);
    [DllImport("hid.dll")] private static extern int HidP_GetButtonCaps(int reportType, nint caps, ref ushort count, nint data);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetFeature(SafeFileHandle handle, [In, Out] byte[] report, int length);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_SetOutputReport(SafeFileHandle handle, byte[] report, int length);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] private static extern bool HidD_GetManufacturerString(SafeFileHandle h, nint b, int l);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] private static extern bool HidD_GetProductString(SafeFileHandle h, nint b, int l);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] private static extern bool HidD_GetSerialNumberString(SafeFileHandle h, nint b, int l);
}

internal sealed record ProbeResult(string DevicePath, string Collection, string? Manufacturer,
    string? Product, string? SerialNumber, Capabilities Capabilities,
    List<OutputButtonCapability> OutputButtonCapabilities, List<FeatureReport> FeatureReports);
internal sealed record Capabilities(ushort UsagePage, ushort Usage, ushort InputReportBytes,
    ushort OutputReportBytes, ushort FeatureReportBytes, ushort InputButtonCaps,
    ushort InputValueCaps, ushort OutputButtonCaps, ushort OutputValueCaps,
    ushort FeatureButtonCaps, ushort FeatureValueCaps);
internal sealed record FeatureReport(int ReportId, string Hex);
internal sealed record OutputButtonCapability(int UsagePage, int ReportId, int BitField,
    int LinkCollection, int LinkUsage, int LinkUsagePage, string RawHex);
