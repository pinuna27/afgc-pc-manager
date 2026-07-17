namespace AFGCPCManager.Core.Devices;

public sealed record RegisteredController
{
    public required string StableId { get; init; }
    public required string DisplayName { get; init; }
    public required int RegistrationOrder { get; init; }
    public uint? PreferredVJoyId { get; init; }
    public DateTimeOffset? LastSeenUtc { get; init; }
}
