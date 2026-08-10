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
        Assert.Equal(DependencyOperationPhase.RestartRequired, journal.PendingDependencyOperation!.Phase);
        Assert.Equal(1, installer.Calls);
        Assert.True(result.RestartRequired);
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
    public async Task TreatsNonstandardExitAsRestartWhenTargetVersionWasInstalled()
    {
        string journalPath = await CreateJournalAsync();
        var detector = new FakeDetector(new(DependencyId.HidHide, false));
        var installer = new FakeInstaller(
            () => detector.State = new(DependencyId.HidHide, true, new(1, 5, 230)),
            new(false, false, 1));
        var coordinator = new DependencyCoordinator(detector, installer, new JournalStore());

        DependencyExecutionResult result = await coordinator.EnsureAsync(journalPath,
            new Dictionary<DependencyId, (Version, string)> { [DependencyId.HidHide] = (new(1, 5, 230), "hidhide.exe") }, true,
            TestContext.Current.CancellationToken);

        InstallationJournal journal = (await new JournalStore().LoadAsync(journalPath, TestContext.Current.CancellationToken))!;
        Assert.True(result.RestartRequired);
        Assert.Contains("HidHide", journal.DependenciesInstalledBySetup);
        Assert.Equal(DependencyOperationPhase.RestartRequired, journal.PendingDependencyOperation!.Phase);
    }

    [Fact]
    public async Task FailedInstallerClearsStartedMarkerSoACleanRetryIsPossible()
    {
        string journalPath = await CreateJournalAsync();
        var detector = new FakeDetector(new(DependencyId.VJoy, false));
        var failed = new DependencyCoordinator(detector,
            new FakeInstaller(result: new(false, false, 1)), new JournalStore());

        await Assert.ThrowsAsync<InvalidOperationException>(() => failed.EnsureAsync(journalPath,
            new Dictionary<DependencyId, (Version, string)> { [DependencyId.VJoy] = (new(2, 2, 2), "vjoy.exe") }, true,
            TestContext.Current.CancellationToken));
        InstallationJournal afterFailure = (await new JournalStore().LoadAsync(
            journalPath, TestContext.Current.CancellationToken))!;
        Assert.Null(afterFailure.PendingDependencyOperation);

        var retryInstaller = new FakeInstaller(() => detector.State = new(DependencyId.VJoy, true, new(2, 2, 2)));
        DependencyExecutionResult retry = await new DependencyCoordinator(detector, retryInstaller, new JournalStore())
            .EnsureAsync(journalPath,
                new Dictionary<DependencyId, (Version, string)> { [DependencyId.VJoy] = (new(2, 2, 2), "vjoy.exe") }, true,
                TestContext.Current.CancellationToken);

        Assert.True(retry.RestartRequired);
        Assert.Equal(1, retryInstaller.Calls);
    }

    [Fact]
    public async Task ResumeDoesNotTreatOlderOperationalVersionAsCompletedUpdate()
    {
        string journalPath = Path.Combine(_root, "install-journal.json");
        await new JournalStore().SaveAsync(journalPath, new InstallationJournal
        {
            InstallDirectory = _root,
            Version = "1.0",
            DependenciesPresentBeforeSetup = ["VJoy"],
            PendingDependencyOperation = new("VJoy", "2.2.2", "vjoy.exe",
                DependencyOperationPhase.InstallerStarted, false)
        }, TestContext.Current.CancellationToken);
        var installer = new FakeInstaller();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new DependencyCoordinator(
                new FakeDetector(new(DependencyId.VJoy, true, new(2, 1, 9))), installer, new JournalStore())
            .EnsureAsync(journalPath,
                new Dictionary<DependencyId, (Version, string)> { [DependencyId.VJoy] = (new(2, 2, 2), "vjoy.exe") }, true,
                TestContext.Current.CancellationToken));

        InstallationJournal saved = (await new JournalStore().LoadAsync(journalPath, TestContext.Current.CancellationToken))!;
        Assert.NotNull(saved.PendingDependencyOperation);
        Assert.Contains("VJoy", saved.DependenciesPresentBeforeSetup);
        Assert.DoesNotContain("VJoy", saved.DependenciesInstalledBySetup);
        Assert.Equal(0, installer.Calls);
    }

    [Fact]
    public async Task NonstandardExitAcceptsEquivalentShortVersion()
    {
        string journalPath = await CreateJournalAsync();
        var detector = new FakeDetector(new(DependencyId.VJoy, false));
        var installer = new FakeInstaller(
            () => detector.State = new(DependencyId.VJoy, true, new Version(2, 2)),
            new(false, false, 1));

        DependencyExecutionResult result = await new DependencyCoordinator(detector, installer, new JournalStore())
            .EnsureAsync(journalPath,
                new Dictionary<DependencyId, (Version, string)> { [DependencyId.VJoy] = (new(2, 2, 0), "vjoy.exe") }, true,
                TestContext.Current.CancellationToken);

        Assert.True(result.RestartRequired);
    }

    [Fact]
    public async Task RefusesToDiscardPendingDriverMissingFromPackageSet()
    {
        string journalPath = Path.Combine(_root, "install-journal.json");
        await new JournalStore().SaveAsync(journalPath, new InstallationJournal
        {
            InstallDirectory = _root,
            Version = "1.0",
            PendingDependencyOperation = new("HidHide", "1.5.230", "hidhide.exe",
                DependencyOperationPhase.InstallerStarted, false)
        }, TestContext.Current.CancellationToken);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DependencyCoordinator(new ThrowingDetector(), new FakeInstaller(), new JournalStore())
                .EnsureAsync(journalPath,
                    new Dictionary<DependencyId, (Version, string)> { [DependencyId.VJoy] = (new(2, 2, 2), "vjoy.exe") }, true,
                    TestContext.Current.CancellationToken));

        Assert.Contains("not present", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TreatsSuccessfulButUndetectedDriverAsRestartRequired()
    {
        string journalPath = await CreateJournalAsync();
        var detector = new FakeDetector(new(DependencyId.HidHide, false));
        var coordinator = new DependencyCoordinator(detector, new FakeInstaller(result: new(true, false, 0)), new JournalStore());

        DependencyExecutionResult result = await coordinator.EnsureAsync(journalPath,
            new Dictionary<DependencyId, (Version, string)> { [DependencyId.HidHide] = (new(1, 5, 230), "hidhide.exe") }, true,
            TestContext.Current.CancellationToken);

        InstallationJournal journal = (await new JournalStore().LoadAsync(journalPath, TestContext.Current.CancellationToken))!;
        Assert.True(result.RestartRequired);
        Assert.Equal(DependencyOperationPhase.RestartRequired, journal.PendingDependencyOperation!.Phase);
    }

    [Fact]
    public async Task DoesNotRerunInstallerWhenDriverRemainsUndetectedAfterRestart()
    {
        string journalPath = Path.Combine(_root, "install-journal.json");
        await new JournalStore().SaveAsync(journalPath, new InstallationJournal
        {
            InstallDirectory = _root, Version = "1.0",
            PendingDependencyOperation = new("HidHide", "1.5.230", "hidhide.exe", DependencyOperationPhase.RestartRequired, true)
        }, TestContext.Current.CancellationToken);
        var detector = new FakeDetector(new(DependencyId.HidHide, false));
        var installer = new FakeInstaller(result: new(true, false, 0));
        var coordinator = new DependencyCoordinator(detector, installer, new JournalStore());

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.EnsureAsync(journalPath,
            new Dictionary<DependencyId, (Version, string)> { [DependencyId.HidHide] = (new(1, 5, 230), "hidhide.exe") }, true,
            TestContext.Current.CancellationToken));

        Assert.Contains("will not rerun", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, installer.Calls);
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

    [Theory]
    [InlineData(DependencyReadiness.Unknown, "reliably verify")]
    [InlineData(DependencyReadiness.Unhealthy, "not operational")]
    public async Task NeverLaunchesInstallerForAmbiguousExistingState(DependencyReadiness readiness, string expectedMessage)
    {
        string journalPath = await CreateJournalAsync();
        var detector = new FakeDetector(new(DependencyId.HidHide, readiness == DependencyReadiness.Unhealthy,
            Readiness: readiness, Evidence: [new("operational API", null)]));
        var installer = new FakeInstaller();
        var coordinator = new DependencyCoordinator(detector, installer, new JournalStore());

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.EnsureAsync(journalPath,
            new Dictionary<DependencyId, (Version, string)> { [DependencyId.HidHide] = (new(1, 5, 230), "") }, true,
            TestContext.Current.CancellationToken));

        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, installer.Calls);
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

    [Fact]
    public async Task DefersSingleRestartUntilAllMissingDependenciesAreInstalled()
    {
        string journalPath = await CreateJournalAsync();
        var detector = new MultiDetector();
        var installer = new RestartingInstaller(detector);
        DateTimeOffset boot = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
        var packages = new Dictionary<DependencyId, (Version, string)>
        {
            [DependencyId.VJoy] = (new(2, 2, 2), "vjoy.exe"),
            [DependencyId.HidHide] = (new(1, 5, 230), "hidhide.exe")
        };

        DependencyExecutionResult first = await new DependencyCoordinator(detector, installer, new JournalStore(), bootStartedAt: () => boot)
            .EnsureAsync(journalPath, packages, true, TestContext.Current.CancellationToken);
        InstallationJournal afterInstallers = (await new JournalStore().LoadAsync(journalPath, TestContext.Current.CancellationToken))!;
        Assert.True(first.RestartRequired);
        Assert.Equal("HidHide", afterInstallers.PendingDependencyOperation!.Dependency);
        Assert.True(detector.Installed[DependencyId.VJoy]);
        Assert.True(detector.Installed[DependencyId.HidHide]);
        Assert.Equal([DependencyId.VJoy, DependencyId.HidHide], installer.Calls);

        DependencyExecutionResult sameBoot = await new DependencyCoordinator(detector, installer, new JournalStore(), bootStartedAt: () => boot)
            .EnsureAsync(journalPath, packages, true, TestContext.Current.CancellationToken);
        Assert.True(sameBoot.RestartRequired);
        Assert.NotNull((await new JournalStore().LoadAsync(journalPath, TestContext.Current.CancellationToken))!.PendingDependencyOperation);

        boot = boot.AddMinutes(10);
        DependencyExecutionResult second = await new DependencyCoordinator(detector, installer, new JournalStore(), bootStartedAt: () => boot)
            .EnsureAsync(journalPath, packages, true, TestContext.Current.CancellationToken);
        InstallationJournal completed = (await new JournalStore().LoadAsync(journalPath, TestContext.Current.CancellationToken))!;
        Assert.False(second.RestartRequired);
        Assert.Null(completed.PendingDependencyOperation);
        Assert.Contains("VJoy", completed.DependenciesInstalledBySetup);
        Assert.Contains("HidHide", completed.DependenciesInstalledBySetup);
        Assert.Equal([DependencyId.VJoy, DependencyId.HidHide], installer.Calls);
    }

    [Fact]
    public async Task SameBootResumeDoesNotProbeAnyDriverInThePendingBatch()
    {
        DateTimeOffset boot = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
        string journalPath = Path.Combine(_root, "install-journal.json");
        await new JournalStore().SaveAsync(journalPath, new InstallationJournal
        {
            InstallDirectory = _root,
            Version = "1.0",
            PendingDependencyOperation = new("HidHide", "1.5.230", "hidhide.exe",
                DependencyOperationPhase.RestartRequired, true, boot)
        }, TestContext.Current.CancellationToken);
        var packages = new Dictionary<DependencyId, (Version, string)>
        {
            [DependencyId.VJoy] = (new(2, 2, 2), "vjoy.exe"),
            [DependencyId.HidHide] = (new(1, 5, 230), "hidhide.exe")
        };

        DependencyExecutionResult result = await new DependencyCoordinator(
                new ThrowingDetector(), new FakeInstaller(), new JournalStore(), bootStartedAt: () => boot)
            .EnsureAsync(journalPath, packages, true, TestContext.Current.CancellationToken);

        Assert.True(result.RestartRequired);
        Assert.Empty(result.Plans);
    }

    [Fact]
    public async Task StopsBatchWhenVendorInstallerInitiatesRestart()
    {
        string journalPath = await CreateJournalAsync();
        var detector = new MultiDetector();
        var installer = new InitiatingRestartInstaller(detector);
        DateTimeOffset boot = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
        var packages = new Dictionary<DependencyId, (Version, string)>
        {
            [DependencyId.VJoy] = (new(2, 2, 2), "vjoy.exe"),
            [DependencyId.HidHide] = (new(1, 5, 230), "hidhide.exe")
        };

        DependencyExecutionResult first = await new DependencyCoordinator(
                detector, installer, new JournalStore(), bootStartedAt: () => boot)
            .EnsureAsync(journalPath, packages, true, TestContext.Current.CancellationToken);

        Assert.True(first.RestartRequired);
        Assert.Equal([DependencyId.VJoy], installer.Calls);
        Assert.False(detector.Installed[DependencyId.HidHide]);
        InstallationJournal pending = (await new JournalStore().LoadAsync(
            journalPath, TestContext.Current.CancellationToken))!;
        Assert.Equal("VJoy", pending.PendingDependencyOperation!.Dependency);

        boot = boot.AddMinutes(10);
        DependencyExecutionResult resumed = await new DependencyCoordinator(
                detector, installer, new JournalStore(), bootStartedAt: () => boot)
            .EnsureAsync(journalPath, packages, true, TestContext.Current.CancellationToken);

        Assert.True(resumed.RestartRequired);
        Assert.Equal([DependencyId.VJoy, DependencyId.HidHide], installer.Calls);
    }

    [Theory]
    [InlineData(false, false, 2)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(true, true, 0)]
    public async Task HandlesEveryPreinstalledDependencyCombination(bool vJoyInstalled, bool hidHideInstalled, int expectedInstalls)
    {
        string journalPath = await CreateJournalAsync();
        var detector = new MultiDetector(vJoyInstalled, hidHideInstalled);
        var installer = new SuccessfulInstaller(detector);
        var packages = new Dictionary<DependencyId, (Version, string)>
        {
            [DependencyId.VJoy] = (new(2, 2, 2), "vjoy.exe"),
            [DependencyId.HidHide] = (new(1, 5, 230), "hidhide.exe")
        };

        DependencyExecutionResult result = await new DependencyCoordinator(detector, installer, new JournalStore())
            .EnsureAsync(journalPath, packages, true, TestContext.Current.CancellationToken);
        InstallationJournal completed = (await new JournalStore().LoadAsync(journalPath, TestContext.Current.CancellationToken))!;

        Assert.Equal(expectedInstalls > 0, result.RestartRequired);
        Assert.Equal(expectedInstalls > 0, completed.PendingDependencyOperation is not null);
        Assert.Equal(expectedInstalls, installer.Calls.Count);
        Assert.Equal(vJoyInstalled, completed.DependenciesPresentBeforeSetup.Contains("VJoy"));
        Assert.Equal(hidHideInstalled, completed.DependenciesPresentBeforeSetup.Contains("HidHide"));
        Assert.Equal(!vJoyInstalled, completed.DependenciesInstalledBySetup.Contains("VJoy"));
        Assert.Equal(!hidHideInstalled, completed.DependenciesInstalledBySetup.Contains("HidHide"));
    }

    private async Task<string> CreateJournalAsync()
    {
        string path = Path.Combine(_root, "install-journal.json");
        await new JournalStore().SaveAsync(path, new InstallationJournal { InstallDirectory = _root, Version = "1.0" }, TestContext.Current.CancellationToken);
        return path;
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class FakeDetector(DependencyState state) : IDependencyDetector { public DependencyState State { get; set; } = state; public DependencyState Detect(DependencyId dependency) => State; }
    private sealed class ThrowingDetector : IDependencyDetector
    {
        public DependencyState Detect(DependencyId dependency) =>
            throw new InvalidOperationException("Drivers must not be probed before the required restart.");
    }
    private sealed class FakeInstaller(Action? completed = null, DependencyInstallResult? result = null) : IDependencyInstaller
    {
        public int Calls { get; private set; }
        public Task<DependencyInstallResult> RunInteractiveAsync(string installerPath, CancellationToken cancellationToken = default) { Calls++; completed?.Invoke(); return Task.FromResult(result ?? new(true, false, 0)); }
    }
    private sealed class MultiDetector(bool vJoyInstalled = false, bool hidHideInstalled = false) : IDependencyDetector
    {
        public Dictionary<DependencyId, bool> Installed { get; } = new()
        {
            [DependencyId.VJoy] = vJoyInstalled,
            [DependencyId.HidHide] = hidHideInstalled
        };
        public DependencyState Detect(DependencyId dependency) => new(dependency, Installed[dependency], Installed[dependency]
            ? dependency == DependencyId.VJoy ? new(2, 2, 2) : new(1, 5, 230)
            : null);
    }
    private sealed class RestartingInstaller(MultiDetector detector) : IDependencyInstaller
    {
        public List<DependencyId> Calls { get; } = [];
        public Task<DependencyInstallResult> RunInteractiveAsync(string installerPath, CancellationToken cancellationToken = default)
        {
            DependencyId dependency = installerPath.Contains("vjoy", StringComparison.OrdinalIgnoreCase) ? DependencyId.VJoy : DependencyId.HidHide;
            Calls.Add(dependency); detector.Installed[dependency] = true;
            return Task.FromResult(new DependencyInstallResult(true, true, 3010));
        }
    }
    private sealed class SuccessfulInstaller(MultiDetector detector) : IDependencyInstaller
    {
        public List<DependencyId> Calls { get; } = [];
        public Task<DependencyInstallResult> RunInteractiveAsync(string installerPath, CancellationToken cancellationToken = default)
        {
            DependencyId dependency = installerPath.Contains("vjoy", StringComparison.OrdinalIgnoreCase) ? DependencyId.VJoy : DependencyId.HidHide;
            Calls.Add(dependency); detector.Installed[dependency] = true;
            return Task.FromResult(new DependencyInstallResult(true, false, 0));
        }
    }
    private sealed class InitiatingRestartInstaller(MultiDetector detector) : IDependencyInstaller
    {
        public List<DependencyId> Calls { get; } = [];
        public Task<DependencyInstallResult> RunInteractiveAsync(string installerPath,
            CancellationToken cancellationToken = default)
        {
            DependencyId dependency = installerPath.Contains("vjoy", StringComparison.OrdinalIgnoreCase)
                ? DependencyId.VJoy : DependencyId.HidHide;
            Calls.Add(dependency);
            detector.Installed[dependency] = true;
            return Task.FromResult(new DependencyInstallResult(true, true, 1641));
        }
    }
}
