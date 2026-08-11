using AFGCPCManager.App.Settings;
using AFGCPCManager.Core.Bridge;
using AFGCPCManager.Core.Devices;
using AFGCPCManager.Core.Registration;
using AFGCPCManager.Core.Settings;
using AFGCPCManager.Core.Updates;
using AFGCPCManager.Core.Output;
using System.Diagnostics;
using AFGCPCManager.VJoy;
using AFGCPCManager.HidHide;
using AFGCPCManager.Windows.Consumer;
using AFGCPCManager.Windows.Devices;
using AFGCPCManager.Windows.RawInput;
using AFGCPCManager.Windows.Startup;

namespace AFGCPCManager.App;

internal sealed class BridgeRuntime : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _mutation = new(1, 1);
    private readonly Dictionary<string, RuntimeEntry> _controllers = [];
    private readonly Dictionary<string, string> _controllerIssues = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hiddenControllers = new(StringComparer.Ordinal);
    private readonly ControllerReconnectGate _reconnectGate = new();
    private readonly ControllerIdentificationLightManager _identificationLights = new();
    private readonly VJoyDirectInputNameManager _vjoyDisplayName = new();
    private readonly IFireControllerDiscovery _discovery = new FireControllerDiscovery();
    private readonly ISettingsStore _settingsStore = new JsonSettingsStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AFGC PC Manager", "settings.json"));
    private readonly HidHideService _hidHide = new(new DeviceInstanceResolver(), new HidHideJournalStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AFGC PC Manager", "hidhide-journal.json")));
    private readonly ControllerOutputSafetyGate _outputSafetyGate;
    private readonly WindowsStartupManager _startup = new();
    private readonly HttpClient _updateClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private ControllerRegistry? _registry; private VJoyBackend? _backend; private Task? _runTask;
    private IReadOnlyList<DiscoveredFireController> _lastDiscovery = [];
    private IReadOnlyList<ControllerRowModel> _lastRows = [];
    private readonly Queue<string> _recentEvents = new();
    private string _lastStatus = "Starting";
    private int _compatibleVJoyDevices;
    private int _lastProvisioningTarget;
    private int _failedProvisioningTarget = -1;
    private HidHideAvailability _hidingAvailability = HidHideAvailability.NotInstalled;
    private int ConnectedDiscoveryCount => _lastDiscovery.Count(x => x.IsConnected && x.Endpoints.Count > 0);

    public BridgeRuntime()
    {
        _outputSafetyGate = new(
            (id, paths, app, cancellationToken) =>
                _hidHide.HideAndVerifyAsync(id, paths, app, cancellationToken),
            (id, cancellationToken) => _hidHide.UnhideAsync(id, cancellationToken),
            id => _hiddenControllers.Add(id));
    }

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<IReadOnlyList<ControllerRowModel>>? ControllersChanged;
    public event EventHandler<IReadOnlyList<UpdateCheckResult.Available>>? UpdatesAvailable;

    public async Task StartAsync()
    {
        try
        {
            SettingsDocument settings;
            try { settings = await _settingsStore.LoadAsync(_stop.Token); }
            catch (InvalidDataException ex) { settings = new(); SetStatus($"Settings could not be loaded; defaults are active — {ex.Message}"); }
            _registry = new(settings);
            try
            {
                if (settings.Application.HidePhysicalControllers)
                {
                    string processPath = Environment.ProcessPath
                        ?? throw new InvalidOperationException("The application executable path is unavailable.");
                    HidHideOwnedState owned = await _hidHide.PrepareOwnedEntriesAsync(
                        processPath, _stop.Token);
                    foreach (string id in owned.ControllerIds)
                        _hiddenControllers.Add(id);
                    foreach (string id in owned.PendingHandleResetControllerIds)
                        _reconnectGate.Require(id,
                            owned.HandleResetDisconnectedControllerIds.Contains(id));
                }
                else
                {
                    await _hidHide.RecoverOwnedEntriesAsync(_stop.Token);
                    _hiddenControllers.Clear();
                    _reconnectGate.Clear();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RecordEvent($"Previous HidHide state could not be recovered safely: {ex.Message}");
            }
            try
            {
                string processPath = settings.Application.HidePhysicalControllers
                    ? Environment.ProcessPath
                        ?? throw new InvalidOperationException("The application executable path is unavailable.")
                    : string.Empty;
                _startup.SetEnabled(settings.Application.StartWithWindows, processPath);
            }
            catch (Exception ex) { RecordEvent($"Startup registration could not be updated: {ex.Message}"); }
            try { await RefreshDiscoveryAsync(_stop.Token); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { RecordEvent($"Initial controller discovery failed: {ex.Message}"); }
            PublishControllers();
            SetStatus(ConnectedDiscoveryCount == 0
                ? "Waiting for an Amazon Fire Game Controller..."
                : $"Found {ConnectedDiscoveryCount} Fire controller(s); starting virtual output...");
            _runTask = RunAsync();
            if (settings.Application.AutomaticallyCheckForUpdates) _ = CheckUpdatesAsync();
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) { SetStatus($"Not running — {ex.Message}"); }
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    if (_backend is null)
                    {
                        await _mutation.WaitAsync(_stop.Token);
                        try
                        {
                            await RefreshDiscoveryAsync(_stop.Token);
                            PublishControllers();
                            SetStatus(ConnectedDiscoveryCount == 0
                                ? "Waiting for an Amazon Fire Game Controller..."
                                : $"Found {ConnectedDiscoveryCount} Fire controller(s); starting virtual output...");
                        }
                        finally { _mutation.Release(); }

                        await InitializeBackendAsync(_stop.Token);
                    }

                    await ReconcileAsync(_stop.Token);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    SetStatus(_backend is null
                        ? $"Virtual output unavailable — {ex.Message} Retrying..."
                        : $"Controller monitoring error — {ex.Message} Retrying...");
                }

                await Task.Delay(TimeSpan.FromSeconds(2), _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) { SetStatus($"Controller runtime stopped — {ex.Message}"); }
    }

    private async Task InitializeBackendAsync(CancellationToken cancellationToken)
    {
        (VJoyBackend Backend, int Compatible) initialized = await Task.Run(() =>
        {
            var backend = new VJoyBackend();
            try { return (backend, backend.EnumerateDevices().Count(x => x.Capabilities is not null)); }
            catch { backend.Dispose(); throw; }
        }, cancellationToken);

        if (cancellationToken.IsCancellationRequested)
        {
            initialized.Backend.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
        }

        _backend = initialized.Backend;
        _compatibleVJoyDevices = initialized.Compatible;
        SynchronizeVJoyDisplayName(_registry!.Snapshot);
        try { _hidingAvailability = _hidHide.GetAvailability(); }
        catch { _hidingAvailability = HidHideAvailability.NotOperational; }
        PublishControllers();
    }

    private async Task RefreshDiscoveryAsync(CancellationToken cancellationToken)
    {
        _lastDiscovery = await _discovery.SnapshotAsync(cancellationToken);
        SettingsDocument before = _registry!.Snapshot;
        bool changed = ControllerRegistrationReconciler.Reconcile(
            _registry, _lastDiscovery, DateTimeOffset.UtcNow);
        if (changed)
        {
            try { await _settingsStore.SaveAsync(_registry.Snapshot, cancellationToken); }
            catch
            {
                _registry = new(before);
                throw;
            }
        }
        SettingsDocument current = _registry.Snapshot;
        _identificationLights.Reconcile(
            current.Application.ControlIdentificationLights,
            _lastDiscovery, current.Controllers, RecordEvent);
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        await _mutation.WaitAsync(cancellationToken);
        try
        {
            int previousHandleCount = CurrentHandleCount();
            await RefreshDiscoveryAsync(cancellationToken);
            TrackHandleGrowth("controller discovery", ref previousHandleCount);
            SettingsDocument settings = _registry!.Snapshot;
            var present = _lastDiscovery
                .Where(x => x.IsConnected && x.Endpoints.Count > 0)
                .Select(x => x.Identity.StableId)
                .ToHashSet(StringComparer.Ordinal);
            bool setupRequired = false;
            ControllerReconnectObservation reconnectObservation = _reconnectGate.Observe(present);
            foreach (string id in reconnectObservation.NewlyDisconnectedControllerIds)
            {
                try
                {
                    await _hidHide.MarkHandleResetDisconnectedAsync(id, cancellationToken);
                    RecordEvent($"Controller {id[..Math.Min(12, id.Length)]} disconnected; waiting for it to reconnect.");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    setupRequired = true;
                    SetControllerIssue(id,
                        $"The controller disconnect could not be recorded safely: {ex.Message}");
                }
            }
            foreach (string id in reconnectObservation.ReadyControllerIds)
            {
                try
                {
                    await _hidHide.AcknowledgeHandleResetAsync(id, cancellationToken);
                    _reconnectGate.Complete(id);
                    _controllerIssues.Remove(id);
                    RecordEvent($"Verified a complete controller disconnect and reconnect for {id[..Math.Min(12, id.Length)]}.");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    setupRequired = true;
                    SetControllerIssue(id,
                        $"The completed controller reconnect could not be recorded safely: {ex.Message}");
                }
            }
            foreach (string id in _controllers.Keys
                         .Where(id => !present.Contains(id) || _controllers[id].IsCompleted)
                         .ToArray())
            {
                bool disconnected = !present.Contains(id);
                RuntimeEntry runtime = _controllers[id];
                _controllers.Remove(id);
                try { await runtime.DisposeAsync(); }
                catch (Exception ex) { RecordEvent($"Could not release controller output {runtime.DeviceId}: {ex.Message}"); }
                if (disconnected)
                {
                    _controllerIssues.Remove(id);
                }
                else RecordEvent($"Restarting controller {id[..Math.Min(12, id.Length)]} after its bridge stopped.");
            }
            int requiredOutputs = present.Count(id => settings.Controllers.Any(x => x.StableId == id));
            if (requiredOutputs != _lastProvisioningTarget) { _lastProvisioningTarget = requiredOutputs; _failedProvisioningTarget = -1; }
            IReadOnlyList<OutputDeviceInfo> outputs = _backend!.EnumerateDevices();
            TrackHandleGrowth("vJoy enumeration", ref previousHandleCount);
            _compatibleVJoyDevices = outputs.Count(x => x.Capabilities is not null && x.Status is OutputDeviceStatus.Free or OutputDeviceStatus.Owned);
            if (requiredOutputs > _compatibleVJoyDevices && requiredOutputs != _failedProvisioningTarget)
            {
                try
                {
                    await new VJoyDeviceProvisioner().EnsureCompatibleDeviceCountAsync(requiredOutputs, cancellationToken);
                    _compatibleVJoyDevices = _backend.EnumerateDevices().Count(x => x.Capabilities is not null);
                    RecordEvent($"Expanded vJoy capacity to {_compatibleVJoyDevices} compatible device(s).");
                }
                catch (Exception ex)
                {
                    _failedProvisioningTarget = requiredOutputs; setupRequired = true;
                    RecordEvent($"Could not provision {requiredOutputs} vJoy outputs: {ex.Message}");
                }
            }
            foreach (var device in _lastDiscovery)
            {
                if (!device.IsConnected || device.Endpoints.Count == 0) continue;
                RegisteredController? registration = settings.Controllers.FirstOrDefault(x => x.StableId == device.Identity.StableId); if (registration is null) continue;
                if (_controllers.ContainsKey(device.Identity.StableId)) continue;
                if (_reconnectGate.IsPending(device.Identity.StableId))
                {
                    setupRequired = true;
                    SetControllerIssue(device.Identity.StableId,
                        "Reconnect required: turn this controller off and back on once. Virtual output is disabled until then.");
                    continue;
                }
                var output = _backend.TryAcquire(registration.PreferredVJoyId);
                TrackHandleGrowth("vJoy acquisition", ref previousHandleCount);
                if (output is null)
                {
                    setupRequired = true;
                    SetControllerIssue(device.Identity.StableId,
                        "No compatible, free vJoy output is available.");
                    continue;
                }
                string[] inputPaths = device.Endpoints.Select(x => x.DevicePath).ToArray();
                if (settings.Application.HidePhysicalControllers && inputPaths.Length == 0)
                {
                    setupRequired = true;
                    output.Dispose();
                    SetControllerIssue(device.Identity.StableId,
                        "Virtual output was withheld because no physical controller endpoints were available for hiding.");
                    continue;
                }
                string processPath = Environment.ProcessPath
                    ?? throw new InvalidOperationException("The application executable path is unavailable.");
                OutputSafetyAuthorization authorization = await _outputSafetyGate.AuthorizeAsync(
                    settings.Application.HidePhysicalControllers,
                    device.Identity.StableId, inputPaths, processPath, output, cancellationToken);
                TrackHandleGrowth("physical-isolation verification", ref previousHandleCount);
                if (!authorization.IsAuthorized)
                {
                    setupRequired = true;
                    if (authorization.HandleResetRequired)
                        _reconnectGate.Require(device.Identity.StableId);
                    SetControllerIssue(device.Identity.StableId,
                        $"Virtual output was withheld because physical hiding was not verified: {authorization.Detail}");
                    continue;
                }
                DirectHidControllerInput input;
                ControllerBridge bridge;
                try
                {
                    input = new DirectHidControllerInput(inputPaths);
                    bridge = new ControllerBridge(input, output, new WindowsConsumerActionEmitter(),
                        _registry.GetEffectiveMapping(device.Identity.StableId));
                }
                catch
                {
                    await UnhideIfOwnedAsync(device.Identity.StableId, CancellationToken.None);
                    output.Dispose();
                    throw;
                }
                if (registration.PreferredVJoyId != output.DeviceId)
                {
                    SettingsDocument beforeAssignment = _registry.Snapshot;
                    _registry.SetPreferredVJoyId(device.Identity.StableId, output.DeviceId);
                    try { await _settingsStore.SaveAsync(_registry.Snapshot, cancellationToken); }
                    catch
                    {
                        _registry = new(beforeAssignment);
                        await UnhideIfOwnedAsync(device.Identity.StableId, CancellationToken.None);
                        try { await bridge.DisposeAsync(); }
                        catch (Exception ex) { RecordEvent($"Could not release controller output {output.DeviceId}: {ex.Message}"); }
                        throw;
                    }
                }
                var runtime = new RuntimeEntry(bridge, output.DeviceId, device.Identity.ToRedactedString(), OnRuntimeStopped); _controllers.Add(device.Identity.StableId, runtime); runtime.Start();
                _controllerIssues.Remove(device.Identity.StableId);
            }
            SetStatus(setupRequired ? "Setup required." : ConnectedDiscoveryCount == 0 ? "Waiting for an Amazon Fire Game Controller..." : $"Running — {_controllers.Count} of {ConnectedDiscoveryCount} Fire controller(s) mapped.");
            SynchronizeVJoyDisplayName(_registry.Snapshot);
            PublishControllers();
        }
        finally { _mutation.Release(); }
    }

    public SettingsDocument GetSettings() => _registry?.Snapshot ?? new();
    public DiagnosticSnapshot GetDiagnostics()
    {
        string[] events; lock (_recentEvents) events = _recentEvents.ToArray();
        bool startupEnabled;
        try { startupEnabled = _startup.IsEnabled(); }
        catch { startupEnabled = false; }
        return new(typeof(Program).Assembly.GetName().Version?.ToString() ?? "Unknown", _lastStatus, _lastRows.Count, _lastRows.Count(x => x.IsConnected),
            _compatibleVJoyDevices, _lastRows, _hidingAvailability.ToString(), startupEnabled, events);
    }
    public IReadOnlyList<DiscoveredFireController> GetAddCandidates() => _lastDiscovery.Where(x => x.IsConnected && x.Endpoints.Count > 0 && !GetSettings().Controllers.Any(c => c.StableId == x.Identity.StableId)).ToArray();
    private async Task CheckUpdatesAsync()
    {
        try
        {
            var sources = new List<ReleaseSource> { new(ReleaseComponent.AfgcPcManager, "pinuna27", "afgc-pc-manager", typeof(Program).Assembly.GetName().Version ?? new(0, 0)) };
            Version? vjoy = DetectVJoyVersion(); if (vjoy is not null) sources.Add(new(ReleaseComponent.VJoy, "BrunnerInnovation", "vJoy", vjoy));
            Version? hidhide = null; try { hidhide = _hidHide.GetInstalledVersion(); } catch { } if (hidhide is not null) sources.Add(new(ReleaseComponent.HidHide, "nefarius", "HidHide", hidhide));
            var checker = new GitHubReleaseChecker(_updateClient); UpdateCheckResult[] results = await Task.WhenAll(sources.Select(x => checker.CheckAsync(x, _stop.Token)));
            var available = results.OfType<UpdateCheckResult.Available>().ToArray();
            foreach (var failure in results.OfType<UpdateCheckResult.Failed>()) RecordEvent($"Update check failed for {failure.Component}: {failure.Message}");
            if (available.Length > 0) { RecordEvent($"{available.Length} stable update(s) available."); UpdatesAvailable?.Invoke(this, available); }
            else RecordEvent("Stable release update check completed; everything is current.");
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) { RecordEvent($"Update check failed: {ex.Message}"); }
    }
    private static Version? DetectVJoyVersion()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (string path in new[] { Path.Combine(programFiles, "vJoy", "x64", "vJoyInterface.dll"), Path.Combine(programFiles, "vJoy", "vJoyInterface.dll") })
            if (File.Exists(path) && Version.TryParse(FileVersionInfo.GetVersionInfo(path).FileVersion, out Version? version)) return version;
        return null;
    }
    public async Task AddControllerAsync(string id)
    {
        await _mutation.WaitAsync();
        try
        {
            var device = _lastDiscovery.FirstOrDefault(x => x.IsConnected && x.Endpoints.Count > 0 && x.Identity.StableId == id)
                ?? throw new InvalidOperationException("That controller is no longer connected.");
            SettingsDocument before = _registry!.Snapshot;
            _registry.Register(device.Identity, DateTimeOffset.UtcNow);
            try { await _settingsStore.SaveAsync(_registry.Snapshot); }
            catch { _registry = new(before); throw; }
            PublishControllers();
        }
        finally { _mutation.Release(); }
    }
    public async Task RemoveControllerAsync(string id)
    {
        await _mutation.WaitAsync();
        try
        {
            SettingsDocument before = _registry!.Snapshot;
            if (_controllers.Remove(id, out var runtime))
            {
                try { await runtime.DisposeAsync(); }
                catch (Exception ex) { RecordEvent($"Could not release controller output {runtime.DeviceId}: {ex.Message}"); }
            }
            await UnhideIfOwnedAsync(id, CancellationToken.None);
            _reconnectGate.Forget(id);
            _identificationLights.Forget(id);
            if (_registry.RemoveAndExclude(id))
            {
                try { await _settingsStore.SaveAsync(_registry.Snapshot); }
                catch { _registry = new(before); throw; }
            }
            PublishControllers();
        }
        finally { _mutation.Release(); }
    }
    public async Task SaveSettingsAsync(SettingsDocument settings)
    {
        await _mutation.WaitAsync();
        try
        {
            SettingsDocument before = _registry?.Snapshot ?? new();
            string processPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("The application executable path is unavailable.");
            _startup.SetEnabled(settings.Application.StartWithWindows, processPath);
            try { await _settingsStore.SaveAsync(settings); }
            catch
            {
                try { _startup.SetEnabled(before.Application.StartWithWindows, processPath); } catch { }
                throw;
            }

            _registry = new(settings);
            _identificationLights.Clear();
            await DisposeAllControllersAsync();
            if (before.Application.HidePhysicalControllers && !settings.Application.HidePhysicalControllers)
            {
                await _hidHide.RecoverOwnedEntriesAsync();
                _hiddenControllers.Clear();
                _reconnectGate.Clear();
            }
            PublishControllers();
        }
        finally { _mutation.Release(); }
    }

    private async Task UnhideIfOwnedAsync(string id, CancellationToken cancellationToken)
    {
        if (!_hiddenControllers.Contains(id)) return;
        try
        {
            await _hidHide.UnhideAsync(id, cancellationToken);
            _hiddenControllers.Remove(id);
        }
        catch (Exception ex) { RecordEvent($"Could not restore physical controller visibility: {ex.Message}"); }
    }

    private async Task DisposeAllControllersAsync()
    {
        foreach ((string id, RuntimeEntry runtime) in _controllers.ToArray())
        {
            _controllers.Remove(id);
            try { await runtime.DisposeAsync(); }
            catch (Exception ex) { RecordEvent($"Could not release controller output {runtime.DeviceId}: {ex.Message}"); }
        }
    }

    private void PublishControllers()
    {
        if (_registry is null) return; var present = _lastDiscovery.Where(x => x.IsConnected && x.Endpoints.Count > 0).Select(x => x.Identity.StableId).ToHashSet(StringComparer.Ordinal);
        SettingsDocument settings = _registry.Snapshot;
        var rows = settings.Controllers.OrderBy(x => x.RegistrationOrder).Select(x => new ControllerRowModel(x.StableId, x.DisplayName, x.RegistrationOrder, present.Contains(x.StableId), _controllers.TryGetValue(x.StableId, out var runtime) ? runtime.DeviceId : null, _controllerIssues.GetValueOrDefault(x.StableId), settings.Application.ControlIdentificationLights ? ControllerIdentificationLightPattern.ForRegistrationOrder(x.RegistrationOrder) : null)).ToArray(); _lastRows = rows;
        ControllersChanged?.Invoke(this, rows);
    }

    private void OnRuntimeStopped(string id, Exception? error) => SetStatus(error is null ? $"Controller {id} stopped." : $"Controller {id} stopped — {error.Message}");
    private void SetStatus(string status)
    {
        if (string.Equals(_lastStatus, status, StringComparison.Ordinal)) return;
        _lastStatus = status; RecordEvent(status);
        StatusChanged?.Invoke(this, status);
    }
    private void RecordEvent(string message)
    {
        lock (_recentEvents)
        {
            _recentEvents.Enqueue($"{DateTimeOffset.Now:HH:mm:ss} {message}");
            while (_recentEvents.Count > 20) _recentEvents.Dequeue();
        }
        RuntimeEventLog.Write(message);
    }
    private void TrackHandleGrowth(string stage, ref int previous)
    {
        int current = CurrentHandleCount();
        int growth = current - previous;
        if (growth >= 256)
            RecordEvent($"Resource diagnostic: {stage} retained {growth} additional process handles.");
        previous = current;
    }
    private static int CurrentHandleCount()
    {
        using Process process = Process.GetCurrentProcess();
        return process.HandleCount;
    }
    private void SetControllerIssue(string stableControllerId, string message)
    {
        if (_controllerIssues.TryGetValue(stableControllerId, out string? existing)
            && string.Equals(existing, message, StringComparison.Ordinal)) return;
        _controllerIssues[stableControllerId] = message;
        RecordEvent(message);
    }
    private void SynchronizeVJoyDisplayName(SettingsDocument settings)
    {
        try
        {
            VJoyDisplayNameUpdate? update = _vjoyDisplayName.Synchronize(settings.Controllers);
            if (update?.Changed == true)
                RecordEvent($"Renamed the shared vJoy DirectInput device to '{update.Name}'.");
        }
        catch (Exception ex)
        {
            RecordEvent($"The vJoy DirectInput display name could not be updated: {ex.Message}");
        }
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); if (_runTask is not null) try { await _runTask; } catch (OperationCanceledException) { }
        await DisposeAllControllersAsync();
        foreach (string id in _hiddenControllers.ToArray()) await UnhideIfOwnedAsync(id, CancellationToken.None);
        _backend?.Dispose(); _updateClient.Dispose(); _mutation.Dispose(); _stop.Dispose();
    }
    private sealed class RuntimeEntry(ControllerBridge bridge, uint deviceId, string id, Action<string, Exception?> stopped) : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new(); private Task? _task; public uint DeviceId { get; } = deviceId;
        public bool IsCompleted => _task?.IsCompleted == true;
        public void Start() => _task = RunAsync();
        private async Task RunAsync() { try { await bridge.RunAsync(_stop.Token); stopped(id, null); } catch (OperationCanceledException) when (_stop.IsCancellationRequested) { } catch (Exception ex) { stopped(id, ex); } }
        public async ValueTask DisposeAsync() { _stop.Cancel(); if (_task is not null) await _task; await bridge.DisposeAsync(); _stop.Dispose(); }
    }
}

