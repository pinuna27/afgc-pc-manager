namespace AFGCPCManager.HidHide;

public sealed record HidHideJournal
{
    public int SchemaVersion { get; init; } = 3;
    public HashSet<string> AddedApplicationPaths { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, HashSet<string>> AddedDeviceInstanceIds { get; init; } = new(StringComparer.Ordinal);
    public HashSet<string> PendingHandleResetControllerIds { get; init; } = new(StringComparer.Ordinal);
    public HashSet<string> HandleResetDisconnectedControllerIds { get; init; } = new(StringComparer.Ordinal);
    public bool ActivatedByApplication { get; init; }
}

public sealed record HidHideOwnedState(
    IReadOnlyCollection<string> ControllerIds,
    IReadOnlyCollection<string> PendingHandleResetControllerIds,
    IReadOnlyCollection<string> HandleResetDisconnectedControllerIds);
