using AFGCPCManager.Setup.Core.Dependencies;
using AFGCPCManager.Setup.Core.Models;

namespace AFGCPCManager.Setup.Core.Tests.Dependencies;

public sealed class DependencyUninstallTests
{
    [Fact]
    public void DefaultsOnOnlyForDependenciesInstalledByAfgc()
    {
        var journal = new InstallationJournal { InstallDirectory = "x", Version = "1", DependenciesInstalledBySetup = ["VJoy"], DependenciesPresentBeforeSetup = ["HidHide"] };
        DependencyUninstallOptions options = DependencyUninstallOptions.FromJournal(journal);
        Assert.True(options.UninstallVJoy); Assert.False(options.UninstallHidHide);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\vJoy\\uninstall.exe\" /remove", "C:\\Program Files\\vJoy\\uninstall.exe", "/remove")]
    [InlineData("MsiExec.exe /I{ABC}", "MsiExec.exe", "/I{ABC}")]
    public void SplitsRegisteredCommands(string command, string executable, string arguments)
    {
        Assert.Equal((executable, arguments), RegisteredDependencyUninstaller.SplitCommand(command));
    }

    [Theory]
    [InlineData(DependencyId.VJoy, "vJoy Device Driver", true)]
    [InlineData(DependencyId.HidHide, "Nefarius HidHide", true)]
    [InlineData(DependencyId.HidHide, "ViGEm Bus Driver", false)]
    public void MatchesOnlyRequestedDependency(DependencyId id, string name, bool expected) =>
        Assert.Equal(expected, RegisteredDependencyUninstaller.Matches(id, name));
}
