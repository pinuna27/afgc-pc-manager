using AFGCPCManager.Setup.Core.Installation;
using AFGCPCManager.Setup.Core.Models;

namespace AFGCPCManager.Setup.Core.Dependencies;

public sealed record DependencyExecutionResult(IReadOnlyList<DependencyPlan> Plans, bool RestartRequired);

public sealed class DependencyCoordinator(
    IDependencyDetector detector,
    IDependencyInstaller installer,
    JournalStore journalStore,
    Action<string>? progress = null)
{
    public async Task<DependencyExecutionResult> EnsureAsync(
        string journalPath,
        IReadOnlyDictionary<DependencyId, (Version Target, string InstallerPath)> packages,
        bool allowUpdates,
        CancellationToken cancellationToken = default)
    {
        InstallationJournal journal = await journalStore.LoadAsync(journalPath, cancellationToken)
            ?? throw new InvalidOperationException("The application installation journal is missing.");
        var plans = new List<DependencyPlan>();
        bool restartRequired = false;

        foreach ((DependencyId id, (Version target, string installerPath)) in packages)
        {
            DependencyState before = detector.Detect(id);
            if (before.IsInstalled
                && journal.PendingDependencyOperation is
                    { Phase: DependencyOperationPhase.InstallerStarted or DependencyOperationPhase.RestartRequired } pending
                && pending.Dependency.Equals(id.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                journal.DependenciesInstalledBySetup.Add(id.ToString());
                journal.DependenciesPresentBeforeSetup.Remove(id.ToString());
                journal = journal with { PendingDependencyOperation = null };
                await journalStore.SaveAsync(journalPath, journal, cancellationToken);
            }
            DependencyPlan plan = DependencyPlanBuilder.Build(before, target, journal);
            plans.Add(plan);

            if (before.IsInstalled && !journal.DependenciesInstalledBySetup.Contains(id.ToString()))
            {
                journal.DependenciesPresentBeforeSetup.Add(id.ToString());
                await journalStore.SaveAsync(journalPath, journal, cancellationToken);
            }

            bool execute = plan.Action is DependencyAction.Install or DependencyAction.Repair
                || (allowUpdates && plan.Action == DependencyAction.Update);
            if (!execute) continue;

            string name = id == DependencyId.VJoy ? "vJoy" : "HidHide";
            progress?.Invoke(plan.Action switch
            {
                DependencyAction.Install => $"Installing {name}... Follow the installer prompts.",
                DependencyAction.Update => $"Updating {name}... Follow the installer prompts.",
                _ => $"Repairing {name}... Follow the installer prompts."
            });

            journal = journal with
            {
                PendingDependencyOperation = new(id.ToString(), target.ToString(), installerPath,
                    DependencyOperationPhase.Prepared, false)
            };
            await journalStore.SaveAsync(journalPath, journal, cancellationToken);
            journal = journal with
            {
                PendingDependencyOperation = journal.PendingDependencyOperation with
                { Phase = DependencyOperationPhase.InstallerStarted }
            };
            await journalStore.SaveAsync(journalPath, journal, cancellationToken);

            DependencyInstallResult result = await installer.RunInteractiveAsync(installerPath, cancellationToken);
            if (!result.Succeeded)
                throw new InvalidOperationException($"The {id} installer exited with code {result.ExitCode}.");

            progress?.Invoke(result.RestartRequired
                ? $"{name} finished installing. Windows must restart before setup can continue."
                : $"{name} finished installing successfully.");

            DependencyState after = detector.Detect(id);
            if (!after.IsInstalled && !result.RestartRequired)
                throw new InvalidOperationException($"{id} was not detected after its installer completed.");

            journal.DependenciesInstalledBySetup.Add(id.ToString());
            journal.DependenciesPresentBeforeSetup.Remove(id.ToString());
            restartRequired |= result.RestartRequired;
            journal = journal with
            {
                PendingDependencyOperation = result.RestartRequired
                    ? journal.PendingDependencyOperation with
                    {
                        Phase = DependencyOperationPhase.RestartRequired,
                        RestartRequired = true
                    }
                    : null
            };
            await journalStore.SaveAsync(journalPath, journal, cancellationToken);
            if (result.RestartRequired) break;
        }

        return new(plans, restartRequired);
    }
}
