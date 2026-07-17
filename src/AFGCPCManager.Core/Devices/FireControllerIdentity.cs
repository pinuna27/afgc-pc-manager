namespace AFGCPCManager.Core.Devices;

public sealed record FireControllerIdentity(string StableId, string DisplayName, ushort VendorId, ushort ProductId)
{
    public string ToRedactedString() => StableId.Length <= 12 ? StableId : StableId[..12];
}
