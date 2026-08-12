using AFGCPCManager.Setup.Core.Dependencies;
using AFGCPCManager.Setup.Core.Models;

namespace AFGCPCManager.Setup.Core.Tests.Dependencies;

public sealed class DependencyUninstallTests
{
    [Fact]
    public void DefaultsOnOnlyForDependenciesInstalledByAfgc()
    {
        var journal = new InstallationJournal { InstallDirectory = "x", Version = "1", DependenciesInstalledBySetup = ["VJoy", "ViGEmBus"], DependenciesPresentBeforeSetup = ["HidHide"] };
        DependencyUninstallOptions options = DependencyUninstallOptions.FromJournal(journal);
        Assert.True(options.UninstallVJoy);
        Assert.True(options.UninstallViGEmBus);
        Assert.False(options.UninstallHidHide);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\vJoy\\uninstall.exe\" /remove", "C:\\Program Files\\vJoy\\uninstall.exe", "/remove")]
    [InlineData("C:\\Program Files\\vJoy\\uninstall.exe /remove", "C:\\Program Files\\vJoy\\uninstall.exe", "/remove")]
    [InlineData("MsiExec.exe /I{ABC}", "MsiExec.exe", "/I{ABC}")]
    public void SplitsRegisteredCommands(string command, string executable, string arguments)
    {
        Assert.Equal((executable, arguments), RegisteredDependencyUninstaller.SplitCommand(command));
    }

    [Fact]
    public void ExpandsEnvironmentVariablesInRegisteredCommand()
    {
        string command = @"%SystemRoot%\System32\msiexec.exe /X{ABC}";

        (string executable, string arguments) = RegisteredDependencyUninstaller.SplitCommand(command);

        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "msiexec.exe"), executable,
            ignoreCase: true);
        Assert.Equal("/X{ABC}", arguments);
    }

    [Theory]
    [InlineData(DependencyId.VJoy, "vJoy Device Driver", true)]
    [InlineData(DependencyId.ViGEmBus, "Nefarius Virtual Gamepad Emulation Bus Driver", true)]
    [InlineData(DependencyId.ViGEmBus, "ViGEm Bus Driver", true)]
    [InlineData(DependencyId.HidHide, "Nefarius HidHide", true)]
    [InlineData(DependencyId.VJoy, "Third-party vJoy Feeder", false)]
    [InlineData(DependencyId.HidHide, "ViGEm Bus Driver", false)]
    public void MatchesOnlyRequestedDependency(DependencyId id, string name, bool expected) =>
        Assert.Equal(expected, RegisteredDependencyUninstaller.Matches(id, name));

    [Fact]
    public void ContinuationDropsOnlyTheCompletedDependency()
    {
        string[] arguments = ["--wizard-run", "--detached", "C:\\App", "--remove-vjoy", "--remove-vigembus", "--remove-hidhide"];

        List<string> afterVJoy = DependencyUninstallContinuation.AfterCompleted(arguments, DependencyId.VJoy);
        List<string> afterViGEm = DependencyUninstallContinuation.AfterCompleted(afterVJoy, DependencyId.ViGEmBus);
        List<string> afterHidHide = DependencyUninstallContinuation.AfterCompleted(afterViGEm, DependencyId.HidHide);

        Assert.DoesNotContain("--remove-vjoy", afterVJoy, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("--remove-vigembus", afterVJoy, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("--remove-hidhide", afterVJoy, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(["--wizard-run", "--detached", "C:\\App"], afterHidHide);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(3010, true, false)]
    [InlineData(1641, true, true)]
    public async Task RemovalAdvancesContinuationAfterSuccessfulVendorExit(
        int exitCode, bool restartRequired, bool restartInitiated)
    {
        var uninstaller = new FakeRegisteredUninstaller(exitCode);
        var registrations = new List<IReadOnlyList<string>>();
        var coordinator = new DependencyRemovalCoordinator(uninstaller,
            arguments => registrations.Add(arguments.ToArray()));
        string[] arguments = ["--detached", "C:\\App", "--remove-vjoy", "--remove-hidhide"];

        DependencyRemovalExecutionResult result = await coordinator.RemoveAsync(
            DependencyId.VJoy, arguments, TestContext.Current.CancellationToken);

        Assert.Equal(restartRequired, result.RestartRequired);
        Assert.Equal(restartInitiated, result.RestartInitiated);
        Assert.Equal(2, registrations.Count);
        Assert.Contains("--remove-vjoy", registrations[0]);
        Assert.DoesNotContain("--remove-vjoy", registrations[1]);
        Assert.Contains("--remove-hidhide", result.ContinuationArguments);
        Assert.Equal(1, uninstaller.Calls);
    }

    [Fact]
    public async Task RemovalSkipsMissingRegistrationWithoutCreatingContinuation()
    {
        var uninstaller = new FakeRegisteredUninstaller(0) { Registered = false };
        var registrations = new List<IReadOnlyList<string>>();
        var coordinator = new DependencyRemovalCoordinator(uninstaller,
            arguments => registrations.Add(arguments));

        DependencyRemovalExecutionResult result = await coordinator.RemoveAsync(
            DependencyId.HidHide, ["--remove-hidhide"], TestContext.Current.CancellationToken);

        Assert.Empty(registrations);
        Assert.Empty(result.ContinuationArguments);
        Assert.Equal(0, uninstaller.Calls);
    }

    [Fact]
    public async Task RemovalKeepsCurrentContinuationWhenVendorFails()
    {
        var registrations = new List<IReadOnlyList<string>>();
        var coordinator = new DependencyRemovalCoordinator(new FakeRegisteredUninstaller(5),
            arguments => registrations.Add(arguments.ToArray()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RemoveAsync(
            DependencyId.VJoy, ["--remove-vjoy", "--remove-hidhide"], TestContext.Current.CancellationToken));

        Assert.Single(registrations);
        Assert.Contains("--remove-vjoy", registrations[0]);
    }

    private sealed class FakeRegisteredUninstaller(int exitCode) : IRegisteredDependencyUninstaller
    {
        public bool Registered { get; set; } = true;
        public int Calls { get; private set; }
        public RegisteredUninstaller? Find(DependencyId dependency) => Registered
            ? new RegisteredUninstaller(dependency.ToString(), "uninstall.exe")
            : null;
        public Task<int> UninstallInteractiveAsync(RegisteredUninstaller registration,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(exitCode);
        }
    }
}
