using AFGCPCManager.App.Settings;
using AFGCPCManager.Core.Devices;
using AFGCPCManager.Core.Input;
using AFGCPCManager.Core.Output;
using AFGCPCManager.Core.Settings;
using AFGCPCManager.HidHide;
using AFGCPCManager.ViGEm;
using AFGCPCManager.VJoy;
using AFGCPCManager.Windows.Consumer;
using AFGCPCManager.Windows.Devices;
using AFGCPCManager.Windows.RawInput;
using AFGCPCManager.Windows.Startup;

namespace AFGCPCManager.App;

internal interface IControllerHidingService
{
    HidHideAvailability GetAvailability();
    Version? GetInstalledVersion();
    Task<HidHideOwnedState> PrepareOwnedEntriesAsync(
        string applicationPath, CancellationToken cancellationToken);
    Task<HidHideVisibilityResult> HideAndVerifyAsync(
        string stableControllerId,
        IEnumerable<string> deviceInterfacePaths,
        string applicationPath,
        CancellationToken cancellationToken);
    Task AcknowledgeHandleResetAsync(string stableControllerId, CancellationToken cancellationToken);
    Task MarkHandleResetDisconnectedAsync(string stableControllerId, CancellationToken cancellationToken);
    Task UnhideAsync(string stableControllerId, CancellationToken cancellationToken);
    Task RecoverOwnedEntriesAsync(CancellationToken cancellationToken);
}

internal interface IStartupRegistration
{
    bool IsEnabled();
    void SetEnabled(bool enabled, string executablePath);
}

internal sealed class BridgeRuntimeServices
{
    public required IFireControllerDiscovery Discovery { get; init; }
    public required ISettingsStore SettingsStore { get; init; }
    public required IControllerHidingService ControllerHiding { get; init; }
    public required IStartupRegistration StartupRegistration { get; init; }
    public required OutputBackendCache OutputBackends { get; init; }
    public required ControllerIdentificationLightManager IdentificationLights { get; init; }
    public required Func<IReadOnlyList<RegisteredController>, VJoyDisplayNameUpdate?>
        SynchronizeVJoyDisplayName
    { get; init; }
    public required Func<int, CancellationToken, Task> EnsureVJoyCapacityAsync { get; init; }
    public required Func<IEnumerable<string>, IRawControllerInput> CreateControllerInput { get; init; }
    public required IConsumerActionEmitter ConsumerActions { get; init; }
    public required HttpClient UpdateClient { get; init; }
    public required InstalledComponentReleaseSourceProvider UpdateSources { get; init; }
    public required TimeProvider TimeProvider { get; init; }

    public static BridgeRuntimeServices CreateDefault()
    {
        var hiding = new HidHideControllerHidingService(new HidHideService(
            new DeviceInstanceResolver(),
            new HidHideJournalStore(AppIdentity.HidHideJournalPath)));
        var provisioner = new VJoyDeviceProvisioner();

        return new()
        {
            Discovery = new FireControllerDiscovery(),
            SettingsStore = new JsonSettingsStore(AppIdentity.SettingsPath),
            ControllerHiding = hiding,
            StartupRegistration = new WindowsStartupRegistration(new WindowsStartupManager()),
            OutputBackends = new(mode => mode switch
            {
                GamepadOutputMode.DirectInput => new VJoyBackend(),
                GamepadOutputMode.XInput => new ViGEmBackend(),
                _ => throw new InvalidOperationException($"Unsupported output mode: {mode}.")
            }),
            IdentificationLights = new ControllerIdentificationLightManager(),
            SynchronizeVJoyDisplayName = new VJoyDirectInputNameManager().Synchronize,
            EnsureVJoyCapacityAsync = provisioner.EnsureCompatibleDeviceCountAsync,
            CreateControllerInput = paths => new DirectHidControllerInput(paths),
            ConsumerActions = new WindowsConsumerActionEmitter(),
            UpdateClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) },
            UpdateSources = new InstalledComponentReleaseSourceProvider(
                hiding.GetInstalledVersion),
            TimeProvider = TimeProvider.System
        };
    }
}

internal sealed class HidHideControllerHidingService(HidHideService service)
    : IControllerHidingService
{
    public HidHideAvailability GetAvailability() => service.GetAvailability();
    public Version? GetInstalledVersion() => service.GetInstalledVersion();
    public Task<HidHideOwnedState> PrepareOwnedEntriesAsync(
        string applicationPath, CancellationToken cancellationToken) =>
        service.PrepareOwnedEntriesAsync(applicationPath, cancellationToken);
    public Task<HidHideVisibilityResult> HideAndVerifyAsync(
        string stableControllerId,
        IEnumerable<string> deviceInterfacePaths,
        string applicationPath,
        CancellationToken cancellationToken) => service.HideAndVerifyAsync(
            stableControllerId, deviceInterfacePaths, applicationPath, cancellationToken);
    public Task AcknowledgeHandleResetAsync(
        string stableControllerId, CancellationToken cancellationToken) =>
        service.AcknowledgeHandleResetAsync(stableControllerId, cancellationToken);
    public Task MarkHandleResetDisconnectedAsync(
        string stableControllerId, CancellationToken cancellationToken) =>
        service.MarkHandleResetDisconnectedAsync(stableControllerId, cancellationToken);
    public Task UnhideAsync(string stableControllerId, CancellationToken cancellationToken) =>
        service.UnhideAsync(stableControllerId, cancellationToken);
    public async Task RecoverOwnedEntriesAsync(CancellationToken cancellationToken) =>
        _ = await service.RecoverOwnedEntriesAsync(cancellationToken);
}

internal sealed class WindowsStartupRegistration(WindowsStartupManager manager)
    : IStartupRegistration
{
    public bool IsEnabled() => manager.IsEnabled();
    public void SetEnabled(bool enabled, string executablePath) =>
        manager.SetEnabled(enabled, executablePath);
}
