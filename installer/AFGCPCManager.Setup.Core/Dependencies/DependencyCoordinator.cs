using AFGCPCManager.Setup.Core.Installation;
using AFGCPCManager.Setup.Core.Models;

namespace AFGCPCManager.Setup.Core.Dependencies;

public sealed record DependencyExecutionResult(IReadOnlyList<DependencyPlan> Plans, bool RestartRequired);

public sealed class DependencyCoordinator(
    IDependencyDetector detector,
    IDependencyInstaller installer,
    JournalStore journalStore,
    Action<string>? progress = null,
    Func<DateTimeOffset>? bootStartedAt = null)
{
    private readonly Func<DateTimeOffset> _bootStartedAt = bootStartedAt ?? (() =>
        DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64));

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

        if (journal.PendingDependencyOperation is { } pendingOperation
            && (!Enum.TryParse(pendingOperation.Dependency, ignoreCase: true, out DependencyId pendingId)
                || !packages.ContainsKey(pendingId)))
            throw new InvalidOperationException(
                $"The unfinished {pendingOperation.Dependency} driver operation is not present in this setup package.");

        if (journal.PendingDependencyOperation is
                { Phase: DependencyOperationPhase.RestartRequired, BootStartedAtUtc: DateTimeOffset installedBoot }
            && Math.Abs((_bootStartedAt() - installedBoot).TotalSeconds) < 5)
        {
            // The pending operation represents the whole driver-installation batch. Do not
            // inspect or provision either driver until Windows has actually restarted.
            return new(plans, true);
        }

        foreach ((DependencyId id, (Version target, string installerPath)) in packages)
        {
            DependencyState before = detector.Detect(id);
            bool pendingForDependency = journal.PendingDependencyOperation is
                    { Phase: DependencyOperationPhase.InstallerStarted or DependencyOperationPhase.RestartRequired } pending
                && pending.Dependency.Equals(id.ToString(), StringComparison.OrdinalIgnoreCase);
            Version? pendingTarget = pendingForDependency
                && Version.TryParse(journal.PendingDependencyOperation!.TargetVersion, out Version? parsedPendingTarget)
                    ? parsedPendingTarget
                    : null;
            bool pendingTargetReady = before.EffectiveReadiness == DependencyReadiness.Ready
                && (before.Version is null || pendingTarget is null
                    || !DependencyPlanBuilder.IsOlder(before.Version, pendingTarget));
            if (pendingForDependency && pendingTargetReady)
            {
                journal.DependenciesInstalledBySetup.Add(id.ToString());
                journal.DependenciesPresentBeforeSetup.Remove(id.ToString());
                journal = journal with { PendingDependencyOperation = null };
                await journalStore.SaveAsync(journalPath, journal, cancellationToken);
            }
            else if (pendingForDependency)
            {
                throw new InvalidOperationException(BuildResumeFailure(id, before));
            }
            DependencyPlan plan = DependencyPlanBuilder.Build(before, target, journal);
            plans.Add(plan);

            if (plan.Action == DependencyAction.Block)
                throw new InvalidOperationException(BuildResumeFailure(id, before));

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
            {
                DependencyState detectedAfterExit = detector.Detect(id);
                bool targetInstalled = detectedAfterExit.IsInstalled &&
                    (detectedAfterExit.Version is null || !DependencyPlanBuilder.IsOlder(detectedAfterExit.Version, target));
                if (!targetInstalled)
                {
                    journal = journal with { PendingDependencyOperation = null };
                    await journalStore.SaveAsync(journalPath, journal, cancellationToken);
                    throw new InvalidOperationException($"The {id} installer exited with code {result.ExitCode}.");
                }
                result = new(true, true, result.ExitCode);
                progress?.Invoke($"{name} installed successfully and requires a Windows restart.");
            }

            // Both dependencies are kernel drivers. Even when a vendor installer returns
            // success without a reboot code, defer provisioning until Windows has rebooted.
            result = result with { RestartRequired = true };

            progress?.Invoke($"{name} finished installing. Windows must restart before setup can continue.");

            journal.DependenciesInstalledBySetup.Add(id.ToString());
            journal.DependenciesPresentBeforeSetup.Remove(id.ToString());
            restartRequired |= result.RestartRequired;
            journal = journal with
            {
                PendingDependencyOperation = result.RestartRequired
                    ? journal.PendingDependencyOperation with
                    {
                        Phase = DependencyOperationPhase.RestartRequired,
                        RestartRequired = true,
                        BootStartedAtUtc = _bootStartedAt()
                    }
                    : null
            };
            await journalStore.SaveAsync(journalPath, journal, cancellationToken);
            if (result.ExitCode == 1641)
                return new(plans, true);
        }

        return new(plans, restartRequired);
    }

    private static string BuildResumeFailure(DependencyId id, DependencyState state)
    {
        string name = id == DependencyId.VJoy ? "vJoy" : "HidHide";
        string evidence = state.Evidence is { Count: > 0 }
            ? " Detected evidence: " + string.Join("; ", state.Evidence.Select(item =>
                $"{item.Source}={(item.Present is null ? "unknown" : item.Present.Value ? "present" : "absent")}")) + "."
            : string.Empty;
        return state.EffectiveReadiness switch
        {
            DependencyReadiness.PendingRestart => $"{name} is installed but Windows still reports it as pending restart. Restart Windows, then run setup again.{evidence}",
            DependencyReadiness.Unhealthy => $"{name} is installed but is not operational after the restart. Setup will not reinstall it automatically; uninstall {name}, restart Windows, and retry setup.{evidence}",
            DependencyReadiness.Unknown => $"Setup could not reliably verify {name} after the restart. It will not reinstall it automatically. Restart Windows once more or repair {name} manually, then retry.{evidence}",
            _ => $"{name} was not present after its installer requested a restart. Setup will not rerun the installer automatically. Uninstall any partial {name} installation, restart Windows, and retry setup.{evidence}"
        };
    }
}
