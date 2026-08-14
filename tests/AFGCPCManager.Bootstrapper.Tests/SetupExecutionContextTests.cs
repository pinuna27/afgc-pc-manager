using AFGCPCManager.Bootstrapper;

namespace AFGCPCManager.Bootstrapper.Tests;

public sealed class SetupExecutionContextTests
{
    [Fact]
    public void ResultAndProgressStateAreOwnedByOneExecution()
    {
        var messages = new List<string>();
        var first = new SetupExecutionContext(cliMode: false, messages.Add)
        {
            LastError = "failure",
            DelegatedToElevatedWizard = true
        };
        var second = new SetupExecutionContext(cliMode: true);

        first.Report("working");
        first.ResetResult();

        Assert.Equal(["working"], messages);
        Assert.Null(first.LastError);
        Assert.False(first.DelegatedToElevatedWizard);
        Assert.True(second.CliMode);
        Assert.Null(second.LastError);
    }

    [Fact]
    public void InstalledApplicationLifecycleIsInjectable()
    {
        var lifecycle = new FakeInstalledApplicationController();

        var execution = new SetupExecutionContext(
            cliMode: false, installedApplication: lifecycle);

        Assert.Same(lifecycle, execution.InstalledApplication);
    }

    [Fact]
    public void ScheduledApplicationDoesNotStartUntilExplicitlyRequested()
    {
        var lifecycle = new FakeInstalledApplicationController();
        var execution = new SetupExecutionContext(
            cliMode: false, installedApplication: lifecycle);

        execution.ScheduleInstalledApplicationStart("C:\\AFGC");

        Assert.Empty(lifecycle.StartedDestinations);

        execution.StartScheduledApplication();
        execution.StartScheduledApplication();

        Assert.Equal(["C:\\AFGC"], lifecycle.StartedDestinations);
    }

    private sealed class FakeInstalledApplicationController
        : IInstalledApplicationController
    {
        public List<string> StartedDestinations { get; } = [];

        public Task StopAsync(
            string destination,
            Action<string> report,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void StartUnelevated(string destination)
        {
            StartedDestinations.Add(destination);
        }
    }
}
