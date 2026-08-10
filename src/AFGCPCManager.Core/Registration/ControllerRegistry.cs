using AFGCPCManager.Core.Devices;
using AFGCPCManager.Core.Mapping;
using AFGCPCManager.Core.Settings;

namespace AFGCPCManager.Core.Registration;

public sealed class ControllerRegistry(SettingsDocument initial)
{
    private readonly object _gate = new();
    private SettingsDocument _document = SettingsValidator.Validate(initial);
    public SettingsDocument Snapshot { get { lock (_gate) return Clone(_document); } }

    public RegisteredController Register(FireControllerIdentity identity, DateTimeOffset seenUtc)
    {
        ArgumentNullException.ThrowIfNull(identity);
        lock (_gate)
        {
            RegisteredController? existing = _document.Controllers.FirstOrDefault(x => x.StableId == identity.StableId);
            RegisteredController updated = existing is null
                ? new() { StableId = identity.StableId, DisplayName = identity.DisplayName, RegistrationOrder = _document.Controllers.Count == 0 ? 1 : _document.Controllers.Max(x => x.RegistrationOrder) + 1, LastSeenUtc = seenUtc }
                : existing with { DisplayName = identity.DisplayName, LastSeenUtc = seenUtc };
            var controllers = _document.Controllers.Where(x => x.StableId != identity.StableId).Append(updated).OrderBy(x => x.RegistrationOrder).ToList();
            var excluded = new HashSet<string>(_document.ExcludedControllerIds, StringComparer.Ordinal); excluded.Remove(identity.StableId);
            _document = _document with { Controllers = controllers, ExcludedControllerIds = excluded };
            return updated;
        }
    }

    public RegisteredController MigrateIdentity(
        string previousStableId, FireControllerIdentity identity, DateTimeOffset seenUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previousStableId);
        ArgumentNullException.ThrowIfNull(identity);
        lock (_gate)
        {
            RegisteredController previous = _document.Controllers.FirstOrDefault(controller =>
                controller.StableId.Equals(previousStableId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException("The previous controller registration no longer exists.");
            if (_document.Controllers.Any(controller =>
                    controller.StableId.Equals(identity.StableId, StringComparison.Ordinal)
                    && !controller.StableId.Equals(previousStableId, StringComparison.Ordinal)))
                throw new InvalidOperationException("The replacement controller identity is already registered.");

            RegisteredController updated = previous with
            {
                StableId = identity.StableId,
                DisplayName = identity.DisplayName,
                LastSeenUtc = seenUtc
            };
            var controllers = _document.Controllers
                .Where(controller => !controller.StableId.Equals(previousStableId, StringComparison.Ordinal))
                .Append(updated).OrderBy(controller => controller.RegistrationOrder).ToList();
            var overrides = new Dictionary<string, ControllerMappingOverrides>(
                _document.Overrides, StringComparer.Ordinal);
            if (overrides.Remove(previousStableId, out ControllerMappingOverrides? mapping))
                overrides[identity.StableId] = mapping;
            var excluded = new HashSet<string>(_document.ExcludedControllerIds, StringComparer.Ordinal);
            excluded.Remove(previousStableId);
            excluded.Remove(identity.StableId);
            _document = _document with
            {
                Controllers = controllers,
                Overrides = overrides,
                ExcludedControllerIds = excluded
            };
            return updated;
        }
    }

    public bool MigrateExcludedIdentity(string previousStableId, string stableId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previousStableId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stableId);
        lock (_gate)
        {
            var excluded = new HashSet<string>(_document.ExcludedControllerIds, StringComparer.Ordinal);
            if (!excluded.Remove(previousStableId)) return false;
            excluded.Add(stableId);
            _document = _document with { ExcludedControllerIds = excluded };
            return true;
        }
    }

    public bool RemoveAndExclude(string stableId)
    {
        lock (_gate)
        {
            if (!_document.Controllers.Any(x => x.StableId == stableId)) return false;
            var excluded = new HashSet<string>(_document.ExcludedControllerIds, StringComparer.Ordinal) { stableId };
            var overrides = new Dictionary<string, ControllerMappingOverrides>(_document.Overrides, StringComparer.Ordinal); overrides.Remove(stableId);
            _document = _document with { Controllers = _document.Controllers.Where(x => x.StableId != stableId).ToList(), ExcludedControllerIds = excluded, Overrides = overrides };
            return true;
        }
    }

    public bool SetPreferredVJoyId(string stableId, uint? deviceId)
    {
        if (deviceId is 0 or > 16) throw new ArgumentOutOfRangeException(nameof(deviceId));
        lock (_gate)
        {
            int index = _document.Controllers.FindIndex(x => x.StableId == stableId); if (index < 0) return false;
            var controllers = _document.Controllers.ToList(); controllers[index] = controllers[index] with { PreferredVJoyId = deviceId };
            _document = _document with { Controllers = controllers }; return true;
        }
    }

    public ControllerMappingProfile GetEffectiveMapping(string stableId)
    {
        lock (_gate) return EffectiveMappingResolver.Resolve(_document.DefaultMapping, _document.Overrides.GetValueOrDefault(stableId));
    }

    private static SettingsDocument Clone(SettingsDocument value) => value with
    {
        Controllers = value.Controllers.ToList(), ExcludedControllerIds = new(value.ExcludedControllerIds, StringComparer.Ordinal), Overrides = new(value.Overrides, StringComparer.Ordinal)
    };
}
