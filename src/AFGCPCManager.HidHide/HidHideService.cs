namespace AFGCPCManager.HidHide;

public sealed class HidHideService
{
    private readonly IHidHideApi _api; private readonly IDeviceInstanceResolver _resolver; private readonly HidHideJournalStore _store; private readonly SemaphoreSlim _gate = new(1, 1);
    public HidHideService(IDeviceInstanceResolver resolver, HidHideJournalStore store) : this(new OfficialHidHideApi(), resolver, store) { }
    internal HidHideService(IHidHideApi api, IDeviceInstanceResolver resolver, HidHideJournalStore store) => (_api, _resolver, _store) = (api, resolver, store);

    public HidHideAvailability GetAvailability()
    {
        if (!_api.IsInstalled) return HidHideAvailability.NotInstalled;
        if (!_api.IsOperational) return HidHideAvailability.NotOperational;
        return _api.IsAppListInverted ? HidHideAvailability.UnsupportedConfiguration : HidHideAvailability.Available;
    }
    public Version? GetInstalledVersion() => _api.IsInstalled ? _api.LocalDriverVersion : null;

    public async Task HideAsync(string stableControllerId, IEnumerable<string> deviceInterfacePaths, string applicationPath, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (GetAvailability() is not HidHideAvailability.Available) throw new InvalidOperationException("Physical controller hiding is not available.");
            HidHideJournal journal = await _store.LoadAsync(cancellationToken);
            string fullApplicationPath = Path.GetFullPath(applicationPath);
            if (!_api.ApplicationPaths.Contains(fullApplicationPath, StringComparer.OrdinalIgnoreCase))
            {
                journal.AddedApplicationPaths.Add(fullApplicationPath); await _store.SaveAsync(journal, cancellationToken); _api.AddApplicationPath(fullApplicationPath);
            }
            if (!journal.AddedDeviceInstanceIds.TryGetValue(stableControllerId, out var owned)) journal.AddedDeviceInstanceIds[stableControllerId] = owned = new(StringComparer.OrdinalIgnoreCase);
            var currentlyBlocked = _api.BlockedInstanceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string interfacePath in deviceInterfacePaths)
            {
                string instanceId = _resolver.Resolve(interfacePath);
                if (currentlyBlocked.Contains(instanceId)) continue;
                owned.Add(instanceId); await _store.SaveAsync(journal, cancellationToken); _api.AddBlockedInstanceId(instanceId); currentlyBlocked.Add(instanceId);
            }
            if (!_api.IsActive) _api.IsActive = true;
        }
        finally { _gate.Release(); }
    }

    public async Task UnhideAsync(string stableControllerId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            HidHideJournal journal = await _store.LoadAsync(cancellationToken);
            if (!journal.AddedDeviceInstanceIds.Remove(stableControllerId, out var ids)) return;
            foreach (string id in ids) if (_api.BlockedInstanceIds.Contains(id, StringComparer.OrdinalIgnoreCase)) _api.RemoveBlockedInstanceId(id);
            await _store.SaveAsync(journal, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task RecoverOwnedEntriesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            HidHideJournal journal = await _store.LoadAsync(cancellationToken);
            var blocked = _api.BlockedInstanceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string id in journal.AddedDeviceInstanceIds.Values.SelectMany(x => x).Distinct(StringComparer.OrdinalIgnoreCase)) if (blocked.Contains(id)) _api.RemoveBlockedInstanceId(id);
            var apps = _api.ApplicationPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string path in journal.AddedApplicationPaths) if (apps.Contains(path)) _api.RemoveApplicationPath(path);
            await _store.SaveAsync(new(), cancellationToken);
        }
        finally { _gate.Release(); }
    }
}
