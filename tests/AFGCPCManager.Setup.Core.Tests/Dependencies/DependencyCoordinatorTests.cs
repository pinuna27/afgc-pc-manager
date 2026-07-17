using AFGCPCManager.Setup.Core.Dependencies;
using AFGCPCManager.Setup.Core.Installation;
using AFGCPCManager.Setup.Core.Models;

namespace AFGCPCManager.Setup.Core.Tests.Dependencies;

public sealed class DependencyCoordinatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "afgc-dependency-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InstallsMissingDependencyAndRecordsOwnership()
    {
        string journalPath = await CreateJournalAsync();
        var detector = new FakeDetector(new(DependencyId.VJoy, false));
        var installer = new FakeInstaller(() => detector.State = new(DependencyId.VJoy, true, new(2, 2, 2)));
        var coordinator = new DependencyCoordinator(detector, installer, new JournalStore());

        DependencyExecutionResult result = await coordinator.EnsureAsync(journalPath,
            new Dictionary<DependencyId, (Version, string)> { [DependencyId.VJoy] = (new(2, 2, 2), "vjoy.exe") }, true,
            TestContext.Current.CancellationToken);

        InstallationJournal journal = (await new JournalStore().LoadAsync(journalPath, TestContext.Current.CancellationToken))!;
        Assert.Contains("VJoy", journal.DependenciesInstalledBySetup);
        Assert.Null(journal.PendingDependencyOperation);
        Assert.Equal(1, installer.Calls);
        Assert.False(result.RestartRequired);
    }

    [Fact]
    public async Task PreservesPreexistingDependencyWithoutLaunchingInstaller()
    {
        string journalPath = await CreateJournalAsync();
        var detector = new FakeDetector(new(DependencyId.HidHide, true, new(1, 5)));
        var installer = new FakeInstaller();
        var coordinator = new DependencyCoordinator(detector, installer, new JournalStore());
        await coordinator.EnsureAsync(journalPath,
            new Dictionary<DependencyId, (Version, string)> { [DependencyId.HidHide] = (new(1, 5), "hidhide.exe") }, true,
            TestContext.Current.CancellationToken);
        InstallationJournal journal = (await new JournalStore().LoadAsync(journalPath, TestContext.Current.CancellationToken))!;
        Assert.Contains("HidHide", journal.DependenciesPresentBeforeSetup);
        Assert.Equal(0, installer.Calls);
    }

    [Fact]
    public async Task PersistsRestartRequiredOperation()
    {
        string journalPath = await CreateJournalAsync();
        var detector = new FakeDetector(new(DependencyId.VJoy, false));
        var installer = new FakeInstaller(result: new(true, true, 3010));
        var coordinator = new DependencyCoordinator(detector, installer, new JournalStore());
        DependencyExecutionResult result = await coordinator.EnsureAsync(journalPath,
            new Dictionary<DependencyId, (Version, string)> { [DependencyId.VJoy] = (new(2, 2, 2), "vjoy.exe") }, true,
            TestContext.Current.CancellationToken);
        InstallationJournal journal = (await new JournalStore().LoadAsync(journalPath, TestContext.Current.CancellationToken))!;
        Assert.True(result.RestartRequired);
        Assert.Equal(DependencyOperationPhase.RestartRequired, journal.PendingDependencyOperation!.Phase);
    }

    [Fact]
    public async Task ResumeClearsRestartOperationWhenDependencyIsDetected()
    {
        string journalPath = Path.Combine(_root, "install-journal.json");
        var journal = new InstallationJournal
        {
            InstallDirectory = _root, Version = "1.0",
            DependenciesInstalledBySetup = ["VJoy"],
            PendingDependencyOperation = new("VJoy", "2.2.2", "vjoy.exe", DependencyOperationPhase.RestartRequired, true)
        };
        await new JournalStore().SaveAsync(journalPath, journal, TestContext.Current.CancellationToken);
        var coordinator = new DependencyCoordinator(new FakeDetector(new(DependencyId.VJoy, true, new(2, 2, 2))), new FakeInstaller(), new JournalStore());
        await coordinator.EnsureAsync(journalPath,
            new Dictionary<DependencyId, (Version, string)> { [DependencyId.VJoy] = (new(2, 2, 2), "vjoy.exe") }, true,
            TestContext.Current.CancellationToken);
        InstallationJournal saved = (await new JournalStore().LoadAsync(journalPath, TestContext.Current.CancellationToken))!;
        Assert.Null(saved.PendingDependencyOperation);
    }

    [Fact]
    public async Task ResumeReconcilesOwnershipWhenRebootInterruptedInstallerReturn()
    {
        string journalPath = Path.Combine(_root, "install-journal.json");
        var journal = new InstallationJournal
        {
            InstallDirectory = _root, Version = "1.0",
            PendingDependencyOperation = new("HidHide", "1.5.230", "hidhide.exe", DependencyOperationPhase.InstallerStarted, false)
        };
        await new JournalStore().SaveAsync(journalPath, journal, TestContext.Current.CancellationToken);
        var coordinator = new DependencyCoordinator(new FakeDetector(new(DependencyId.HidHide, true, new(1, 5, 230))), new FakeInstaller(), new JournalStore());
        await coordinator.EnsureAsync(journalPath,
            new Dictionary<DependencyId, (Version, string)> { [DependencyId.HidHide] = (new(1, 5, 230), "hidhide.exe") }, true,
            TestContext.Current.CancellationToken);
        InstallationJournal saved = (await new JournalStore().LoadAsync(journalPath, TestContext.Current.CancellationToken))!;
        Assert.Contains("HidHide", saved.DependenciesInstalledBySetup);
        Assert.DoesNotContain("HidHide", saved.DependenciesPresentBeforeSetup);
        Assert.Null(saved.PendingDependencyOperation);
    }

    private async Task<string> CreateJournalAsync()
    {
        string path = Path.Combine(_root, "install-journal.json");
        await new JournalStore().SaveAsync(path, new InstallationJournal { InstallDirectory = _root, Version = "1.0" }, TestContext.Current.CancellationToken);
        return path;
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class FakeDetector(DependencyState state) : IDependencyDetector { public DependencyState State { get; set; } = state; public DependencyState Detect(DependencyId dependency) => State; }
    private sealed class FakeInstaller(Action? completed = null, DependencyInstallResult? result = null) : IDependencyInstaller
    {
        public int Calls { get; private set; }
        public Task<DependencyInstallResult> RunInteractiveAsync(string installerPath, CancellationToken cancellationToken = default) { Calls++; completed?.Invoke(); return Task.FromResult(result ?? new(true, false, 0)); }
    }
}
