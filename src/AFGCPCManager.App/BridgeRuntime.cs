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
    private readonly HashSet<string> _hiddenControllers = new(StringComparer.Ordinal);
    private readonly IFireControllerDiscovery _discovery = new FireControllerDiscovery();
    private readonly ISettingsStore _settingsStore = new JsonSettingsStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AFGC PC Manager", "settings.json"));
    private readonly HidHideService _hidHide = new(new DeviceInstanceResolver(), new HidHideJournalStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AFGC PC Manager", "hidhide-journal.json")));
    private readonly WindowsStartupManager _startup = new();
    private readonly HttpClient _updateClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private ControllerRegistry? _registry; private VJoyBackend? _backend; private Task? _monitorTask;
    private IReadOnlyList<DiscoveredFireController> _lastDiscovery = [];
    private IReadOnlyList<ControllerRowModel> _lastRows = [];
    private readonly Queue<string> _recentEvents = new();
    private string _lastStatus = "Starting";
    private int _compatibleVJoyDevices;
    private int _lastProvisioningTarget;
    private int _failedProvisioningTarget = -1;
    private HidHideAvailability _hidingAvailability = HidHideAvailability.NotInstalled;

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
            _registry = new(settings); _startup.SetEnabled(settings.Application.StartWithWindows, Environment.ProcessPath!); _backend = new VJoyBackend();
            _compatibleVJoyDevices = _backend.EnumerateDevices().Count(x => x.Capabilities is not null);
            try { _hidingAvailability = _hidHide.GetAvailability(); } catch { _hidingAvailability = HidHideAvailability.NotOperational; }
            _monitorTask = MonitorAsync(); PublishControllers(); if (settings.Application.AutomaticallyCheckForUpdates) _ = CheckUpdatesAsync(settings.Application);
        }
        catch (Exception ex) { SetStatus($"Not running — {ex.Message}"); }
    }

    private async Task MonitorAsync()
    {
        try { while (!_stop.IsCancellationRequested) { await ReconcileAsync(_stop.Token); await Task.Delay(TimeSpan.FromSeconds(2), _stop.Token); } }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) { SetStatus($"Controller monitoring stopped — {ex.Message}"); }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        await _mutation.WaitAsync(cancellationToken);
        try
        {
            _lastDiscovery = await _discovery.SnapshotAsync(cancellationToken);
            SettingsDocument settings = _registry!.Snapshot; bool changed = false;
            if (settings.Application.AutomaticallyFindControllers)
            {
                foreach (var device in _lastDiscovery)
                    if (!settings.Controllers.Any(x => x.StableId == device.Identity.StableId) && !settings.ExcludedControllerIds.Contains(device.Identity.StableId))
                    { _registry.Register(device.Identity, DateTimeOffset.UtcNow); changed = true; }
                if (changed) { await _settingsStore.SaveAsync(_registry.Snapshot, cancellationToken); settings = _registry.Snapshot; }
            }
            var present = _lastDiscovery.Select(x => x.Identity.StableId).ToHashSet(StringComparer.Ordinal);
            foreach (string removed in _controllers.Keys.Where(id => !present.Contains(id)).ToArray()) { await _controllers[removed].DisposeAsync(); _controllers.Remove(removed); await UnhideIfOwnedAsync(removed, cancellationToken); }
            bool setupRequired = false;
            int requiredOutputs = _lastDiscovery.Count(device => settings.Controllers.Any(x => x.StableId == device.Identity.StableId));
            if (requiredOutputs != _lastProvisioningTarget) { _lastProvisioningTarget = requiredOutputs; _failedProvisioningTarget = -1; }
            IReadOnlyList<OutputDeviceInfo> outputs = _backend!.EnumerateDevices();
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
                if (_controllers.ContainsKey(device.Identity.StableId)) continue;
                RegisteredController? registration = settings.Controllers.FirstOrDefault(x => x.StableId == device.Identity.StableId); if (registration is null) continue;
                var output = _backend.TryAcquire(registration.PreferredVJoyId); if (output is null) continue;
                var bridge = new ControllerBridge(new RawInputControllerInput(device.Endpoints.Select(x => x.DevicePath)), output, new WindowsConsumerActionEmitter(), _registry.GetEffectiveMapping(device.Identity.StableId));
                if (settings.Application.HidePhysicalControllers)
                {
                    try { await _hidHide.HideAsync(device.Identity.StableId, device.Endpoints.Select(x => x.DevicePath), Environment.ProcessPath!, cancellationToken); _hiddenControllers.Add(device.Identity.StableId); }
                    catch { setupRequired = true; try { await _hidHide.UnhideAsync(device.Identity.StableId, CancellationToken.None); } catch { } }
                }
                if (registration.PreferredVJoyId != output.DeviceId) { _registry.SetPreferredVJoyId(device.Identity.StableId, output.DeviceId); await _settingsStore.SaveAsync(_registry.Snapshot, cancellationToken); }
                var runtime = new RuntimeEntry(bridge, output.DeviceId, device.Identity.ToRedactedString(), OnRuntimeStopped); _controllers.Add(device.Identity.StableId, runtime); runtime.Start();
            }
            SetStatus(setupRequired ? "Setup required." : _lastDiscovery.Count == 0 ? "Waiting for an Amazon Fire Game Controller..." : $"Running — {_controllers.Count} of {_lastDiscovery.Count} Fire controller(s) mapped.");
            PublishControllers();
        }
        finally { _mutation.Release(); }
    }

    public SettingsDocument GetSettings() => _registry?.Snapshot ?? new();
    public DiagnosticSnapshot GetDiagnostics()
    {
        string[] events; lock (_recentEvents) events = _recentEvents.ToArray();
        return new(typeof(Program).Assembly.GetName().Version?.ToString() ?? "Unknown", _lastStatus, _lastRows.Count, _lastRows.Count(x => x.IsConnected),
            _compatibleVJoyDevices, _lastRows, _hidingAvailability.ToString(), _startup.IsEnabled(), events);
    }
    public IReadOnlyList<DiscoveredFireController> GetAddCandidates() => _lastDiscovery.Where(x => !GetSettings().Controllers.Any(c => c.StableId == x.Identity.StableId)).ToArray();
    private async Task CheckUpdatesAsync(AppSettings settings)
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
            if (settings.AutomaticallyInstallUpdates && available.Any(x => x.Component == ReleaseComponent.AfgcPcManager))
            {
                string setup = Path.Combine(AppContext.BaseDirectory, "AFGCPCManager.Setup.exe");
                if (File.Exists(setup)) { Process.Start(new ProcessStartInfo(setup, "--update") { UseShellExecute = true }); RecordEvent("Verified automatic application update started."); }
                else RecordEvent("Automatic update could not start because Repair Setup is unavailable.");
            }
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
        await _mutation.WaitAsync(); try { var device = _lastDiscovery.FirstOrDefault(x => x.Identity.StableId == id) ?? throw new InvalidOperationException("That controller is no longer connected."); _registry!.Register(device.Identity, DateTimeOffset.UtcNow); await _settingsStore.SaveAsync(_registry.Snapshot); PublishControllers(); } finally { _mutation.Release(); }
    }
    public async Task RemoveControllerAsync(string id)
    {
        await _mutation.WaitAsync(); try { if (_controllers.Remove(id, out var runtime)) await runtime.DisposeAsync(); await UnhideIfOwnedAsync(id, CancellationToken.None); if (_registry!.RemoveAndExclude(id)) await _settingsStore.SaveAsync(_registry.Snapshot); PublishControllers(); } finally { _mutation.Release(); }
    }
    public async Task SaveSettingsAsync(SettingsDocument settings)
    {
        await _mutation.WaitAsync(); try { bool wasHiding = _registry?.Snapshot.Application.HidePhysicalControllers == true; await _settingsStore.SaveAsync(settings); _startup.SetEnabled(settings.Application.StartWithWindows, Environment.ProcessPath!); _registry = new(settings); foreach (var runtime in _controllers.Values) await runtime.DisposeAsync(); _controllers.Clear(); if (wasHiding && !settings.Application.HidePhysicalControllers) { await _hidHide.RecoverOwnedEntriesAsync(); _hiddenControllers.Clear(); } PublishControllers(); } finally { _mutation.Release(); }
    }

    private async Task UnhideIfOwnedAsync(string id, CancellationToken cancellationToken)
    {
        if (!_hiddenControllers.Remove(id)) return;
        try { await _hidHide.UnhideAsync(id, cancellationToken); } catch { }
    }

    private void PublishControllers()
    {
        if (_registry is null) return; var present = _lastDiscovery.Select(x => x.Identity.StableId).ToHashSet(StringComparer.Ordinal);
        var rows = _registry.Snapshot.Controllers.OrderBy(x => x.RegistrationOrder).Select(x => new ControllerRowModel(x.StableId, x.DisplayName, x.RegistrationOrder, present.Contains(x.StableId), _controllers.TryGetValue(x.StableId, out var runtime) ? runtime.DeviceId : null)).ToArray(); _lastRows = rows;
        ControllersChanged?.Invoke(this, rows);
    }
    private void OnRuntimeStopped(string id, Exception? error) => SetStatus(error is null ? $"Controller {id} stopped." : $"Controller {id} stopped — {error.Message}");
    private void SetStatus(string status)
    {
        _lastStatus = status; RecordEvent(status);
        StatusChanged?.Invoke(this, status);
    }
    private void RecordEvent(string message) { lock (_recentEvents) { _recentEvents.Enqueue($"{DateTimeOffset.Now:HH:mm:ss} {message}"); while (_recentEvents.Count > 20) _recentEvents.Dequeue(); } }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); if (_monitorTask is not null) try { await _monitorTask; } catch (OperationCanceledException) { }
        foreach (var runtime in _controllers.Values) await runtime.DisposeAsync(); _controllers.Clear();
        foreach (string id in _hiddenControllers.ToArray()) await UnhideIfOwnedAsync(id, CancellationToken.None);
        _backend?.Dispose(); _updateClient.Dispose(); _mutation.Dispose(); _stop.Dispose();
    }
    private sealed class RuntimeEntry(ControllerBridge bridge, uint deviceId, string id, Action<string, Exception?> stopped) : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new(); private Task? _task; public uint DeviceId { get; } = deviceId;
        public void Start() => _task = RunAsync();
        private async Task RunAsync() { try { await bridge.RunAsync(_stop.Token); stopped(id, null); } catch (OperationCanceledException) when (_stop.IsCancellationRequested) { } catch (Exception ex) { stopped(id, ex); } }
        public async ValueTask DisposeAsync() { _stop.Cancel(); if (_task is not null) await _task; await bridge.DisposeAsync(); _stop.Dispose(); }
    }
}

