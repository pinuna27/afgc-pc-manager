using AFGCPCManager.Setup.Core.Installation;

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
    public async Task UninstallerRemovesOwnedFileButPreservesModifiedFile()
    {
        string payload = Path.Combine(_root, "payload"), target = Path.Combine(_root, "app");
        Directory.CreateDirectory(payload); File.WriteAllText(Path.Combine(payload, "owned.bin"), "owned"); File.WriteAllText(Path.Combine(payload, "changed.bin"), "original");
        var store = new JournalStore(); await new ApplicationInstaller(store).InstallAsync(payload, target, new Version(1, 0), TestContext.Current.CancellationToken);
        File.WriteAllText(Path.Combine(target, "changed.bin"), "user change");
        var result = await new ApplicationUninstaller(store).UninstallOwnedFilesAsync(Path.Combine(target, "install-journal.json"), TestContext.Current.CancellationToken);
        Assert.False(File.Exists(Path.Combine(target, "owned.bin"))); Assert.True(File.Exists(Path.Combine(target, "changed.bin")));
        Assert.Equal(["changed.bin"], result.PreservedModifiedFiles);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
