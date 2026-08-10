using AFGCPCManager.Setup.Core.Dependencies;
using AFGCPCManager.Setup.Core.Installation;
using AFGCPCManager.Setup.Core.Models;

namespace AFGCPCManager.Setup.Core.Tests.Installation;

public sealed class LifecycleIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "afgc-full-lifecycle-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FreshInstallDriverRestartUpdateAndUninstallPreserveAllLifecycleInvariants()
    {
        string payload = Path.Combine(_root, "payload"), install = Path.Combine(_root, "app");
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, "application.bin"), "version one");
        File.WriteAllText(Path.Combine(payload, "retired.bin"), "retired");
        var journalStore = new JournalStore();
        var applicationInstaller = new ApplicationInstaller(journalStore);
        InstallationJournal initial = await applicationInstaller.InstallAsync(
            payload, install, new Version(1, 0), TestContext.Current.CancellationToken);
        Assert.Equal(2, initial.Files.Count);

        DateTimeOffset boot = new(2026, 8, 7, 10, 0, 0, TimeSpan.Zero);
        var detector = new LifecycleDetector();
        var driverInstaller = new LifecycleDriverInstaller(detector);
        var packages = new Dictionary<DependencyId, (Version, string)>
        {
            [DependencyId.VJoy] = (new(2, 2, 2), "vjoy.exe"),
            [DependencyId.HidHide] = (new(1, 5, 230), "hidhide.exe")
        };
        string journalPath = Path.Combine(install, "install-journal.json");
        DependencyExecutionResult driverInstall = await new DependencyCoordinator(
                detector, driverInstaller, journalStore, bootStartedAt: () => boot)
            .EnsureAsync(journalPath, packages, true, TestContext.Current.CancellationToken);
        Assert.True(driverInstall.RestartRequired);
        Assert.Equal(2, driverInstaller.Calls);

        DependencyExecutionResult sameBoot = await new DependencyCoordinator(
                new ThrowingDetector(), driverInstaller, journalStore, bootStartedAt: () => boot)
            .EnsureAsync(journalPath, packages, true, TestContext.Current.CancellationToken);
        Assert.True(sameBoot.RestartRequired);
        Assert.Equal(2, driverInstaller.Calls);

        boot = boot.AddMinutes(10);
        DependencyExecutionResult resumed = await new DependencyCoordinator(
                detector, driverInstaller, journalStore, bootStartedAt: () => boot)
            .EnsureAsync(journalPath, packages, true, TestContext.Current.CancellationToken);
        Assert.False(resumed.RestartRequired);
        InstallationJournal afterRestart = (await journalStore.LoadAsync(
            journalPath, TestContext.Current.CancellationToken))!;
        Assert.Null(afterRestart.PendingDependencyOperation);
        Assert.Contains("VJoy", afterRestart.DependenciesInstalledBySetup);
        Assert.Contains("HidHide", afterRestart.DependenciesInstalledBySetup);

        File.WriteAllText(Path.Combine(install, "user-notes.txt"), "preserve");
        File.WriteAllText(Path.Combine(payload, "application.bin"), "version two");
        File.Delete(Path.Combine(payload, "retired.bin"));
        InstallationJournal updated = await applicationInstaller.InstallAsync(
            payload, install, new Version(2, 0), TestContext.Current.CancellationToken);
        Assert.Equal("version two", File.ReadAllText(Path.Combine(install, "application.bin")));
        Assert.False(File.Exists(Path.Combine(install, "retired.bin")));
        Assert.Equal("preserve", File.ReadAllText(Path.Combine(install, "user-notes.txt")));
        Assert.True(DependencyUninstallOptions.FromJournal(updated).UninstallVJoy);
        Assert.True(DependencyUninstallOptions.FromJournal(updated).UninstallHidHide);

        UninstallResult uninstall = await new ApplicationUninstaller(journalStore)
            .UninstallOwnedFilesAsync(journalPath, TestContext.Current.CancellationToken);
        Assert.Equal(1, uninstall.RemovedFiles);
        Assert.Equal(["user-notes.txt"], uninstall.PreservedFiles);
        Assert.False(File.Exists(Path.Combine(install, "application.bin")));
        Assert.True(File.Exists(Path.Combine(install, "user-notes.txt")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class LifecycleDetector : IDependencyDetector
    {
        public HashSet<DependencyId> Installed { get; } = [];
        public DependencyState Detect(DependencyId dependency) => Installed.Contains(dependency)
            ? new(dependency, true, dependency == DependencyId.VJoy ? new(2, 2, 2) : new(1, 5, 230))
            : new(dependency, false);
    }

    private sealed class LifecycleDriverInstaller(LifecycleDetector detector) : IDependencyInstaller
    {
        public int Calls { get; private set; }
        public Task<DependencyInstallResult> RunInteractiveAsync(string installerPath,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            detector.Installed.Add(installerPath.Contains("vjoy", StringComparison.OrdinalIgnoreCase)
                ? DependencyId.VJoy : DependencyId.HidHide);
            return Task.FromResult(new DependencyInstallResult(true, true, 3010));
        }
    }

    private sealed class ThrowingDetector : IDependencyDetector
    {
        public DependencyState Detect(DependencyId dependency) =>
            throw new InvalidOperationException("No driver probing is allowed before restart.");
    }
}