internal sealed record ControllerRowModel(string StableId, string DisplayName, int RegistrationOrder, bool IsConnected, uint? VJoyDeviceId);
internal sealed record DiagnosticSnapshot(string Version, string BridgeStatus, int RegisteredControllers, int ConnectedControllers, int CompatibleVJoyDevices, IReadOnlyList<ControllerRowModel> Controllers, string HidingStatus, bool StartupEnabled, IReadOnlyList<string> RecentEvents)
{
    public string ToReport()
    {
        var lines = new List<string> { "AFGC PC Manager diagnostics", $"Version: {Version}", $"Bridge: {BridgeStatus}", $"Controllers: {ConnectedControllers} connected / {RegisteredControllers} registered", $"Compatible vJoy devices: {CompatibleVJoyDevices}", $"Physical hiding: {HidingStatus}", $"Start with Windows: {StartupEnabled}", "", "Controller assignments:" };
        lines.AddRange(Controllers.Select(x => $"- Controller {x.RegistrationOrder} [{x.StableId[..Math.Min(12, x.StableId.Length)]}]: {(x.IsConnected ? "connected" : "disconnected")}, output {(x.VJoyDeviceId?.ToString() ?? "none")}"));
        lines.Add(""); lines.Add("Recent events:"); lines.AddRange(RecentEvents.Select(x => $"- {x}")); return string.Join(Environment.NewLine, lines);
    }
}
