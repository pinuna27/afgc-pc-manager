namespace AFGCPCManager.Core.Devices;

public sealed record DiscoveredFireController(
    FireControllerIdentity Identity,
    IReadOnlyList<PhysicalDeviceEndpoint> Endpoints,
    bool IsConnected);
