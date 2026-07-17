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