internal sealed record ControllerRowModel(string StableId, string DisplayName, int RegistrationOrder,
    bool IsConnected, uint? VJoyDeviceId, string? Issue,
    byte? IdentificationLightMask = null);
internal sealed record DiagnosticSnapshot(string Version, string BridgeStatus, int RegisteredControllers, int ConnectedControllers, int CompatibleVJoyDevices, IReadOnlyList<ControllerRowModel> Controllers, string HidingStatus, bool StartupEnabled, IReadOnlyList<string> RecentEvents)
{
    public string ToReport()
    {
        var lines = new List<string> { "AFGC PC Manager diagnostics", $"Version: {Version}", $"Bridge: {BridgeStatus}", $"Controllers: {ConnectedControllers} connected / {RegisteredControllers} registered", $"Compatible vJoy devices: {CompatibleVJoyDevices}", $"Physical hiding: {HidingStatus}", $"Start with Windows: {StartupEnabled}", "", "Controller assignments:" };
        lines.AddRange(Controllers.Select(x => $"- Controller {x.RegistrationOrder} [{x.StableId[..Math.Min(12, x.StableId.Length)]}]: {(x.IsConnected ? "connected" : "disconnected")}, output {(x.VJoyDeviceId?.ToString() ?? "none")}, lights {IdentificationLightDisplay.Format(x.IdentificationLightMask)}{(x.Issue is null ? string.Empty : $", issue: {x.Issue}")}"));
        lines.Add(""); lines.Add("Recent events:"); lines.AddRange(RecentEvents.Select(x => $"- {x}")); return string.Join(Environment.NewLine, lines);
    }
}
