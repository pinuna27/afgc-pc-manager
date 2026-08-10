using AFGCPCManager.Setup.Core.Models;

namespace AFGCPCManager.Setup.Core.Dependencies;

public static class DependencyManifestPolicy
{
    public static bool CanUseInstalledDependenciesWithoutManifest(
        IEnumerable<DependencyState> states, InstallationJournal journal)
    {
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(journal);
        if (journal.PendingDependencyOperation is not null) return false;
        Dictionary<DependencyId, DependencyState> byId = states
            .GroupBy(state => state.Id).ToDictionary(group => group.Key, group => group.Last());
        return Enum.GetValues<DependencyId>().All(id =>
            byId.TryGetValue(id, out DependencyState? state)
            && state.EffectiveReadiness == DependencyReadiness.Ready);
    }
}
