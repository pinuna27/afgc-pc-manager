using AFGCPCManager.Setup.Core.Installation;
using AFGCPCManager.Setup.Core.Models;

namespace AFGCPCManager.Setup.Core.Tests.Installation;

public sealed class JournalStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "afgc-journal-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RoundTripRestoresCaseInsensitiveDependencySets()
    {
        string path = Path.Combine(_root, "install-journal.json");
        var store = new JournalStore();
        await store.SaveAsync(path, new InstallationJournal
        {
            InstallDirectory = _root,
            Version = "1.0",
            DependenciesInstalledBySetup = ["VJoy"]
        }, TestContext.Current.CancellationToken);

        InstallationJournal loaded = (await store.LoadAsync(path, TestContext.Current.CancellationToken))!;

        Assert.Contains("vjoy", loaded.DependenciesInstalledBySetup);
    }

    [Fact]
    public async Task RejectsOwnershipPathOutsideInstallDirectory()
    {
        var store = new JournalStore();
        var invalid = new InstallationJournal
        {
            InstallDirectory = _root,
            Version = "1.0",
            Files = [new InstalledFile("..\\outside.exe", new string('A', 64))]
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(
            Path.Combine(_root, "install-journal.json"), invalid, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RejectsAbsoluteOwnershipPathEvenInsideInstallDirectory()
    {
        var store = new JournalStore();
        var invalid = new InstallationJournal
        {
            InstallDirectory = _root,
            Version = "1.0",
            Files = [new InstalledFile(Path.Combine(_root, "application.exe"), new string('A', 64))]
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(
            Path.Combine(_root, "install-journal.json"), invalid, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RejectsDuplicateOwnershipPathsAfterNormalization()
    {
        var store = new JournalStore();
        var invalid = new InstallationJournal
        {
            InstallDirectory = _root,
            Version = "1.0",
            Files =
            [
                new InstalledFile("application.exe", new string('A', 64)),
                new InstalledFile("folder\\..\\application.exe", new string('B', 64))
            ]
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(
            Path.Combine(_root, "install-journal.json"), invalid, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RejectsUnknownPendingDependency()
    {
        var invalid = new InstallationJournal
        {
            InstallDirectory = _root,
            Version = "1.0",
            PendingDependencyOperation = new("UnknownDriver", "1.0", "driver.exe",
                DependencyOperationPhase.Prepared, false)
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => new JournalStore().SaveAsync(
            Path.Combine(_root, "install-journal.json"), invalid, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RejectsUnknownDependencyOwnership()
    {
        var invalid = new InstallationJournal
        {
            InstallDirectory = _root,
            Version = "1.0",
            DependenciesInstalledBySetup = ["UnknownDriver"]
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => new JournalStore().SaveAsync(
            Path.Combine(_root, "install-journal.json"), invalid, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RejectsConflictingDependencyOwnership()
    {
        var invalid = new InstallationJournal
        {
            InstallDirectory = _root,
            Version = "1.0",
            DependenciesInstalledBySetup = ["VJoy"],
            DependenciesPresentBeforeSetup = ["vjoy"]
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => new JournalStore().SaveAsync(
            Path.Combine(_root, "install-journal.json"), invalid, TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
