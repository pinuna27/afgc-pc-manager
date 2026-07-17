using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AFGCPCManager.Core.Devices;
using AFGCPCManager.Windows.RawInput;

namespace AFGCPCManager.Windows.Devices;

public sealed partial class FireControllerDiscovery : IFireControllerDiscovery
{
    private const uint RidiDeviceName = 0x20000007, RidiDeviceInfo = 0x2000000b, RimTypeHid = 2;

    public Task<IReadOnlyList<DiscoveredFireController>> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        uint count = 0, elementSize = (uint)Marshal.SizeOf<RawInputDeviceList>();
        if (GetRawInputDeviceList(null, ref count, elementSize) != 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var devices = new RawInputDeviceList[count];
        if (count > 0 && GetRawInputDeviceList(devices, ref count, elementSize) == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());

        var endpoints = new List<(string GroupKey, PhysicalDeviceEndpoint Endpoint)>();
        foreach (var device in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (device.Type != RimTypeHid) continue;
            string path = GetDeviceName(device.Device);
            if (!FireDevicePathMatcher.IsMatch(path) || !TryGetHidInfo(device.Device, out var info)) continue;
            endpoints.Add((NormalizeCollectionPath(path), new(path, checked((ushort)info.UsagePage), checked((ushort)info.Usage))));
        }

        IReadOnlyList<DiscoveredFireController> result = endpoints.GroupBy(x => x.GroupKey, StringComparer.OrdinalIgnoreCase).Select(group =>
        {
            string stableId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("AFGC-PC-MANAGER\0" + group.Key.ToUpperInvariant())));
            var identity = new FireControllerIdentity(stableId, FireControllerConstants.BluetoothName, FireControllerConstants.VendorId, FireControllerConstants.ProductId);
            return new DiscoveredFireController(identity, group.Select(x => x.Endpoint).ToArray(), true);
        }).OrderBy(x => x.Identity.StableId, StringComparer.Ordinal).ToArray();
        return Task.FromResult(result);
    }

    internal static string NormalizeCollectionPath(string path) => CollectionPattern().Replace(path, "", 1);
    private static bool TryGetHidInfo(nint device, out HidInfo hid)
    {
        var info = new RawInputDeviceInfo { Size = (uint)Marshal.SizeOf<RawInputDeviceInfo>() }; uint size = info.Size;
        if (GetRawInputDeviceInfo(device, RidiDeviceInfo, ref info, ref size) == uint.MaxValue) { hid = default; return false; }
        hid = info.Hid; return info.Type == RimTypeHid;
    }
    private static string GetDeviceName(nint device)
    {
        uint length = 0; GetRawInputDeviceInfoName(device, RidiDeviceName, null, ref length);
        if (length == 0) return string.Empty; var value = new StringBuilder((int)length);
        return GetRawInputDeviceInfoName(device, RidiDeviceName, value, ref length) == uint.MaxValue ? string.Empty : value.ToString();
    }

    [GeneratedRegex(@"&COL[0-9A-F]{2}(?=#)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex CollectionPattern();
    [StructLayout(LayoutKind.Sequential)] private struct RawInputDeviceList { public nint Device; public uint Type; }
    [StructLayout(LayoutKind.Sequential)] private struct HidInfo { public uint VendorId, ProductId, VersionNumber, UsagePage, Usage; }
    [StructLayout(LayoutKind.Explicit)] private struct RawInputDeviceInfo { [FieldOffset(0)] public uint Size; [FieldOffset(4)] public uint Type; [FieldOffset(8)] public HidInfo Hid; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputDeviceList([Out] RawInputDeviceList[]? list, ref uint count, uint size);
    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetRawInputDeviceInfoName(nint device, uint command, StringBuilder? data, ref uint size);
    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true)] private static extern uint GetRawInputDeviceInfo(nint device, uint command, ref RawInputDeviceInfo data, ref uint size);
}
