using AFGCPCManager.Setup.Core.Models;

namespace AFGCPCManager.Setup.Core.Dependencies;

public sealed record DependencyUninstallOptions(bool UninstallVJoy, bool UninstallHidHide)
{
    public static DependencyUninstallOptions FromJournal(InstallationJournal journal) => new(
        journal.DependenciesInstalledBySetup.Contains(DependencyId.VJoy.ToString()),
        journal.DependenciesInstalledBySetup.Contains(DependencyId.HidHide.ToString()));
}
