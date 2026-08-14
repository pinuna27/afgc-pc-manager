using AFGCPCManager.Core.Devices;
using AFGCPCManager.Windows.Devices;

namespace AFGCPCManager.App;

internal sealed record ControllerIdentificationLightResetResult(
    int Attempted,
    int Succeeded);

internal sealed class ControllerIdentificationLightManager
{
    private readonly Func<IEnumerable<string>, byte, bool> _apply;
    private readonly Dictionary<string, LightState> _applied = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LightState> _failed = new(StringComparer.Ordinal);

    public ControllerIdentificationLightManager()
    {
        var writer = new FireControllerLightWriter();
        _apply = writer.TrySetIdentificationLight;
    }

    internal ControllerIdentificationLightManager(
        Func<IEnumerable<string>, byte, bool> apply) =>
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));

    public void Reconcile(bool enabled, IReadOnlyList<DiscoveredFireController> discovered,
        IReadOnlyList<RegisteredController> registered, Action<string> recordEvent)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        ArgumentNullException.ThrowIfNull(registered);
        ArgumentNullException.ThrowIfNull(recordEvent);

        HashSet<string> present = discovered.Where(device =>
                device.IsConnected && device.Endpoints.Count > 0)
            .Select(device => device.Identity.StableId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (string id in _applied.Keys.Where(id => !present.Contains(id)).ToArray())
            _applied.Remove(id);
        foreach (string id in _failed.Keys.Where(id => !present.Contains(id)).ToArray())
            _failed.Remove(id);

        if (!enabled)
        {
            var devices = discovered.Where(controller => controller.IsConnected)
                .ToDictionary(controller => controller.Identity.StableId,
                    StringComparer.Ordinal);
            foreach (string id in _applied.Keys.ToArray())
            {
                if (!devices.TryGetValue(id, out DiscoveredFireController? device))
                    continue;

                string[] paths = device.Endpoints.Select(endpoint => endpoint.DevicePath)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
                if (_apply(paths, 0))
                    recordEvent("Turned off the controller identification lights; " +
                        "the app will leave them alone until light control is enabled again.");
                else
                    recordEvent("Could not turn off the controller identification lights; " +
                        "the app will leave them alone until light control is enabled again.");
            }
            Clear();
            return;
        }

        var registrations = registered.ToDictionary(
            controller => controller.StableId, StringComparer.Ordinal);
        foreach (DiscoveredFireController device in discovered.Where(controller =>
                     controller.IsConnected && controller.Endpoints.Count > 0))
        {
            if (!registrations.TryGetValue(device.Identity.StableId,
                    out RegisteredController? registration)) continue;

            byte mask = ControllerIdentificationLightPattern.ForRegistrationOrder(
                registration.RegistrationOrder);
            string[] paths = device.Endpoints.Select(endpoint => endpoint.DevicePath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            var desired = new LightState(mask, string.Join('\n', paths));
            if (_applied.GetValueOrDefault(device.Identity.StableId) == desired) continue;

            if (_apply(paths, mask))
            {
                _applied[device.Identity.StableId] = desired;
                _failed.Remove(device.Identity.StableId);
                recordEvent($"Applied identification-light pattern {IdentificationLightDisplay.Format(mask)} to Controller {registration.RegistrationOrder}.");
            }
            else if (_failed.GetValueOrDefault(device.Identity.StableId) != desired)
            {
                _failed[device.Identity.StableId] = desired;
                recordEvent($"Could not apply the identification-light pattern to Controller {registration.RegistrationOrder}; no HID collection accepted the output report.");
            }
        }
    }

    public void Forget(string stableId)
    {
        _applied.Remove(stableId);
        _failed.Remove(stableId);
    }

    public void Clear()
    {
        _applied.Clear();
        _failed.Clear();
    }

    public ControllerIdentificationLightResetResult ResetRegistered(
        IReadOnlyList<DiscoveredFireController> discovered,
        IReadOnlyList<RegisteredController> registered)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        ArgumentNullException.ThrowIfNull(registered);

        HashSet<string> registeredIds = registered
            .Select(controller => controller.StableId)
            .ToHashSet(StringComparer.Ordinal);
        int attempted = 0;
        int succeeded = 0;
        foreach (DiscoveredFireController controller in discovered.Where(controller =>
                     controller.IsConnected
                     && controller.Endpoints.Count > 0
                     && registeredIds.Contains(controller.Identity.StableId)))
        {
            attempted++;
            string[] paths = controller.Endpoints
                .Select(endpoint => endpoint.DevicePath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (_apply(paths, 0))
                succeeded++;
        }
        Clear();
        return new(attempted, succeeded);
    }

    private sealed record LightState(byte Mask, string EndpointSet);
}
