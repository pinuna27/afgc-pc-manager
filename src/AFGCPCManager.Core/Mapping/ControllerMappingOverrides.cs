namespace AFGCPCManager.Core.Mapping;

public sealed record ControllerMappingOverrides
{
    public HomeButtonMode? HomeButton { get; init; }
    public GameCircleButtonMode? GameCircleButton { get; init; }
    public MediaRowMode? MediaRow { get; init; }
}
