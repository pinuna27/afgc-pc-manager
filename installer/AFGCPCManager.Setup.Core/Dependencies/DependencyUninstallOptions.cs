using AFGCPCManager.Setup.Core.Models;

namespace AFGCPCManager.Setup.Core.Dependencies;

public sealed record DependencyUninstallOptions(bool UninstallVJoy, bool UninstallHidHide)
{
    public static DependencyUninstallOptions FromJournal(InstallationJournal journal) => new(
        journal.DependenciesInstalledBySetup.Contains(DependencyId.VJoy.ToString()),
        journal.DependenciesInstalledBySetup.Contains(DependencyId.HidHide.ToString()));
}

public static class DependencyUninstallContinuation
{
    public static List<string> AfterCompleted(IEnumerable<string> arguments, DependencyId dependency)
    {
        string completedArgument = dependency == DependencyId.VJoy ? "--remove-vjoy" : "--remove-hidhide";
        return arguments.Where(argument => !argument.Equals(completedArgument, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
