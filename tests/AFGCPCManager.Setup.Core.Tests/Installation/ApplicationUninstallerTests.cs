using AFGCPCManager.Setup.Core.Installation;
using AFGCPCManager.Setup.Core.Models;
using AFGCPCManager.Setup.Core.Security;

namespace AFGCPCManager.Setup.Core.Tests.Installation;

public sealed class ApplicationUninstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "afgc-uninstaller-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RemovesOwnedReadOnlyFile()
    {
        string install = Path.Combine(_root, "app");
        Directory.CreateDirectory(install);
        string owned = Path.Combine(install, "owned.bin");
        await File.WriteAllTextAsync(owned, "owned", TestContext.Current.CancellationToken);
        string hash = await Hashing.Sha256Async(owned, TestContext.Current.CancellationToken);
        File.SetAttributes(owned, FileAttributes.ReadOnly);
        string journalPath = await SaveJournalAsync(install, [new("owned.bin", hash)]);

        UninstallResult result = await new ApplicationUninstaller(new JournalStore())
            .UninstallOwnedFilesAsync(journalPath, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.RemovedFiles);
        Assert.False(File.Exists(owned));
    }

    [Fact]
    public async Task RejectsJournalStoredOutsideRecordedInstallDirectory()
    {
        string install = Path.Combine(_root, "app"), journalDirectory = Path.Combine(_root, "copied-journal");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(journalDirectory);
        string owned = Path.Combine(install, "owned.bin");
        await File.WriteAllTextAsync(owned, "owned", TestContext.Current.CancellationToken);
        string hash = await Hashing.Sha256Async(owned, TestContext.Current.CancellationToken);
        string journalPath = Path.Combine(journalDirectory, "install-journal.json");
        await new JournalStore().SaveAsync(journalPath, new InstallationJournal
        {
            InstallDirectory = install,
            Version = "1.0",
            Files = [new("owned.bin", hash)]
        }, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidDataException>(() => new ApplicationUninstaller(new JournalStore())
            .UninstallOwnedFilesAsync(journalPath, TestContext.Current.CancellationToken));

        Assert.True(File.Exists(owned));
    }

    [Fact]
    public async Task PreCanceledUninstallDoesNotDeleteFiles()
    {
        string install = Path.Combine(_root, "app");
        Directory.CreateDirectory(install);
        string owned = Path.Combine(install, "owned.bin");
        await File.WriteAllTextAsync(owned, "owned", TestContext.Current.CancellationToken);
        string hash = await Hashing.Sha256Async(owned, TestContext.Current.CancellationToken);
        string journalPath = await SaveJournalAsync(install, [new("owned.bin", hash)]);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ApplicationUninstaller(new JournalStore())
            .UninstallOwnedFilesAsync(journalPath, canceled.Token));

        Assert.True(File.Exists(owned));
    }

    [Fact]
    public async Task MissingOwnedFilesAreIdempotentlyIgnored()
    {
        string install = Path.Combine(_root, "app");
        Directory.CreateDirectory(install);
        string journalPath = await SaveJournalAsync(install, [new("already-gone.bin", new string('A', 64))]);

        UninstallResult result = await new ApplicationUninstaller(new JournalStore())
            .UninstallOwnedFilesAsync(journalPath, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.RemovedFiles);
        Assert.Empty(result.PreservedFiles);
    }

    [Fact]
    public async Task ReportsUnownedFilesLeftInInstallDirectory()
    {
        string install = Path.Combine(_root, "app");
        Directory.CreateDirectory(install);
        await File.WriteAllTextAsync(Path.Combine(install, "user-notes.txt"), "keep",
            TestContext.Current.CancellationToken);
        string journalPath = await SaveJournalAsync(install, []);

        UninstallResult result = await new ApplicationUninstaller(new JournalStore())
            .UninstallOwnedFilesAsync(journalPath, TestContext.Current.CancellationToken);

        Assert.Equal(["user-notes.txt"], result.PreservedFiles);
    }

    [Fact]
    public async Task KeepsInstalledUninstallerUntilOtherOwnedFilesAreRemoved()
    {
        string install = Path.Combine(_root, "app");
        Directory.CreateDirectory(install);
        string uninstaller = Path.Combine(install, "AFGCPCManager.Uninstaller.exe");
        string locked = Path.Combine(install, "locked.bin");
        await File.WriteAllTextAsync(uninstaller, "uninstaller", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(locked, "locked", TestContext.Current.CancellationToken);
        string uninstallerHash = await Hashing.Sha256Async(uninstaller, TestContext.Current.CancellationToken);
        string lockedHash = await Hashing.Sha256Async(locked, TestContext.Current.CancellationToken);
        string journalPath = await SaveJournalAsync(install,
            [new("AFGCPCManager.Uninstaller.exe", uninstallerHash), new("locked.bin", lockedHash)]);
        await using FileStream lockStream = new(locked, FileMode.Open, FileAccess.Read, FileShare.None);

        await Assert.ThrowsAsync<IOException>(() => new ApplicationUninstaller(new JournalStore())
            .UninstallOwnedFilesAsync(journalPath, TestContext.Current.CancellationToken));

        Assert.True(File.Exists(uninstaller));
    }

    private static async Task<string> SaveJournalAsync(string install, List<InstalledFile> files)
    {
        string path = Path.Combine(install, "install-journal.json");
        await new JournalStore().SaveAsync(path, new InstallationJournal
        {
            InstallDirectory = install,
            Version = "1.0",
            Files = files
        }, TestContext.Current.CancellationToken);
        return path;
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }
}
