namespace AFGCPCManager.Core.Mapping;

public static class EffectiveMappingResolver
{
    public static ControllerMappingProfile Resolve(
        ControllerMappingProfile defaults,
        ControllerMappingOverrides? overrides)
    {
        ArgumentNullException.ThrowIfNull(defaults);

        ControllerMappingProfile resolved = new()
        {
            HomeButton = overrides?.HomeButton ?? defaults.HomeButton,
            GameCircleButton = overrides?.GameCircleButton ?? defaults.GameCircleButton,
            MediaRow = overrides?.MediaRow ?? defaults.MediaRow
        };

        return resolved.HomeButton == HomeButtonMode.Guide
            ? resolved
            : resolved with { GameCircleButton = GameCircleButtonMode.Guide };
    }
}
