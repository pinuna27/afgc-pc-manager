namespace AFGCPCManager.HidHide;

public sealed record HidHideJournal
{
    public int SchemaVersion { get; init; } = 1;
    public HashSet<string> AddedApplicationPaths { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, HashSet<string>> AddedDeviceInstanceIds { get; init; } = new(StringComparer.Ordinal);
}
