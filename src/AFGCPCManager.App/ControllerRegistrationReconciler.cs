using AFGCPCManager.Core.Devices;
using AFGCPCManager.Core.Registration;
using AFGCPCManager.Core.Settings;

namespace AFGCPCManager.App;

internal static class ControllerRegistrationReconciler
{
    public static bool Reconcile(ControllerRegistry registry,
        IReadOnlyList<DiscoveredFireController> discovered, DateTimeOffset discoveredAt)
    {
        bool changed = false;
        SettingsDocument settings = registry.Snapshot;

        foreach (DiscoveredFireController device in discovered.Where(device =>
                     device.IsConnected && device.Endpoints.Count > 0))
        {
            string stableId = device.Identity.StableId;
            if (settings.ExcludedControllerIds.Contains(stableId)) continue;

            RegisteredController? existing = settings.Controllers.FirstOrDefault(controller =>
                controller.StableId.Equals(stableId, StringComparison.Ordinal));
            if (existing is not null)
            {
                if (existing.DisplayName.Equals(
                        device.Identity.DisplayName, StringComparison.Ordinal)) continue;
                registry.Register(device.Identity, discoveredAt);
            }
            else
            {
                if (!settings.Application.AutomaticallyFindControllers) continue;
                registry.Register(device.Identity, discoveredAt);
            }

            changed = true;
            settings = registry.Snapshot;
        }

        return changed;
    }
}
