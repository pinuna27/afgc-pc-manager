using System.Runtime.InteropServices;
using System.Text;
using AFGCPCManager.Core.Devices;

namespace AFGCPCManager.Windows.Devices;

internal static class ControllerDisplayNameResolver
{
    private const int MaximumParentDepth = 8;
    private const uint CrSuccess = 0, CrBufferSmall = 0x1a, DevPropTypeString = 0x12;
    private static readonly DevPropKey DeviceInstanceId = new(
        new("78c34fc8-104a-4aca-9ea4-524d52996e57"), 256);
    private static readonly DevPropKey DeviceFriendlyName = new(
        new("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
    private static readonly DevPropKey DeviceBusReportedDescription = new(
        new("540b947e-8b40-45bc-a8a2-6a0b894cbda2"), 4);

    private static readonly string[] GenericNameFragments =
    [
        "HID-compliant",
        "HID Gamepad",
        "Bluetooth HID Device",
        "Bluetooth Device (RFCOMM",
        "Bluetooth LE Generic Attribute",
        "Bluetooth Adapter",
        "Bluetooth Radio",
        "Wireless Bluetooth",
        "USB Input Device",
        "Microsoft Bluetooth Enumerator",
        "Generic Bluetooth"
    ];

    public static string Resolve(string devicePath, string? productString)
        => ResolveMetadata(devicePath, productString).DisplayName;

    public static ControllerDeviceMetadata ResolveMetadata(
        string devicePath, string? productString = null)
    {
        string? serialNumber = null;
        try
        {
            string? instanceId = GetInterfaceStringProperty(devicePath, DeviceInstanceId);
            if (instanceId is not null
                && CM_Locate_DevNodeW(out uint device, instanceId, 0) == CrSuccess)
            {
                var properties = new List<(string? FriendlyName, string? BusDescription)>();
                for (int depth = 0; depth < MaximumParentDepth; depth++)
                {
                    string? currentInstanceId = GetDeviceStringProperty(device, DeviceInstanceId);
                    serialNumber ??= ExtractBluetoothAddress(currentInstanceId);
                    properties.Add((
                        GetDeviceStringProperty(device, DeviceFriendlyName),
                        GetDeviceStringProperty(device, DeviceBusReportedDescription)));

                    if (CM_Get_Parent(out uint parent, device, 0) != CrSuccess) break;
                    device = parent;
                }

                string? resolved = SelectName(properties, productString);
                if (resolved is not null)
                    return new(serialNumber, resolved);
            }
        }
        catch
        {
            // Device metadata is best-effort. Raw Input discovery must still work when
            // the PnP node disappears during enumeration or property access is denied.
        }

        return new(serialNumber,
            SelectName([], productString) ?? FireControllerConstants.BluetoothName);
    }

    internal static string? ExtractBluetoothAddress(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(instanceId,
            @"(?i)(?:&0&|DEV_|_)([0-9A-F]{12})(?:_C[0-9A-F]+)?$");
        return match.Success
            ? FireControllerPathIdentity.NormalizeSerialNumber(match.Groups[1].Value)
            : null;
    }

    internal static string? SelectName(
        IEnumerable<(string? FriendlyName, string? BusDescription)> properties,
        string? productString)
    {
        string? defaultFriendlyName = null;
        string? busDescription = null;
        foreach ((string? rawFriendlyName, string? rawBusDescription) in properties)
        {
            string? friendlyName = Normalize(rawFriendlyName);
            if (IsSpecific(friendlyName))
            {
                if (!friendlyName!.Equals(FireControllerConstants.BluetoothName,
                        StringComparison.OrdinalIgnoreCase))
                    return friendlyName;
                defaultFriendlyName ??= friendlyName;
            }

            string? normalizedBusDescription = Normalize(rawBusDescription);
            if (IsSpecific(normalizedBusDescription))
                busDescription ??= normalizedBusDescription;
        }

        if (defaultFriendlyName is not null) return defaultFriendlyName;
        if (busDescription is not null) return busDescription;
        string? normalizedProduct = Normalize(productString);
        return IsSpecific(normalizedProduct) ? normalizedProduct : null;
    }

    internal static bool IsSpecific(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !GenericNameFragments.Any(fragment =>
            value.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string normalized = string.Join(' ', value.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 128 ? normalized : normalized[..128];
    }

    private static string? GetInterfaceStringProperty(string devicePath, DevPropKey key)
    {
        uint size = 0;
        uint result = CM_Get_Device_Interface_PropertyW(
            devicePath, ref key, out uint propertyType, null, ref size, 0);
        if (result != CrBufferSmall || size == 0) return null;
        var buffer = new byte[size];
        result = CM_Get_Device_Interface_PropertyW(
            devicePath, ref key, out propertyType, buffer, ref size, 0);
        return result == CrSuccess && propertyType == DevPropTypeString
            ? DecodeString(buffer, size) : null;
    }

    private static string? GetDeviceStringProperty(uint device, DevPropKey key)
    {
        uint size = 0;
        uint result = CM_Get_DevNode_PropertyW(
            device, ref key, out uint propertyType, null, ref size, 0);
        if (result != CrBufferSmall || size == 0) return null;
        var buffer = new byte[size];
        result = CM_Get_DevNode_PropertyW(
            device, ref key, out propertyType, buffer, ref size, 0);
        return result == CrSuccess && propertyType == DevPropTypeString
            ? DecodeString(buffer, size) : null;
    }

    private static string? DecodeString(byte[] buffer, uint size)
    {
        int length = checked((int)Math.Min(size, (uint)buffer.Length));
        string value = Encoding.Unicode.GetString(buffer, 0, length).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_Interface_PropertyW(string deviceInterface,
        ref DevPropKey propertyKey, out uint propertyType, byte[]? propertyBuffer,
        ref uint propertyBufferSize, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Locate_DevNodeW(out uint deviceInstance,
        string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_DevNode_PropertyW(uint deviceInstance,
        ref DevPropKey propertyKey, out uint propertyType, byte[]? propertyBuffer,
        ref uint propertyBufferSize, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_Parent(out uint parentDeviceInstance,
        uint deviceInstance, uint flags);
}

internal sealed record ControllerDeviceMetadata(string? SerialNumber, string DisplayName);
