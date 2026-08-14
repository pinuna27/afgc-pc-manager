namespace AFGCPCManager.HidHide;

public sealed record HidHideDependencyStatus(bool Installed, bool Operational, Version? Version);

public sealed record HidHideRecoveryResult(
    int RemovedDeviceInstanceIds,
    int RemovedApplicationPaths,
    bool DeactivatedByApplication)
{
    public bool Changed => RemovedDeviceInstanceIds > 0
        || RemovedApplicationPaths > 0
        || DeactivatedByApplication;
}

public static class HidHideDependencyProbe
{
    public static HidHideDependencyStatus Detect()
    {
        var api = new OfficialHidHideApi();
        return new(api.IsInstalled, api.IsOperational, api.IsInstalled ? api.LocalDriverVersion : null);
    }
}

public sealed class HidHideService
{
    private readonly IHidHideApi _api; private readonly IDeviceInstanceResolver _resolver; private readonly HidHideJournalStore _store; private readonly IHidHideVisibilityVerifier? _visibilityVerifier; private readonly SemaphoreSlim _gate = new(1, 1);
    public HidHideService(IDeviceInstanceResolver resolver, HidHideJournalStore store) : this(
        new OfficialHidHideApi(), resolver, store,
        new ProcessHidHideVisibilityVerifier(Path.Combine(
            AppContext.BaseDirectory, "AFGCPCManager.HidVisibilityProbe.exe")))
    { }
    internal HidHideService(IHidHideApi api, IDeviceInstanceResolver resolver,
        HidHideJournalStore store, IHidHideVisibilityVerifier? visibilityVerifier = null) =>
        (_api, _resolver, _store, _visibilityVerifier) = (api, resolver, store, visibilityVerifier);

    public HidHideAvailability GetAvailability()
    {
        if (!_api.IsInstalled) return HidHideAvailability.NotInstalled;
        if (!_api.IsOperational) return HidHideAvailability.NotOperational;
        return _api.IsAppListInverted ? HidHideAvailability.UnsupportedConfiguration : HidHideAvailability.Available;
    }
    public Version? GetInstalledVersion() => _api.IsInstalled ? _api.LocalDriverVersion : null;

    public async Task<HidHideOwnedState> PrepareOwnedEntriesAsync(
        string applicationPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(applicationPath))
            throw new ArgumentException("The application path is required.", nameof(applicationPath));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (GetAvailability() is not HidHideAvailability.Available)
                throw new InvalidOperationException("Physical controller hiding is not available.");
            HidHideJournal journal = await _store.LoadAsync(cancellationToken);
            string fullApplicationPath = Path.GetFullPath(applicationPath);
            if (!ApplicationPathIdentity.Contains(_api.ApplicationPaths, fullApplicationPath))
            {
                journal.AddedApplicationPaths.Add(fullApplicationPath);
                await _store.SaveAsync(journal, cancellationToken);
                _api.AddApplicationPath(fullApplicationPath);
            }
            else
            {
                // Persist schema migrations even when no driver mutation is needed.
                await _store.SaveAsync(journal, cancellationToken);
            }

