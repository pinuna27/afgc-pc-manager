using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        string output = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.Combine(Environment.CurrentDirectory,
                $"fire-controller-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        using var writer = new StreamWriter(output, false, new UTF8Encoding(false))
        { AutoFlush = true };
        writer.WriteLine("utc_time,elapsed_ms,kind,step,label,device,report_hex");

        using var window = new RawInputWindow(writer);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; Application.Exit(); };

        Task.Run(() =>
        {
            try { new GuidedCapture(window, output).Run(); }
            catch (Exception exception) { Console.Error.WriteLine(exception); }
            finally { Application.Exit(); }
        });
        Application.Run();
    }
}

internal sealed record CaptureStep(string Label, string Instruction, int TimeoutSeconds = 10);

internal sealed class GuidedCapture
{
    private static readonly CaptureStep[] Steps =
    {
        new("A", "Press A once, then release."),
        new("B", "Press B once, then release."),
        new("X", "Press X once, then release."),
        new("Y", "Press Y once, then release."),
        new("L1", "Press the left shoulder button (L1), then release."),
        new("R1", "Press the right shoulder button (R1), then release."),
        new("L2", "Slowly squeeze L2 to 100%, hold briefly, then slowly release.", 30),
        new("R2", "Slowly squeeze R2 to 100%, hold briefly, then slowly release.", 30),
        new("L2+R2", "Slowly squeeze both triggers fully together, hold, then release both.", 30),
        new("DPAD_UP", "Press D-pad Up, then release."),
        new("DPAD_RIGHT", "Press D-pad Right, then release."),
        new("DPAD_DOWN", "Press D-pad Down, then release."),
        new("DPAD_LEFT", "Press D-pad Left, then release."),
        new("DPAD_UP_RIGHT", "Press D-pad Up+Right together, then release."),
        new("DPAD_DOWN_RIGHT", "Press D-pad Down+Right together, then release."),
        new("DPAD_DOWN_LEFT", "Press D-pad Down+Left together, then release."),
        new("DPAD_UP_LEFT", "Press D-pad Up+Left together, then release."),
        new("LEFT_STICK_LEFT", "Move the left stick fully left, hold briefly, then let it center."),
        new("LEFT_STICK_RIGHT", "Move the left stick fully right, hold briefly, then let it center."),
        new("LEFT_STICK_UP", "Move the left stick fully up, hold briefly, then let it center."),
        new("LEFT_STICK_DOWN", "Move the left stick fully down, hold briefly, then let it center."),
        new("LEFT_STICK_CIRCLE", "From center, move the left stick straight to the top. Starting at the top, make exactly two full clockwise circles along the outer edge, finish at the top, then release directly to center.", 30),
        new("L3", "Click the left stick without deliberately tilting it, then release."),
        new("RIGHT_STICK_LEFT", "Move the right stick fully left, hold briefly, then let it center."),
        new("RIGHT_STICK_RIGHT", "Move the right stick fully right, hold briefly, then let it center."),
        new("RIGHT_STICK_UP", "Move the right stick fully up, hold briefly, then let it center."),
        new("RIGHT_STICK_DOWN", "Move the right stick fully down, hold briefly, then let it center."),
        new("RIGHT_STICK_CIRCLE", "From center, move the right stick straight to the top. Starting at the top, make exactly two full clockwise circles along the outer edge, finish at the top, then release directly to center.", 30),
        new("R3", "Click the right stick without deliberately tilting it, then release."),
        new("BACK", "Press Back, then release."),
        new("MENU", "Press Menu, then release."),
        new("GAMECIRCLE", "Press the GameCircle button, then release."),
        new("REWIND", "Press the first-generation Rewind media button, then release."),
        new("PLAY_PAUSE", "Press the first-generation Play/Pause media button, then release."),
        new("FAST_FORWARD", "Press the first-generation Fast Forward media button, then release."),
        new("HOME", "Press Home once. Return here if Windows opens something.", 10)
    };

    private readonly RawInputWindow _input;
    private readonly string _output;
    private int _repeatRequested;

    public GuidedCapture(RawInputWindow input, string output) => (_input, _output) = (input, output);

