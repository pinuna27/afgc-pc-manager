using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using AFGCPCManager.Core.Devices;

return ProbeProgram.Run(args);

internal static class ProbeProgram
{
    private const int VisibleExitCode = 10;
    private const int IndeterminateExitCode = 20;

    public static int Run(string[] args)
    {
        try
        {
            string? expected = ValueAfter(args, "--verify-hidden");
            bool assertNoneVisible = args.Contains("--assert-none-visible",
                StringComparer.OrdinalIgnoreCase);
            bool snapshot = args.Contains("--snapshot", StringComparer.OrdinalIgnoreCase);
            if ((expected is not null ? 1 : 0) + (assertNoneVisible ? 1 : 0)
                + (snapshot ? 1 : 0) != 1)
                throw new ArgumentException(
                    "Specify exactly one of --snapshot, --assert-none-visible, or --verify-hidden <stable-id>.");
            if (expected is not null && !IsStableId(expected))
                throw new ArgumentException("The expected controller identity is invalid.");

            RawVisibilitySnapshot snapshotResult = RawInputVisibility.Snapshot();
            IReadOnlyList<VisibleController> visible = snapshotResult.VisibleControllers;
            ProbeDecisionResult decision = assertNoneVisible
                ? ProbeDecision.EvaluateNoneVisible(visible)
                : ProbeDecision.Evaluate(expected, visible);
            var result = new ProbeResult(
                snapshot ? "snapshot" : decision.Status,
                visible.Count,
                visible.Count(controller => !controller.HasPersistentIdentity),
                snapshotResult.InaccessibleEndpointCount,
                visible.Select(controller => new VisibleControllerSummary(
                    controller.StableId, controller.CollectionCount,
                    controller.HasPersistentIdentity)).ToArray());
            Console.WriteLine(JsonSerializer.Serialize(result));
            return decision.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new ProbeError(
                "indeterminate", ex.GetType().Name, ex.Message)));
            return IndeterminateExitCode;
        }
    }

    private static string? ValueAfter(string[] args, string key)
    {
        int index = Array.FindIndex(args,
            value => value.Equals(key, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static bool IsStableId(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}

internal static class RawInputVisibility
{
    private const uint RimTypeHid = 2;
    private const uint RidiDeviceName = 0x20000007;

    public static RawVisibilitySnapshot Snapshot()
    {
        uint count = 0;
        uint elementSize = (uint)Marshal.SizeOf<RawInputDeviceList>();
        if (GetRawInputDeviceList(null, ref count, elementSize) != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Could not query the Raw Input device count.");
        var devices = new RawInputDeviceList[count];
        if (count > 0 && GetRawInputDeviceList(devices, ref count, elementSize) == uint.MaxValue)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Could not enumerate Raw Input devices.");

        EndpointInspection[] inspected = devices.Take(checked((int)count))
            .Where(device => device.Type == RimTypeHid)
            .Select(device => GetDeviceName(device.Device))
            .Where(FireControllerPathIdentity.IsMatch)
            .Select(InspectEndpoint)
            .ToArray();
        IReadOnlyList<VisibleController> visible = inspected
            .Where(endpoint => endpoint.IsAccessible)
            .Select(endpoint => new ControllerEndpoint(endpoint.Path,
                FireControllerPathIdentity.NormalizeSerialNumber(endpoint.SerialNumber)))
            .GroupBy(endpoint => FireControllerPathIdentity.NormalizeCollectionPath(
                endpoint.Path), StringComparer.OrdinalIgnoreCase)
            .SelectMany(collectionGroup =>
            {
                string[] serials = collectionGroup
                    .Select(endpoint => endpoint.NormalizedSerialNumber)
                    .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                string? inferredSerial = serials.Length == 1 ? serials[0] : null;
                return collectionGroup.Select(endpoint => endpoint with
                {
                    NormalizedSerialNumber = endpoint.NormalizedSerialNumber ?? inferredSerial
                });
            })
            .GroupBy(endpoint => FireControllerPathIdentity.CreateStableId(
                endpoint.Path, endpoint.NormalizedSerialNumber), StringComparer.OrdinalIgnoreCase)
            .Select(group => new VisibleController(group.Key,
                group.Select(endpoint => endpoint.Path)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                group.All(endpoint => endpoint.NormalizedSerialNumber is not null)))
            .OrderBy(controller => controller.StableId, StringComparer.Ordinal)
            .ToArray();
        return new(visible, inspected.Count(endpoint => !endpoint.IsAccessible));
    }

    private static string GetDeviceName(nint device)
    {
        uint length = 0;
        if (GetRawInputDeviceInfo(device, RidiDeviceName, null, ref length) == uint.MaxValue)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Could not query a Raw Input device path.");
        if (length == 0) return string.Empty;
        var value = new StringBuilder(checked((int)length));
        if (GetRawInputDeviceInfo(device, RidiDeviceName, value, ref length) == uint.MaxValue)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Could not read a Raw Input device path.");
        return value.ToString();
    }

    private static EndpointInspection InspectEndpoint(string path)
    {
        const uint shareRead = 1, shareWrite = 2, openExisting = 3;
        using SafeFileHandle handle = CreateFile(path, 0, shareRead | shareWrite,
            nint.Zero, openExisting, 0, nint.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            if (IsHidHideAccessDenial(error)) return new(path, null, false);
            throw new Win32Exception(error,
                "A physical controller endpoint could not be tested for visibility.");
        }
        nint buffer = Marshal.AllocHGlobal(512);
        try
        {
            string? serial = HidD_GetSerialNumberString(handle, buffer, 512)
                ? Marshal.PtrToStringUni(buffer) : null;
            return new(path, serial, true);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    internal static bool IsHidHideAccessDenial(int errorCode) => errorCode == 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDeviceList
    {
        public nint Device;
        public uint Type;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList(
        [Out] RawInputDeviceList[]? list, ref uint count, uint size);

    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(
        nint device, uint command, StringBuilder? data, ref uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share,
        nint security, uint creation, uint flags, nint template);

    [DllImport("hid.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetSerialNumberString(
        SafeFileHandle handle, nint buffer, int bufferLength);
}

internal static class ProbeDecision
{
    public static ProbeDecisionResult Evaluate(string? expected,
        IReadOnlyList<VisibleController> visible)
    {
        if (expected is null) return new("snapshot", 0);
        if (visible.Any(controller => controller.StableId.Equals(
                expected, StringComparison.OrdinalIgnoreCase)))
            return new("visible", 10);
        if (visible.Any(controller => !controller.HasPersistentIdentity))
            return new("indeterminate", 20);
        return new("hidden", 0);
    }

    public static ProbeDecisionResult EvaluateNoneVisible(
        IReadOnlyList<VisibleController> visible) => visible.Count == 0
        ? new("hidden", 0)
        : new("visible", 10);
}

internal sealed record ControllerEndpoint(string Path, string? NormalizedSerialNumber);
internal sealed record EndpointInspection(string Path, string? SerialNumber,
    bool IsAccessible);
internal sealed record RawVisibilitySnapshot(
    IReadOnlyList<VisibleController> VisibleControllers,
    int InaccessibleEndpointCount);
internal sealed record VisibleController(string StableId, int CollectionCount,
    bool HasPersistentIdentity);
internal sealed record VisibleControllerSummary(string StableId, int CollectionCount,
    bool HasPersistentIdentity);
internal sealed record ProbeResult(string Status, int VisibleControllerCount,
    int UnidentifiedVisibleControllerCount,
    int InaccessibleEndpointCount,
    IReadOnlyList<VisibleControllerSummary> Controllers);
internal sealed record ProbeError(string Status, string ErrorType, string Detail);
internal sealed record ProbeDecisionResult(string Status, int ExitCode);
