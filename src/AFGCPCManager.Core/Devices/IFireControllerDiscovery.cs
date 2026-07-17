namespace AFGCPCManager.Core.Devices;

public interface IFireControllerDiscovery
{
    Task<IReadOnlyList<DiscoveredFireController>> SnapshotAsync(CancellationToken cancellationToken = default);
}