    public void Run()
    {
        Console.WriteLine("Amazon Fire Game Controller guided raw-HID capture");
        Console.WriteLine($"Output: {_output}");
        Console.WriteLine("This records raw reports; websites and x360ce cannot rename the inputs.");
        Console.WriteLine("The steps advance automatically--no Enter key is needed.");
        Console.WriteLine("If you make a mistake, press R once to redo the most recent step.");
        Task.Run(WatchForRepeatKey);
        Console.WriteLine();
        Console.WriteLine("STARTUP");
        Console.WriteLine("1. Look at Windows Settings > Bluetooth & devices.");
        Console.WriteLine("2. If the controller says Connected, tap and release A once.");
        Console.WriteLine("3. If it says Paired but not Connected, press the controller's Home/Amazon");
        Console.WriteLine("   button once, wait for its lights, then tap and release A.");
        Console.WriteLine("Waiting for the first controller report (up to 60 seconds)...");
        if (!WaitForStartupReport(TimeSpan.FromSeconds(60)))
            throw new TimeoutException("No Fire controller reports arrived. Confirm it is connected and try again.");

        Console.WriteLine("Controller report received. Release A and leave all controls neutral.");
        Thread.Sleep(750);

        for (int index = 0; index < Steps.Length;)
        {
            if (ConsumeRepeat() && index > 0)
            {
                index--;
                _input.Mark("REVERT", index + 1, Steps[index].Label);
            }

            CaptureStep step = Steps[index];
            Console.WriteLine();
            Console.WriteLine($"STEP {index + 1}/{Steps.Length}: {step.Label}");
            Console.WriteLine(step.Instruction);

            byte[]? baseline = _input.LatestReport;
            _input.Mark("START", index + 1, step.Label);
            DetectionResult result = DetectAction(baseline, TimeSpan.FromSeconds(step.TimeoutSeconds));
            _input.Mark("END", index + 1, step.Label);

            Console.WriteLine(result.SawChange
                ? $"Captured ({result.ReportCount} reports)."
                : "No change detected; continuing.");
            Thread.Sleep(500);
            if (ConsumeRepeat())
            {
                _input.Mark("REPEAT", index + 1, step.Label);
                Console.WriteLine("Repeating that step.");
                continue;
            }

            _input.Mark("ACCEPT", index + 1, step.Label);
            index++;
        }

        _input.Mark("COMPLETE", 0, "guided_capture");
        Console.WriteLine();
        Console.WriteLine("Capture complete and saved. You can close this window.");
        Thread.Sleep(1500);
    }

    private DetectionResult DetectAction(byte[]? baseline, TimeSpan timeout)
    {
        long sequence = _input.Sequence;
        int reports = 0, peak = 0;
        bool changed = false;
        DateTime deadline = DateTime.UtcNow + timeout;
        DateTime? neutralSince = null;

        while (DateTime.UtcNow < deadline)
        {
            if (!_input.WaitForReportAfter(sequence, TimeSpan.FromMilliseconds(250), out RawReport sample))
            {
                if (changed && neutralSince is not null && DateTime.UtcNow - neutralSince >= TimeSpan.FromMilliseconds(700))
                    break;
                continue;
            }

            sequence = sample.Sequence;
            reports++;
            int difference = Difference(baseline, sample.Bytes);
            peak = Math.Max(peak, difference);
            if (difference > 0) changed = true;
            // A released analog stick may settle a few raw units away from its
            // starting value. Require a meaningful fall from the observed peak,
            // while exact digital releases still complete immediately.
            int neutralTolerance = Math.Max(2, peak / 20);
            bool returnedNearNeutral = difference == 0 ||
                (peak > 2 && difference < peak && difference <= neutralTolerance);
            neutralSince = changed && returnedNearNeutral ? DateTime.UtcNow : null;
        }
        return new DetectionResult(changed, reports, peak);
    }

    private bool WaitForStartupReport(TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        int secondsShown = -1;
        int lineWidth = 0;
        while (DateTime.UtcNow < deadline)
        {
            if (_input.WaitForAnyReport(TimeSpan.FromSeconds(1)))
            {
                ClearStatusLine(lineWidth);
                return true;
            }
            int elapsed = (int)(timeout - (deadline - DateTime.UtcNow)).TotalSeconds;
            if (elapsed != secondsShown)
            {
                secondsShown = elapsed;
                string status = $"Waiting for controller input... {elapsed,2}/{(int)timeout.TotalSeconds}s";
                lineWidth = Math.Max(lineWidth, status.Length);
                Console.Write('\r');
                Console.Write(status.PadRight(lineWidth));
            }
        }
        ClearStatusLine(lineWidth);
        return false;
    }

    private static void ClearStatusLine(int width)
    {
        if (width == 0) return;
        Console.Write('\r');
        Console.Write(new string(' ', width));
        Console.Write('\r');
    }

    private static int Difference(byte[]? left, byte[] right)
    {
        if (left is null || left.Length != right.Length) return right.Length * 255;
        int score = 0;
        for (int i = 0; i < left.Length; i++) score += Math.Abs(left[i] - right[i]);
        return score;
    }

    private readonly record struct DetectionResult(bool SawChange, int ReportCount, int PeakDifference);

    private void WatchForRepeatKey()
    {
        while (true)
        {
            if (Console.ReadKey(intercept: true).Key == ConsoleKey.R)
                Interlocked.Exchange(ref _repeatRequested, 1);
        }
    }

    private bool ConsumeRepeat() => Interlocked.Exchange(ref _repeatRequested, 0) == 1;
}

internal readonly record struct RawReport(long Sequence, byte[] Bytes);

