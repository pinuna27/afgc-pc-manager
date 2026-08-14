using AFGCPCManager.Core.Devices;
using AFGCPCManager.Core.Input;
using AFGCPCManager.Core.Output;
using AFGCPCManager.Core.Settings;
using AFGCPCManager.HidHide;
using Xunit;

namespace AFGCPCManager.App.Tests;

public sealed class BridgeRuntimeCompositionTests
{
    [Fact]
    public async Task StartupRegistrationUsesTheExecutableWhenHidingIsDisabled()
    {
        var startup = new FakeStartupRegistration();
        SettingsDocument settings = new()
        {
            Application = new()
            {
                StartWithWindows = true,
                HidePhysicalControllers = false,
                AutomaticallyFindControllers = false,
                AutomaticallyCheckForUpdates = false
            }
        };
        var services = new BridgeRuntimeServices
        {
            Discovery = new EmptyDiscovery(),
            SettingsStore = new MemorySettingsStore(settings),
            ControllerHiding = new FakeControllerHidingService(),
            StartupRegistration = startup,
            OutputBackends = new(_ => new EmptyOutputBackend()),
            IdentificationLights = new((_, _) => true),
            SynchronizeVJoyDisplayName = _ => null,
            EnsureVJoyCapacityAsync = (_, _) => Task.CompletedTask,
            CreateControllerInput = _ => throw new InvalidOperationException(
                "No controller input should be created in this test."),
            ConsumerActions = new NullConsumerActions(),
            UpdateClient = new HttpClient(),
            UpdateSources = new InstalledComponentReleaseSourceProvider(() => null),
            TimeProvider = TimeProvider.System
        };

        await using var runtime = new BridgeRuntime(services);
        await runtime.StartAsync();

        Assert.True(startup.Enabled);
        Assert.Equal(Environment.ProcessPath, startup.ExecutablePath);
    }

    private sealed class EmptyDiscovery : IFireControllerDiscovery
    {
        public Task<IReadOnlyList<DiscoveredFireController>> SnapshotAsync(
            CancellationToken cancellationToken = default) => Task.FromResult<
                IReadOnlyList<DiscoveredFireController>>([]);
    }

    private sealed class MemorySettingsStore(SettingsDocument document) : ISettingsStore
    {
        public Task<SettingsDocument> LoadAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(document);

        public Task SaveAsync(
            SettingsDocument settings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeStartupRegistration : IStartupRegistration
    {
        public bool Enabled { get; private set; }
        public string? ExecutablePath { get; private set; }

        public bool IsEnabled() => Enabled;

        public void SetEnabled(bool enabled, string executablePath)
        {
            Enabled = enabled;
            ExecutablePath = executablePath;
        }
    }

    private sealed class EmptyOutputBackend : IGamepadOutputBackend
    {
        public IReadOnlyList<OutputDeviceInfo> EnumerateDevices() => [];
        public IGamepadOutputSession? TryAcquire(uint? preferredDeviceId = null) => null;
        public void Dispose() { }
    }

    private sealed class NullConsumerActions : IConsumerActionEmitter
    {
        public ValueTask EmitAsync(
            ConsumerAction action, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class FakeControllerHidingService : IControllerHidingService
    {
        public HidHideAvailability GetAvailability() => HidHideAvailability.NotInstalled;
        public Version? GetInstalledVersion() => null;
        public Task<HidHideOwnedState> PrepareOwnedEntriesAsync(
            string applicationPath, CancellationToken cancellationToken) =>
            Task.FromResult(new HidHideOwnedState([], [], []));
        public Task<HidHideVisibilityResult> HideAndVerifyAsync(
            string stableControllerId,
            IEnumerable<string> deviceInterfacePaths,
            string applicationPath,
            CancellationToken cancellationToken) => Task.FromResult(new HidHideVisibilityResult(
                HidHideVisibilityStatus.Hidden, "hidden"));
        public Task AcknowledgeHandleResetAsync(
            string stableControllerId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task MarkHandleResetDisconnectedAsync(
            string stableControllerId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task UnhideAsync(
            string stableControllerId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task RecoverOwnedEntriesAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
