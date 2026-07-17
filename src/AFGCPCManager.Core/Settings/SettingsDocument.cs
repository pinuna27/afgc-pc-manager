using AFGCPCManager.Core.Devices;
using AFGCPCManager.Core.Mapping;

namespace AFGCPCManager.Core.Settings;

public sealed record SettingsDocument
{
    public int SchemaVersion { get; init; } = 1;
    public AppSettings Application { get; init; } = new();
    public ControllerMappingProfile DefaultMapping { get; init; } = new();
    public Dictionary<string, ControllerMappingOverrides> Overrides { get; init; } = [];
    public List<RegisteredController> Controllers { get; init; } = [];
    public HashSet<string> ExcludedControllerIds { get; init; } = [];
}
