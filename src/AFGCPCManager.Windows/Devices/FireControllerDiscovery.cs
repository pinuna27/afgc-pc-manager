using System.Runtime.InteropServices;
using AFGCPCManager.Core.Devices;
using AFGCPCManager.Windows.RawInput;

namespace AFGCPCManager.Windows.Devices;

public sealed partial class FireControllerDiscovery : IFireControllerDiscovery
{
    private const uint CrSuccess = 0, CrBufferSmall = 0x1A;
    private static readonly Guid HidInterfaceClass = new("4D1E55B2-F16F-11CF-88CB-001111000030");

    public Task<IReadOnlyList<DiscoveredFireController>> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var endpoints = new List<(string Path, ushort UsagePage, ushort Usage,
            string? SerialNumber, string? DisplayName)>();
        foreach (string path in EnumeratePresentHidInterfaces())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!FireDevicePathMatcher.IsMatch(path)) continue;
            ControllerDeviceMetadata metadata = ControllerDisplayNameResolver.ResolveMetadata(path);
            (ushort usagePage, ushort usage) = KnownUsage(path);
            endpoints.Add((path, usagePage, usage,
                metadata.SerialNumber, metadata.DisplayName));
        }

        return Task.FromResult(BuildSnapshot(endpoints));
    }

    internal static IReadOnlyList<DiscoveredFireController> BuildSnapshot(
        IEnumerable<(string Path, ushort UsagePage, ushort Usage, string? SerialNumber)> endpoints) =>
        BuildSnapshot(endpoints.Select(endpoint => (endpoint.Path, endpoint.UsagePage,
            endpoint.Usage, endpoint.SerialNumber, (string?)null)));

    internal static IReadOnlyList<DiscoveredFireController> BuildSnapshot(
        IEnumerable<(string Path, ushort UsagePage, ushort Usage, string? SerialNumber,
            string? DisplayName)> endpoints) =>
        endpoints.Where(endpoint => FireControllerPathIdentity.IsMatch(endpoint.Path))
            .Select(endpoint => new
            {
                Endpoint = endpoint,
                NormalizedSerial = FireControllerPathIdentity.NormalizeSerialNumber(
                    endpoint.SerialNumber)
            })
            .GroupBy(endpoint => FireControllerPathIdentity.NormalizeCollectionPath(
                endpoint.Endpoint.Path), StringComparer.OrdinalIgnoreCase)
            .SelectMany(collectionGroup =>
            {
                string[] serials = collectionGroup
                    .Select(endpoint => endpoint.NormalizedSerial)
                    .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                string? inferredSerial = serials.Length == 1 ? serials[0] : null;
                return collectionGroup.Select(endpoint => new
                {
                    endpoint.Endpoint,
                    EffectiveSerial = endpoint.NormalizedSerial ?? inferredSerial
                });
            })
            .GroupBy(endpoint => FireControllerPathIdentity.CreateStableId(
                endpoint.Endpoint.Path, endpoint.EffectiveSerial), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                string stableId = group.Key;
                string displayName = group.Select(endpoint => endpoint.Endpoint.DisplayName)
                    .FirstOrDefault(ControllerDisplayNameResolver.IsSpecific)
                    ?? FireControllerConstants.BluetoothName;
                var identity = new FireControllerIdentity(stableId,
                    displayName,
                    FireControllerConstants.VendorId, FireControllerConstants.ProductId);
                PhysicalDeviceEndpoint[] groupedEndpoints = group
                    .GroupBy(endpoint => endpoint.Endpoint.Path, StringComparer.OrdinalIgnoreCase)
                    .Select(paths => paths.First())
                    .Select(endpoint => new PhysicalDeviceEndpoint(
                        endpoint.Endpoint.Path, endpoint.Endpoint.UsagePage,
                        endpoint.Endpoint.Usage))
                    .OrderBy(endpoint => endpoint.DevicePath, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return new DiscoveredFireController(identity, groupedEndpoints, true);
            })
            .OrderBy(controller => controller.Identity.StableId, StringComparer.Ordinal)
            .ToArray();

    internal static string NormalizeCollectionPath(string path) =>
        FireControllerPathIdentity.NormalizeCollectionPath(path);
    internal static (ushort UsagePage, ushort Usage) KnownUsage(string path)
    {
        if (path.Contains("&Col02", StringComparison.OrdinalIgnoreCase))
            return (0x0C, 1);
        return (1, 5);
    }

    private static string[] EnumeratePresentHidInterfaces()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Guid interfaceClass = HidInterfaceClass;
            uint result = CM_Get_Device_Interface_List_SizeW(
                out uint length, ref interfaceClass, null, 0);
            if (result != CrSuccess)
                throw new InvalidOperationException(
                    $"Windows could not size the HID interface list (CM error 0x{result:X8}).");
            if (length <= 1) return [];
            var buffer = new char[length];
            result = CM_Get_Device_Interface_ListW(
                ref interfaceClass, null, buffer, length, 0);
            if (result == CrBufferSmall) continue;
            if (result != CrSuccess)
                throw new InvalidOperationException(
                    $"Windows could not enumerate HID interfaces (CM error 0x{result:X8}).");
            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
        throw new InvalidOperationException(
            "The HID interface list changed repeatedly during enumeration.");
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_Interface_List_SizeW(out uint length,
        ref Guid interfaceClassGuid, string? deviceId, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_Interface_ListW(ref Guid interfaceClassGuid,
        string? deviceId, [Out] char[] buffer, uint bufferLength, uint flags);
}