            return new(
                journal.AddedDeviceInstanceIds.Keys.ToArray(),
                journal.PendingHandleResetControllerIds.ToArray(),
                journal.HandleResetDisconnectedControllerIds.ToArray());
        }
        finally { _gate.Release(); }
    }

    public async Task<HidHideVisibilityResult> HideAndVerifyAsync(
        string stableControllerId,
        IEnumerable<string> deviceInterfacePaths,
        string applicationPath,
        CancellationToken cancellationToken = default)
    {
        string[] paths = deviceInterfacePaths?.Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            ?? throw new ArgumentNullException(nameof(deviceInterfacePaths));
        await HideAsync(stableControllerId, paths, applicationPath, cancellationToken);
        bool handleResetRequired = await IsHandleResetPendingAsync(
            stableControllerId, cancellationToken);
        if (_visibilityVerifier is null)
            return new(HidHideVisibilityStatus.Indeterminate,
                "No independent physical-controller visibility verifier is configured.",
                handleResetRequired);

        try
        {
            string fullApplicationPath = Path.GetFullPath(applicationPath);
            string probePath = Path.GetFullPath(_visibilityVerifier.ProbeApplicationPath);
            string[] expectedInstanceIds = paths.Select(_resolver.Resolve)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var blocked = _api.BlockedInstanceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            IReadOnlyCollection<string> allowed = _api.ApplicationPaths;
            if (!_api.IsActive || !ApplicationPathIdentity.Contains(allowed, fullApplicationPath)
                || expectedInstanceIds.Length == 0 || expectedInstanceIds.Any(id => !blocked.Contains(id)))
                return new(HidHideVisibilityStatus.Indeterminate,
                    "HidHide did not retain the required active, application, and blocked-device configuration.");
            if (ApplicationPathIdentity.Contains(allowed, probePath))
                return new(HidHideVisibilityStatus.Indeterminate,
                    "The independent visibility probe is whitelisted in HidHide and cannot verify isolation. Remove that probe entry from HidHide Configuration Client.");
            HidHideVisibilityResult visibility = await _visibilityVerifier.VerifyHiddenAsync(
                stableControllerId, cancellationToken);
            return visibility with { HandleResetRequired = handleResetRequired };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return new(HidHideVisibilityStatus.Indeterminate,
                $"HidHide configuration could not be read back safely: {ex.Message}");
        }
    }

    public async Task AcknowledgeHandleResetAsync(
        string stableControllerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stableControllerId))
            throw new ArgumentException("The controller identity is required.", nameof(stableControllerId));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            HidHideJournal journal = await _store.LoadAsync(cancellationToken);
            if (!journal.PendingHandleResetControllerIds.Remove(stableControllerId)) return;
            journal.HandleResetDisconnectedControllerIds.Remove(stableControllerId);
            await _store.SaveAsync(journal, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task MarkHandleResetDisconnectedAsync(
        string stableControllerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stableControllerId))
            throw new ArgumentException("The controller identity is required.", nameof(stableControllerId));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            HidHideJournal journal = await _store.LoadAsync(cancellationToken);
            if (!journal.PendingHandleResetControllerIds.Contains(stableControllerId)
                || !journal.HandleResetDisconnectedControllerIds.Add(stableControllerId)) return;
            await _store.SaveAsync(journal, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<bool> IsHandleResetPendingAsync(
        string stableControllerId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            HidHideJournal journal = await _store.LoadAsync(cancellationToken);
            return journal.PendingHandleResetControllerIds.Contains(stableControllerId);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> HideAsync(string stableControllerId, IEnumerable<string> deviceInterfacePaths, string applicationPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stableControllerId)) throw new ArgumentException("The controller identity is required.", nameof(stableControllerId));
        if (string.IsNullOrWhiteSpace(applicationPath)) throw new ArgumentException("The application path is required.", nameof(applicationPath));
        string[] interfacePaths = deviceInterfacePaths?.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            ?? throw new ArgumentNullException(nameof(deviceInterfacePaths));
        if (interfacePaths.Length == 0) throw new ArgumentException("At least one controller device path is required.", nameof(deviceInterfacePaths));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            bool reconnectRequired = false;
            if (GetAvailability() is not HidHideAvailability.Available) throw new InvalidOperationException("Physical controller hiding is not available.");
            HidHideJournal journal = await _store.LoadAsync(cancellationToken);
            string fullApplicationPath = Path.GetFullPath(applicationPath);
            if (!ApplicationPathIdentity.Contains(_api.ApplicationPaths, fullApplicationPath))
            {
                journal.AddedApplicationPaths.Add(fullApplicationPath); await _store.SaveAsync(journal, cancellationToken); _api.AddApplicationPath(fullApplicationPath);
            }
            if (!journal.AddedDeviceInstanceIds.TryGetValue(stableControllerId, out var owned)) journal.AddedDeviceInstanceIds[stableControllerId] = owned = new(StringComparer.OrdinalIgnoreCase);
            var currentlyBlocked = _api.BlockedInstanceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string interfacePath in interfacePaths)
            {
                string instanceId = _resolver.Resolve(interfacePath);
                if (currentlyBlocked.Contains(instanceId)) continue;
                owned.Add(instanceId);
                journal.PendingHandleResetControllerIds.Add(stableControllerId);
                journal.HandleResetDisconnectedControllerIds.Remove(stableControllerId);
                await _store.SaveAsync(journal, cancellationToken);
                _api.AddBlockedInstanceId(instanceId); currentlyBlocked.Add(instanceId);
                reconnectRequired = true;
            }
            if (!_api.IsActive)
            {
                journal = journal with { ActivatedByApplication = true };
                journal.PendingHandleResetControllerIds.Add(stableControllerId);
                journal.HandleResetDisconnectedControllerIds.Remove(stableControllerId);
                await _store.SaveAsync(journal, cancellationToken);
                _api.IsActive = true;
                reconnectRequired = true;
            }
            return reconnectRequired;
        }
        finally { _gate.Release(); }
    }

    public async Task UnhideAsync(string stableControllerId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            HidHideJournal journal = await _store.LoadAsync(cancellationToken);
            bool removedController = journal.AddedDeviceInstanceIds.Remove(stableControllerId, out var ids);
            journal.PendingHandleResetControllerIds.Remove(stableControllerId);
            journal.HandleResetDisconnectedControllerIds.Remove(stableControllerId);
            if (removedController)
                foreach (string id in ids!) if (_api.BlockedInstanceIds.Contains(id, StringComparer.OrdinalIgnoreCase)) _api.RemoveBlockedInstanceId(id);
            if (!removedController && journal.AddedDeviceInstanceIds.Count > 0) return;
            if (journal.AddedDeviceInstanceIds.Count == 0)
            {
                foreach (string path in journal.AddedApplicationPaths)
                    if (ApplicationPathIdentity.Contains(_api.ApplicationPaths, path)) _api.RemoveApplicationPath(path);
                journal.AddedApplicationPaths.Clear();
                if (journal.ActivatedByApplication && _api.IsActive)
                    _api.IsActive = false;
                journal = journal with { ActivatedByApplication = false };
            }
            await _store.SaveAsync(journal, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task<HidHideRecoveryResult> RecoverOwnedEntriesAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            HidHideJournal journal = await _store.LoadAsync(cancellationToken);
            var blocked = _api.BlockedInstanceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            int removedDeviceInstanceIds = 0;
            foreach (string id in journal.AddedDeviceInstanceIds.Values
                         .SelectMany(x => x)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!blocked.Contains(id)) continue;
                _api.RemoveBlockedInstanceId(id);
                removedDeviceInstanceIds++;
            }
            IReadOnlyCollection<string> apps = _api.ApplicationPaths;
            int removedApplicationPaths = 0;
            foreach (string path in journal.AddedApplicationPaths)
            {
                if (!ApplicationPathIdentity.Contains(apps, path)) continue;
                _api.RemoveApplicationPath(path);
                removedApplicationPaths++;
            }
            bool deactivated = journal.ActivatedByApplication && _api.IsActive;
            if (deactivated)
                _api.IsActive = false;
            await _store.SaveAsync(new(), cancellationToken);
            return new(removedDeviceInstanceIds, removedApplicationPaths, deactivated);
        }
        finally { _gate.Release(); }
    }
}
