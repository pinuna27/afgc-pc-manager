using AFGCPCManager.Core.Settings;

namespace AFGCPCManager.App;

internal static class RuntimeSettingsChangePolicy
{
    public static bool RequiresBridgeRestart(SettingsDocument before, SettingsDocument after) =>
        before.Application.OutputMode != after.Application.OutputMode
        || before.Application.HidePhysicalControllers != after.Application.HidePhysicalControllers
        || MappingsChanged(before, after);

    private static bool MappingsChanged(SettingsDocument before, SettingsDocument after)
    {
        if (before.DefaultMapping != after.DefaultMapping
            || before.Overrides.Count != after.Overrides.Count)
            return true;

        return before.Overrides.Any(pair =>
            !after.Overrides.TryGetValue(pair.Key, out var value)
            || pair.Value != value);
    }
}
