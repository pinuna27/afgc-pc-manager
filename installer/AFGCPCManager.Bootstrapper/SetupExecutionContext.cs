namespace AFGCPCManager.Bootstrapper;

internal sealed class SetupExecutionContext(
    bool cliMode,
    Action<string>? progress = null,
    IInstalledApplicationController? installedApplication = null)
{
    private string? _scheduledApplicationDestination;

    public bool CliMode { get; } = cliMode;
    public Action<string>? Progress { get; set; } = progress;
    public string? LastError { get; set; }
    public bool DelegatedToElevatedWizard { get; set; }
    public IInstalledApplicationController InstalledApplication { get; } =
        installedApplication ?? new InstalledApplicationController();

    public void ResetResult()
    {
        LastError = null;
        DelegatedToElevatedWizard = false;
        _scheduledApplicationDestination = null;
    }

    public void ScheduleInstalledApplicationStart(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        _scheduledApplicationDestination = destination;
    }

    public void StartScheduledApplication()
    {
        string? destination = _scheduledApplicationDestination;
        _scheduledApplicationDestination = null;
        if (destination is not null)
            InstalledApplication.StartUnelevated(destination);
    }

    public void Report(string message)
    {
        Console.WriteLine(message);
        Progress?.Invoke(message);
    }
}
