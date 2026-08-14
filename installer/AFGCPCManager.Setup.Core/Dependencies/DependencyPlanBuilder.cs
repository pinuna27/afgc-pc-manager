using AFGCPCManager.Setup.Core.Models;

namespace AFGCPCManager.Setup.Core.Dependencies;

public enum DependencyAction { None, Install, Update, Repair, ReportOutdated, Block }

public sealed record DependencyPlan(DependencyId Id, DependencyAction Action, bool ManagedByAfgc, string Reason);

public static class DependencyPlanBuilder
{
    public static DependencyPlan Build(DependencyState state, Version target, InstallationJournal? journal)
    {
        string name = state.Id.ToString();
        bool interruptedManagedInstall = journal?.PendingDependencyOperation is
        {
            Phase: DependencyOperationPhase.InstallerStarted or DependencyOperationPhase.RestartRequired
                or DependencyOperationPhase.DeferredUntilRestart
        } pending
            && pending.Dependency.Equals(name, StringComparison.OrdinalIgnoreCase);
        bool managed = journal?.DependenciesInstalledBySetup.Contains(name) == true || interruptedManagedInstall;
        bool knownPreexisting = journal?.DependenciesPresentBeforeSetup.Contains(name) == true;

        if (state.EffectiveReadiness is DependencyReadiness.Unknown or DependencyReadiness.PendingRestart)
            return new(state.Id, DependencyAction.Block, managed, "The dependency state cannot be verified safely yet.");
        if (state.EffectiveReadiness == DependencyReadiness.Unhealthy)
            return new(state.Id, DependencyAction.Block, managed, "The dependency is present but is not operational.");

        if (!state.IsInstalled)
            return new(state.Id, managed ? DependencyAction.Repair : DependencyAction.Install, true,
                managed ? "An AFGC-managed dependency is missing." : "The required dependency is not installed.");

        if (journal is null)
            return new(state.Id, DependencyAction.None, false, "The dependency was present before AFGC PC Manager.");

        if (state.Version is not null && IsOlder(state.Version, target))
            return managed
                ? new(state.Id, DependencyAction.Update, true, "An AFGC-managed dependency has an update.")
                : new(state.Id, DependencyAction.ReportOutdated, false, "A pre-existing dependency has an update available.");

        return new(state.Id, DependencyAction.None, managed && !knownPreexisting,
            managed ? "The AFGC-managed dependency is current." : "The pre-existing dependency is current.");
    }

    internal static bool IsOlder(Version installed, Version target)
    {
        int[] left = [installed.Major, installed.Minor, Math.Max(installed.Build, 0), Math.Max(installed.Revision, 0)];
        int[] right = [target.Major, target.Minor, Math.Max(target.Build, 0), Math.Max(target.Revision, 0)];
        for (int index = 0; index < left.Length; index++)
            if (left[index] != right[index]) return left[index] < right[index];
        return false;
    }
}
