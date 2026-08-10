using AFGCPCManager.Setup.Core.Installation;
using AFGCPCManager.Setup.Core.Models;
using System.Text.Json;

namespace AFGCPCManager.Setup.Core.Tests.Installation;

public sealed class ApplicationInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "afgc-setup-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InstallsPayloadAndCreatesJournal()
    {
        string payload = Path.Combine(_root, "payload"), target = Path.Combine(_root, "app");
        Directory.CreateDirectory(Path.Combine(payload, "sub")); File.WriteAllText(Path.Combine(payload, "sub", "app.bin"), "release");
        var journal = await new ApplicationInstaller(new JournalStore()).InstallAsync(payload, target, new Version(1, 2, 3), TestContext.Current.CancellationToken);
        Assert.Equal("release", File.ReadAllText(Path.Combine(target, "sub", "app.bin")));
        Assert.Single(journal.Files); Assert.True(File.Exists(Path.Combine(target, "install-journal.json")));
    }

    [Fact]
    public async Task UpdatePreservesDependencyOwnershipAndRemovesBackup()
    {
        string payload = Path.Combine(_root, "payload"), target = Path.Combine(_root, "app");
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, "app.bin"), "first");
        var store = new JournalStore();
        InstallationJournal first = await new ApplicationInstaller(store).InstallAsync(payload, target, new Version(1, 0), TestContext.Current.CancellationToken);
        first.DependenciesInstalledBySetup.Add("VJoy");
        await store.SaveAsync(Path.Combine(target, "install-journal.json"), first, TestContext.Current.CancellationToken);
        File.WriteAllText(Path.Combine(payload, "app.bin"), "second");

        InstallationJournal updated = await new ApplicationInstaller(store).InstallAsync(payload, target, new Version(1, 1), TestContext.Current.CancellationToken);

        Assert.Equal("second", File.ReadAllText(Path.Combine(target, "app.bin")));
        Assert.Contains("VJoy", updated.DependenciesInstalledBySetup);
        Assert.False(Directory.Exists(target + ".previous"));
        Assert.NotNull(await store.LoadAsync(Path.Combine(target, "install-journal.json"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateStopsWithoutReplacingFilesWhenExistingJournalIsCorrupt()
    {
        string payload = Path.Combine(_root, "payload"), target = Path.Combine(_root, "app");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(payload, "app.bin"), "new");
        File.WriteAllText(Path.Combine(target, "app.bin"), "existing");
        File.WriteAllText(Path.Combine(target, "install-journal.json"), "{ invalid");

        await Assert.ThrowsAsync<JsonException>(() => new ApplicationInstaller(new JournalStore())
            .InstallAsync(payload, target, new Version(1, 1), TestContext.Current.CancellationToken));

        Assert.Equal("existing", File.ReadAllText(Path.Combine(target, "app.bin")));
        Assert.False(Directory.Exists(target + ".staging"));
    }

    [Fact]
    public async Task RefusesToReplaceNonEmptyDirectoryWithoutOwnershipJournal()
    {
        string payload = Path.Combine(_root, "payload"), target = Path.Combine(_root, "existing-data");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(payload, "app.bin"), "release");
        File.WriteAllText(Path.Combine(target, "personal.txt"), "do not delete");

        await Assert.ThrowsAsync<InvalidOperationException>(() => new ApplicationInstaller(new JournalStore())
            .InstallAsync(payload, target, new Version(1, 0), TestContext.Current.CancellationToken));

        Assert.Equal("do not delete", File.ReadAllText(Path.Combine(target, "personal.txt")));
        Assert.False(Directory.Exists(target + ".staging"));
    }

    [Fact]
    public async Task RefusesToReplaceFilesWhileDriverOperationIsPending()
    {
        string payload = Path.Combine(_root, "payload"), target = Path.Combine(_root, "app");
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, "app.bin"), "first");
        var store = new JournalStore();
        InstallationJournal journal = await new ApplicationInstaller(store).InstallAsync(
            payload, target, new Version(1, 0), TestContext.Current.CancellationToken);
        journal = journal with
        {
            PendingDependencyOperation = new("VJoy", "2.2.2", "vjoy.exe",
                DependencyOperationPhase.RestartRequired, true)
        };
        await store.SaveAsync(Path.Combine(target, "install-journal.json"), journal,
            TestContext.Current.CancellationToken);
        File.WriteAllText(Path.Combine(payload, "app.bin"), "second");

        await Assert.ThrowsAsync<InvalidOperationException>(() => new ApplicationInstaller(store)
            .InstallAsync(payload, target, new Version(1, 1), TestContext.Current.CancellationToken));

        Assert.Equal("first", File.ReadAllText(Path.Combine(target, "app.bin")));
    }

    [Fact]
    public async Task UninstallerRemovesOwnedFileButPreservesModifiedFile()
    {
        string payload = Path.Combine(_root, "payload"), target = Path.Combine(_root, "app");
        Directory.CreateDirectory(payload); File.WriteAllText(Path.Combine(payload, "owned.bin"), "owned"); File.WriteAllText(Path.Combine(payload, "changed.bin"), "original");
        var store = new JournalStore(); await new ApplicationInstaller(store).InstallAsync(payload, target, new Version(1, 0), TestContext.Current.CancellationToken);
        File.WriteAllText(Path.Combine(target, "changed.bin"), "user change");
        var result = await new ApplicationUninstaller(store).UninstallOwnedFilesAsync(Path.Combine(target, "install-journal.json"), TestContext.Current.CancellationToken);
        Assert.False(File.Exists(Path.Combine(target, "owned.bin"))); Assert.True(File.Exists(Path.Combine(target, "changed.bin")));
        Assert.Equal(["changed.bin"], result.PreservedFiles);
    }

    [Fact]
    public async Task UpdatePreservesUnownedFiles()
    {
        string payload = Path.Combine(_root, "payload"), target = Path.Combine(_root, "app");
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, "app.bin"), "first");
        var installer = new ApplicationInstaller(new JournalStore());
        await installer.InstallAsync(payload, target, new Version(1, 0), TestContext.Current.CancellationToken);
        File.WriteAllText(Path.Combine(target, "user-notes.txt"), "keep me");
        File.WriteAllText(Path.Combine(payload, "app.bin"), "second");

        await installer.InstallAsync(payload, target, new Version(1, 1), TestContext.Current.CancellationToken);

        Assert.Equal("keep me", File.ReadAllText(Path.Combine(target, "user-notes.txt")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(target, "app.bin")));
    }

    [Fact]
    public async Task UpdatePreservesModifiedRetiredFileButRemovesUnchangedRetiredFile()
    {
        string payload = Path.Combine(_root, "payload"), target = Path.Combine(_root, "app");
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, "modified-retired.bin"), "original");
        File.WriteAllText(Path.Combine(payload, "unchanged-retired.bin"), "original");
        var installer = new ApplicationInstaller(new JournalStore());
        await installer.InstallAsync(payload, target, new Version(1, 0), TestContext.Current.CancellationToken);
        File.WriteAllText(Path.Combine(target, "modified-retired.bin"), "user change");
        File.Delete(Path.Combine(payload, "modified-retired.bin"));
        File.Delete(Path.Combine(payload, "unchanged-retired.bin"));
        File.WriteAllText(Path.Combine(payload, "replacement.bin"), "new");

        await installer.InstallAsync(payload, target, new Version(1, 1), TestContext.Current.CancellationToken);

        Assert.Equal("user change", File.ReadAllText(Path.Combine(target, "modified-retired.bin")));
        Assert.False(File.Exists(Path.Combine(target, "unchanged-retired.bin")));
    }

    [Fact]
    public async Task RecoversPreviousInstallationWhenSwapWasInterrupted()
    {
        string payload = Path.Combine(_root, "payload"), target = Path.Combine(_root, "app");
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, "app.bin"), "first");
        var installer = new ApplicationInstaller(new JournalStore());
        await installer.InstallAsync(payload, target, new Version(1, 0), TestContext.Current.CancellationToken);
        Directory.Move(target, target + ".previous");
        File.WriteAllText(Path.Combine(payload, "app.bin"), "second");

        InstallationJournal result = await installer.InstallAsync(
            payload, target, new Version(1, 1), TestContext.Current.CancellationToken);

        Assert.Equal("second", File.ReadAllText(Path.Combine(target, "app.bin")));
        Assert.Equal("1.1", result.Version);
        Assert.False(Directory.Exists(target + ".previous"));
    }

    [Fact]
    public async Task RejectsOverlappingPayloadAndInstallDirectories()
    {
        string target = Path.Combine(_root, "app"), payload = Path.Combine(target, "payload");
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, "app.bin"), "release");

        await Assert.ThrowsAsync<InvalidOperationException>(() => new ApplicationInstaller(new JournalStore())
            .InstallAsync(payload, target, new Version(1, 0), TestContext.Current.CancellationToken));

        Assert.Equal("release", File.ReadAllText(Path.Combine(payload, "app.bin")));
    }

    [Fact]
    public async Task PreCanceledUpdateLeavesExistingInstallationUntouched()
    {
        string payload = Path.Combine(_root, "payload"), target = Path.Combine(_root, "app");
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, "app.bin"), "first");
        var installer = new ApplicationInstaller(new JournalStore());
        await installer.InstallAsync(payload, target, new Version(1, 0), TestContext.Current.CancellationToken);
        File.WriteAllText(Path.Combine(payload, "app.bin"), "second");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            installer.InstallAsync(payload, target, new Version(1, 1), canceled.Token));

        Assert.Equal("first", File.ReadAllText(Path.Combine(target, "app.bin")));
        Assert.False(Directory.Exists(target + ".staging"));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