internal sealed class RawInputWindow : NativeWindow, IDisposable
{
    private const int WmInput = 0x00FF;
    private const uint RidInput = 0x10000003, RidiDeviceName = 0x20000007, RidevInputSink = 0x100;
    private readonly StreamWriter _writer;
    private readonly object _gate = new();
    private readonly AutoResetEvent _reportArrived = new(false);
    private readonly long _started = Environment.TickCount64;
    private RawReport _latest;

    public long Sequence { get { lock (_gate) return _latest.Sequence; } }
    public byte[]? LatestReport { get { lock (_gate) return _latest.Bytes?.ToArray(); } }

    public RawInputWindow(StreamWriter writer)
    {
        _writer = writer;
        CreateHandle(new CreateParams { Caption = "Fire Controller Capture" });
        var devices = new[]
        {
            new RawInputDevice { UsagePage = 1, Usage = 5, Flags = RidevInputSink, Target = Handle },
            new RawInputDevice { UsagePage = 1, Usage = 4, Flags = RidevInputSink, Target = Handle },
            // The first-generation controller exposes some navigation/media
            // controls outside its gamepad top-level collection.
            new RawInputDevice { UsagePage = 0x0C, Usage = 0x01, Flags = RidevInputSink, Target = Handle },
            new RawInputDevice { UsagePage = 1, Usage = 0x06, Flags = RidevInputSink, Target = Handle },
            new RawInputDevice { UsagePage = 1, Usage = 0x80, Flags = RidevInputSink, Target = Handle }
        };
        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>()))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not register for raw game-controller input.");
    }

    public bool WaitForAnyReport(TimeSpan timeout) => _reportArrived.WaitOne(timeout);

    public bool WaitForReportAfter(long sequence, TimeSpan timeout, out RawReport report)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            lock (_gate) if (_latest.Sequence > sequence) { report = _latest; return true; }
            _reportArrived.WaitOne(TimeSpan.FromMilliseconds(50));
        }
        report = default;
        return false;
    }

    public void Mark(string kind, int step, string label)
    {
        lock (_gate) WriteRow(kind, step, label, "", "");
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmInput) Capture(message.LParam);
        base.WndProc(ref message);
    }

    private unsafe void Capture(nint handle)
    {
        uint size = 0, headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        if (GetRawInputData(handle, RidInput, null, ref size, headerSize) != 0 || size == 0) return;
        byte[] buffer = new byte[size];
        fixed (byte* pointer = buffer)
        {
            if (GetRawInputData(handle, RidInput, pointer, ref size, headerSize) != size) return;
            var header = Marshal.PtrToStructure<RawInputHeader>((nint)pointer);
            string device = GetDeviceName(header.Device);
            if (!IsFireController(device)) return;
            int offset = Marshal.SizeOf<RawInputHeader>();
            uint reportSize = *(uint*)(pointer + offset), reportCount = *(uint*)(pointer + offset + 4);
            byte* reports = pointer + offset + 8;
            for (uint i = 0; i < reportCount; i++)
            {
                byte[] bytes = new ReadOnlySpan<byte>(reports + i * reportSize, checked((int)reportSize)).ToArray();
                lock (_gate)
                {
                    _latest = new RawReport(_latest.Sequence + 1, bytes);
                    WriteRow("REPORT", 0, "", device, Convert.ToHexString(bytes));
                }
                _reportArrived.Set();
            }
        }
    }

    private void WriteRow(string kind, int step, string label, string device, string hex) =>
        _writer.WriteLine($"{DateTimeOffset.UtcNow:O},{Environment.TickCount64 - _started},{kind},{step},\"{Esc(label)}\",\"{Esc(device)}\",{hex}");
    private static string Esc(string value) => value.Replace("\"", "\"\"");
    private static bool IsFireController(string name) =>
        name.Contains("VID&00021949_PID&0402", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("VID_1949&PID_0402", StringComparison.OrdinalIgnoreCase);
    private static string GetDeviceName(nint device)
    {
        uint length = 0; GetRawInputDeviceInfo(device, RidiDeviceName, null, ref length);
        var text = new StringBuilder(checked((int)length));
        return length > 0 && GetRawInputDeviceInfo(device, RidiDeviceName, text, ref length) != uint.MaxValue ? text.ToString() : "unknown";
    }
    public void Dispose() { DestroyHandle(); _reportArrived.Dispose(); }

    [StructLayout(LayoutKind.Sequential)] private struct RawInputDevice { public ushort UsagePage, Usage; public uint Flags; public nint Target; }
    [StructLayout(LayoutKind.Sequential)] private struct RawInputHeader { public uint Type, Size; public nint Device, WParam; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)] private static extern unsafe uint GetRawInputData(nint input, uint command, void* data, ref uint size, uint headerSize);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetRawInputDeviceInfo(nint device, uint command, StringBuilder? data, ref uint size);
}
