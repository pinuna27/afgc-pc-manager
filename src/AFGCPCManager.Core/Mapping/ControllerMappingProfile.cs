namespace AFGCPCManager.Core.Mapping;

public sealed record ControllerMappingProfile
{
    public HomeButtonMode HomeButton { get; init; } = HomeButtonMode.Guide;
    public GameCircleButtonMode GameCircleButton { get; init; } =
        GameCircleButtonMode.Guide;
    public MediaRowMode MediaRow { get; init; } = MediaRowMode.Media;

    public static ControllerMappingProfile Default { get; } = new();
}
