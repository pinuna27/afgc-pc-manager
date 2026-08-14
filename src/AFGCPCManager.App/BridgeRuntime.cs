using AFGCPCManager.Core.Bridge;
using AFGCPCManager.Core.Devices;
using AFGCPCManager.Core.Input;
using AFGCPCManager.Core.Registration;
using AFGCPCManager.Core.Settings;
using AFGCPCManager.Core.Updates;
using AFGCPCManager.Core.Output;
using System.Diagnostics;
using AFGCPCManager.ViGEm;
using AFGCPCManager.HidHide;
using AFGCPCManager.Windows.Devices;

namespace AFGCPCManager.App;

internal sealed class BridgeRuntime : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _mutation = new(1, 1);
    private readonly object _lifetimeGate = new();
    private readonly Dictionary<string, ControllerRuntimeSession> _controllers = [];
    private readonly Dictionary<string, string> _controllerIssues = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hiddenControllers = new(StringComparer.Ordinal);
    private readonly ControllerReconnectGate _reconnectGate = new();
    private readonly ControllerIdentificationLightManager _identificationLights;
    private readonly Func<IReadOnlyList<RegisteredController>, VJoyDisplayNameUpdate?>
        _synchronizeVJoyDisplayName;
    private readonly OutputBackendCache _backends;
    private readonly IFireControllerDiscovery _discovery;
    private readonly ISettingsStore _settingsStore;
    private readonly IControllerHidingService _hidHide;
    private readonly ControllerOutputSafetyGate _outputSafetyGate;
    private readonly IStartupRegistration _startup;
    private readonly HttpClient _updateClient;
    private readonly InstalledComponentReleaseSourceProvider _updateSources;
    private readonly Func<int, CancellationToken, Task> _ensureVJoyCapacityAsync;
    private readonly Func<IEnumerable<string>, IRawControllerInput> _createControllerInput;
    private readonly IConsumerActionEmitter _consumerActions;
    private readonly TimeProvider _timeProvider;
    private ControllerRegistry? _registry;
    private IGamepadOutputBackend? _backend;
    private Task? _startTask;
    private Task? _runTask;
    private Task? _updateTask;
    private Task? _disposeTask;
    private GamepadOutputMode? _backendMode;
    private IReadOnlyList<DiscoveredFireController> _lastDiscovery = [];
    private IReadOnlyList<ControllerRowModel> _lastRows = [];
    private readonly Queue<string> _recentEvents = new();
    private string _lastStatus = "Starting";
    private int _compatibleOutputDevices;
    private int _lastProvisioningTarget;
    private int _failedProvisioningTarget = -1;
    private HidHideAvailability _hidingAvailability = HidHideAvailability.NotInstalled;
    private int ConnectedDiscoveryCount => _lastDiscovery.Count(x => x.IsConnected && x.Endpoints.Count > 0);

    public BridgeRuntime() : this(BridgeRuntimeServices.CreateDefault())
    {
    }

    internal BridgeRuntime(BridgeRuntimeServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _discovery = services.Discovery;
        _settingsStore = services.SettingsStore;
        _hidHide = services.ControllerHiding;
        _startup = services.StartupRegistration;
        _backends = services.OutputBackends;
        _identificationLights = services.IdentificationLights;
        _synchronizeVJoyDisplayName = services.SynchronizeVJoyDisplayName;
        _ensureVJoyCapacityAsync = services.EnsureVJoyCapacityAsync;
        _createControllerInput = services.CreateControllerInput;
        _consumerActions = services.ConsumerActions;
        _updateClient = services.UpdateClient;
        _updateSources = services.UpdateSources;
        _timeProvider = services.TimeProvider;
        _outputSafetyGate = new(
            (id, paths, app, cancellationToken) =>
                _hidHide.HideAndVerifyAsync(id, paths, app, cancellationToken),
            (id, cancellationToken) => _hidHide.UnhideAsync(id, cancellationToken),
            id => _hiddenControllers.Add(id));
    }

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<IReadOnlyList<ControllerRowModel>>? ControllersChanged;
    public event EventHandler<IReadOnlyList<UpdateCheckResult.Available>>? UpdatesAvailable;

    public Task StartAsync()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            return _startTask ??= StartCoreAsync();
        }
    }

    private async Task StartCoreAsync()
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
                string processPath = settings.Application.StartWithWindows
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
            if (settings.Application.AutomaticallyCheckForUpdates)
                _updateTask = CheckUpdatesAsync();
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
        GamepadOutputMode mode = _registry!.Snapshot.Application.OutputMode;
        (IGamepadOutputBackend Backend, int Compatible) initialized = await Task.Run(() =>
        {
            IGamepadOutputBackend backend = _backends.GetOrCreate(mode);
            try { return (backend, backend.EnumerateDevices().Count(x => x.Capabilities is not null)); }
            catch { _backends.Remove(mode, backend); throw; }
        }, cancellationToken);

        if (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        await _mutation.WaitAsync(cancellationToken);
        try
        {
            if (_backend is not null || _registry!.Snapshot.Application.OutputMode != mode)
            {
                return;
            }

            _backend = initialized.Backend;
            _backendMode = mode;
            _compatibleOutputDevices = initialized.Compatible;
            // vJoy remains a DirectInput device even while Xbox output is selected.
            // Keep its shared Windows name migrated away from the legacy suffix.
            SynchronizeVJoyDisplayName(_registry.Snapshot);
            try { _hidingAvailability = _hidHide.GetAvailability(); }
            catch { _hidingAvailability = HidHideAvailability.NotOperational; }
            PublishControllers();
        }
        finally { _mutation.Release(); }
    }

    private async Task RefreshDiscoveryAsync(CancellationToken cancellationToken)
    {
        _lastDiscovery = await _discovery.SnapshotAsync(cancellationToken);
        SettingsDocument before = _registry!.Snapshot;
        bool changed = ControllerRegistrationReconciler.Reconcile(
            _registry, _lastDiscovery, _timeProvider.GetUtcNow());
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
            GamepadOutputMode outputMode = settings.Application.OutputMode;
            if (_backendMode != outputMode)
            {
                await DisposeAllControllersAsync();
                _backend = null;
                _backendMode = null;
                _compatibleOutputDevices = 0;
                PublishControllers();
                return;
            }
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
                ControllerRuntimeSession runtime = _controllers[id];
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
            if (requiredOutputs != _lastProvisioningTarget)
            {
                _lastProvisioningTarget = requiredOutputs;
                _failedProvisioningTarget = -1;
            }
            IReadOnlyList<OutputDeviceInfo> outputs = _backend!.EnumerateDevices();
            TrackHandleGrowth($"{OutputModeName(outputMode)} enumeration", ref previousHandleCount);
            _compatibleOutputDevices = outputs.Count(x => x.Capabilities is not null
                && x.Status is OutputDeviceStatus.Free or OutputDeviceStatus.Owned);
            if (outputMode == GamepadOutputMode.DirectInput
                && requiredOutputs > _compatibleOutputDevices
                && requiredOutputs != _failedProvisioningTarget)
            {
                try
                {
                    await _ensureVJoyCapacityAsync(requiredOutputs, cancellationToken);
                    _compatibleOutputDevices = _backend.EnumerateDevices().Count(x => x.Capabilities is not null);
                    RecordEvent($"Expanded vJoy capacity to {_compatibleOutputDevices} compatible device(s).");
                }
                catch (Exception ex)
                {
                    _failedProvisioningTarget = requiredOutputs;
                    setupRequired = true;
                    RecordEvent($"Could not provision {requiredOutputs} vJoy outputs: {ex.Message}");
                }
            }
            var connectedRegistrations = settings.Controllers
                .OrderBy(registration => registration.RegistrationOrder)
                .Select(registration => (Registration: registration, Device: _lastDiscovery.FirstOrDefault(
                    device => device.IsConnected && device.Endpoints.Count > 0
                        && device.Identity.StableId == registration.StableId)))
                .Where(item => item.Device is not null)
                .ToArray();
            if (outputMode == GamepadOutputMode.XInput)
            {
                foreach (string overflowId in connectedRegistrations
                             .Skip((int)ViGEmBackend.MaximumDeviceId)
                             .Select(item => item.Registration.StableId))
                {
                    if (!_controllers.Remove(overflowId,
                            out ControllerRuntimeSession? overflowRuntime))
                        continue;
                    try { await overflowRuntime.DisposeAsync(); }
                    catch (Exception ex)
                    {
                        RecordEvent($"Could not release Xbox output {overflowRuntime.DeviceId}: {ex.Message}");
                    }
                }
            }
            for (int connectedIndex = 0; connectedIndex < connectedRegistrations.Length; connectedIndex++)
            {
                (RegisteredController registration, DiscoveredFireController? discovered) = connectedRegistrations[connectedIndex];
                DiscoveredFireController device = discovered!;
                if (outputMode == GamepadOutputMode.XInput
                    && connectedIndex >= (int)ViGEmBackend.MaximumDeviceId)
                {
                    setupRequired = true;
                    SetControllerIssue(device.Identity.StableId,
                        "Xbox (XInput) supports up to 4 controllers. Switch to vJoy (DirectInput) for more.");
                    continue;
                }
                if (_controllers.ContainsKey(device.Identity.StableId)) continue;
                if (_reconnectGate.IsPending(device.Identity.StableId))
                {
                    setupRequired = true;
                    SetControllerIssue(device.Identity.StableId,
                        "Reconnect required: turn this controller off and back on once. Virtual output is disabled until then.");
                    continue;
                }
                uint? preferredOutput = outputMode == GamepadOutputMode.XInput
                    ? registration.PreferredXInputSlot
                    : registration.PreferredVJoyId;
                string processPath = Environment.ProcessPath
                    ?? throw new InvalidOperationException(
                        "The application executable path is unavailable.");
                var output = _backend.TryAcquire(preferredOutput);
                TrackHandleGrowth($"{OutputModeName(outputMode)} acquisition", ref previousHandleCount);
                if (output is null)
                {
                    setupRequired = true;
                    SetControllerIssue(device.Identity.StableId,
                        outputMode == GamepadOutputMode.XInput
                            ? "No free Xbox (XInput) slot is available."
                            : "No compatible, free vJoy output is available.");
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
                IRawControllerInput? input = null;
                ControllerBridge bridge;
                try
                {
                    input = _createControllerInput(inputPaths);
                    bridge = new ControllerBridge(input, output, _consumerActions,
                        _registry.GetEffectiveMapping(device.Identity.StableId));
                    input = null; // ControllerBridge now owns the input subscription.
                }
                catch
                {
                    if (input is not null)
                    {
                        try { await input.DisposeAsync().ConfigureAwait(false); }
                        catch (Exception ex)
                        {
                            RecordEvent($"Could not release controller input after initialization failed: {ex.Message}");
                        }
                    }
                    await UnhideIfOwnedAsync(device.Identity.StableId, CancellationToken.None);
                    output.Dispose();
                    throw;
                }
                if (preferredOutput != output.DeviceId)
                {
                    SettingsDocument beforeAssignment = _registry.Snapshot;
                    if (outputMode == GamepadOutputMode.XInput)
                        _registry.SetPreferredXInputSlot(device.Identity.StableId, output.DeviceId);
                    else
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
                var runtime = new ControllerRuntimeSession(
                    bridge, output.DeviceId, device.Identity.ToRedactedString(), OnRuntimeStopped);
                _controllers.Add(device.Identity.StableId, runtime);
                runtime.Start();
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
            _compatibleOutputDevices, _lastRows, _hidingAvailability.ToString(), startupEnabled, events,
            GetSettings().Application.OutputMode);
    }
    public IReadOnlyList<DiscoveredFireController> GetAddCandidates() => _lastDiscovery.Where(x => x.IsConnected && x.Endpoints.Count > 0 && !GetSettings().Controllers.Any(c => c.StableId == x.Identity.StableId)).ToArray();
    private async Task CheckUpdatesAsync()
    {
        try
        {
            Version applicationVersion = typeof(Program).Assembly.GetName().Version
                ?? new Version(0, 0);
            IReadOnlyList<ReleaseSource> sources = _updateSources.GetSources(applicationVersion);
            var checker = new GitHubReleaseChecker(_updateClient);
            UpdateCheckResult[] results = await Task.WhenAll(
                sources.Select(source => checker.CheckAsync(source, _stop.Token)));
            var available = results.OfType<UpdateCheckResult.Available>().ToArray();
            foreach (UpdateCheckResult.Failed failure in results.OfType<UpdateCheckResult.Failed>())
                RecordEvent($"Update check failed for {failure.Component}: {failure.Message}");
            if (available.Length > 0)
            {
                RecordEvent($"{available.Length} stable update(s) available.");
                UpdatesAvailable?.Invoke(this, available);
            }
            else
            {
                RecordEvent("Stable release update check completed; everything is current.");
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) { RecordEvent($"Update check failed: {ex.Message}"); }
    }
    public async Task AddControllerAsync(string id)
    {
        (Task waitTask, CancellationToken cancellationToken) = BeginOperation();
        await waitTask.ConfigureAwait(false);
        try
        {
            var device = _lastDiscovery.FirstOrDefault(x => x.IsConnected && x.Endpoints.Count > 0 && x.Identity.StableId == id)
                ?? throw new InvalidOperationException("That controller is no longer connected.");
            SettingsDocument before = _registry!.Snapshot;
            _registry.Register(device.Identity, _timeProvider.GetUtcNow());
            try { await _settingsStore.SaveAsync(_registry.Snapshot, cancellationToken).ConfigureAwait(false); }
            catch { _registry = new(before); throw; }
            PublishControllers();
        }
        finally { _mutation.Release(); }
    }
    public async Task RemoveControllerAsync(string id)
    {
        (Task waitTask, CancellationToken cancellationToken) = BeginOperation();
        await waitTask.ConfigureAwait(false);
        try
        {
            SettingsDocument before = _registry!.Snapshot;
            if (_controllers.Remove(id, out var runtime))
            {
                try { await runtime.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { RecordEvent($"Could not release controller output {runtime.DeviceId}: {ex.Message}"); }
            }
            await UnhideIfOwnedAsync(id, cancellationToken).ConfigureAwait(false);
            _reconnectGate.Forget(id);
            _identificationLights.Forget(id);
            if (_registry.RemoveAndExclude(id))
            {
                try { await _settingsStore.SaveAsync(_registry.Snapshot, cancellationToken).ConfigureAwait(false); }
                catch { _registry = new(before); throw; }
            }
            PublishControllers();
        }
        finally { _mutation.Release(); }
    }
    public async Task SaveSettingsAsync(SettingsDocument settings)
    {
        (Task waitTask, CancellationToken cancellationToken) = BeginOperation();
        await waitTask.ConfigureAwait(false);
        try
        {
            SettingsDocument before = _registry?.Snapshot ?? new();
            string processPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("The application executable path is unavailable.");
            _startup.SetEnabled(settings.Application.StartWithWindows, processPath);
            try { await _settingsStore.SaveAsync(settings, cancellationToken).ConfigureAwait(false); }
            catch
            {
                try { _startup.SetEnabled(before.Application.StartWithWindows, processPath); } catch { }
                throw;
            }

            _registry = new(settings);
            bool outputModeChanged = before.Application.OutputMode !=
                settings.Application.OutputMode;
            bool bridgeRestartRequired = RequiresBridgeRestart(before, settings);

            _identificationLights.Reconcile(
                settings.Application.ControlIdentificationLights,
                _lastDiscovery, settings.Controllers, RecordEvent);

            // Preferences such as identification lights, startup, updates, and
            // notifications do not invalidate a running virtual controller. In
            // particular, avoid disconnecting and recreating a ViGEm target for
            // a light-only save: the row would falsely claim that ViGEmBus was
            // missing while the replacement target was still being acquired.
            if (bridgeRestartRequired)
                await DisposeAllControllersAsync().ConfigureAwait(false);
            if (outputModeChanged)
            {
                _backend = null;
                _backendMode = null;
                _compatibleOutputDevices = 0;
                _lastProvisioningTarget = 0;
                _failedProvisioningTarget = -1;
                _controllerIssues.Clear();
                RecordEvent($"Virtual controller output changed to {OutputModeName(settings.Application.OutputMode)}.");
            }
            if (before.Application.HidePhysicalControllers && !settings.Application.HidePhysicalControllers)
            {
                await _hidHide.RecoverOwnedEntriesAsync(cancellationToken).ConfigureAwait(false);
                _hiddenControllers.Clear();
                _reconnectGate.Clear();
            }
            PublishControllers();
        }
        finally { _mutation.Release(); }
    }

    internal static bool RequiresBridgeRestart(SettingsDocument before,
        SettingsDocument after) =>
        RuntimeSettingsChangePolicy.RequiresBridgeRestart(before, after);

    private async Task UnhideIfOwnedAsync(string id, CancellationToken cancellationToken)
    {
        if (!_hiddenControllers.Contains(id)) return;
        try
        {
            await _hidHide.UnhideAsync(id, cancellationToken).ConfigureAwait(false);
            _hiddenControllers.Remove(id);
        }
        catch (Exception ex) { RecordEvent($"Could not restore physical controller visibility: {ex.Message}"); }
    }

    private async Task DisposeAllControllersAsync()
    {
        foreach ((string id, ControllerRuntimeSession runtime) in _controllers.ToArray())
        {
            _controllers.Remove(id);
            try { await runtime.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { RecordEvent($"Could not release controller output {runtime.DeviceId}: {ex.Message}"); }
        }
    }

    private void PublishControllers()
    {
        if (_registry is null)
            return;
        HashSet<string> present = _lastDiscovery
            .Where(controller => controller.IsConnected && controller.Endpoints.Count > 0)
            .Select(controller => controller.Identity.StableId)
            .ToHashSet(StringComparer.Ordinal);
        SettingsDocument settings = _registry.Snapshot;
        ControllerRowModel[] rows = settings.Controllers
            .OrderBy(controller => controller.RegistrationOrder)
            .Select(controller => new ControllerRowModel(
                controller.StableId,
                controller.DisplayName,
                controller.RegistrationOrder,
                present.Contains(controller.StableId),
                _controllers.TryGetValue(controller.StableId,
                    out ControllerRuntimeSession? runtime) ? runtime.DeviceId : null,
                _controllerIssues.GetValueOrDefault(controller.StableId),
                settings.Application.ControlIdentificationLights
                    ? ControllerIdentificationLightPattern.ForRegistrationOrder(
                        controller.RegistrationOrder)
                    : null,
                settings.Application.OutputMode))
            .ToArray();
        _lastRows = rows;
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
            VJoyDisplayNameUpdate? update =
                _synchronizeVJoyDisplayName(settings.Controllers);
            if (update?.Changed == true)
                RecordEvent($"Renamed the shared vJoy DirectInput device to '{update.Name}'.");
        }
        catch (Exception ex)
        {
            RecordEvent($"The vJoy DirectInput display name could not be updated: {ex.Message}");
        }
    }
    private static string OutputModeName(GamepadOutputMode mode) => mode switch
    {
        GamepadOutputMode.XInput => "Xbox (XInput)",
        GamepadOutputMode.DirectInput => "vJoy (DirectInput)",
        _ => mode.ToString()
    };
    public ValueTask DisposeAsync()
    {
        lock (_lifetimeGate)
            return new(_disposeTask ??= DisposeCoreAsync());
    }

    private (Task WaitTask, CancellationToken Token) BeginOperation()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            CancellationToken token = _stop.Token;
            return (_mutation.WaitAsync(token), token);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        Task? startTask = _startTask;
        if (startTask is not null)
            await IgnoreExpectedCancellationAsync(startTask).ConfigureAwait(false);
        Task? runTask = _runTask;
        if (runTask is not null)
            await IgnoreExpectedCancellationAsync(runTask).ConfigureAwait(false);
        Task? updateTask = _updateTask;
        if (updateTask is not null)
            await IgnoreExpectedCancellationAsync(updateTask).ConfigureAwait(false);

        List<Exception> failures = [];
        await _mutation.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await DisposeAllControllersAsync().ConfigureAwait(false);
            foreach (string id in _hiddenControllers.ToArray())
                await UnhideIfOwnedAsync(id, CancellationToken.None).ConfigureAwait(false);
            _backend = null;
            try { _backends.Dispose(); }
            catch (Exception ex) { failures.Add(ex); }
            try { _updateClient.Dispose(); }
            catch (Exception ex) { failures.Add(ex); }
        }
        finally { _mutation.Release(); }
        _mutation.Dispose();
        _stop.Dispose();
        if (failures.Count > 0)
            throw new AggregateException("Runtime cleanup failed.", failures);
    }

    private async Task IgnoreExpectedCancellationAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

}
