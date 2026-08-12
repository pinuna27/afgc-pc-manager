using AFGCPCManager.App;
using Xunit;

namespace AFGCPCManager.App.Tests;

public sealed class ManagerUpdateLauncherTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(),
        "AFGCPCManager.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void CreateStartInfoLaunchesInstalledSetupInImmediateUpdateMode()
    {
        Directory.CreateDirectory(_directory);
        string setup = Path.Combine(_directory, "AFGCPCManager.Setup.exe");
        File.WriteAllBytes(setup, []);

        var start = ManagerUpdateLauncher.CreateStartInfo(_directory);

        Assert.Equal(setup, start.FileName);
        Assert.True(start.UseShellExecute);
        Assert.Equal(_directory, start.WorkingDirectory);
        Assert.Equal(["--update", "--wizard-run", "--install-dir", _directory],
            start.ArgumentList);
    }

    [Fact]
    public void CreateStartInfoRejectsMissingUpdateHelper()
    {
        Directory.CreateDirectory(_directory);

        FileNotFoundException error = Assert.Throws<FileNotFoundException>(
            () => ManagerUpdateLauncher.CreateStartInfo(_directory));

        Assert.Equal(Path.Combine(_directory, "AFGCPCManager.Setup.exe"),
            error.FileName);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
        catch { }
    }
}
