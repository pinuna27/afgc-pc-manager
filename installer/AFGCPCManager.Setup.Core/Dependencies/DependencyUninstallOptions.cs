using AFGCPCManager.Setup.Core.Models;

namespace AFGCPCManager.Setup.Core.Dependencies;

public sealed record DependencyUninstallOptions(
    bool UninstallVJoy,
    bool UninstallViGEmBus,
    bool UninstallHidHide)
{
    public static DependencyUninstallOptions FromJournal(InstallationJournal journal) => new(
        journal.DependenciesInstalledBySetup.Contains(DependencyId.VJoy.ToString()),
        journal.DependenciesInstalledBySetup.Contains(DependencyId.ViGEmBus.ToString()),
        journal.DependenciesInstalledBySetup.Contains(DependencyId.HidHide.ToString()));
}

public static class DependencyUninstallContinuation
{
    public static List<string> AfterCompleted(IEnumerable<string> arguments, DependencyId dependency)
    {
        string completedArgument = DependencyNames.RemovalArgument(dependency);
        return arguments.Where(argument => !argument.Equals(completedArgument, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
