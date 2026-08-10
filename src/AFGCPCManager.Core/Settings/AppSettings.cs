namespace AFGCPCManager.Core.Settings;

public sealed record AppSettings
{
    public bool StartWithWindows { get; init; } = true;
    public bool ShowTrayOnAutomaticStart { get; init; } = true;
    public bool AutomaticallyFindControllers { get; init; } = true;
    public bool HidePhysicalControllers { get; init; } = true;
    public bool ControlIdentificationLights { get; init; }
    public bool AutomaticallyCheckForUpdates { get; init; } = true;
    public bool ShowNotifications { get; init; } = true;
}
