using AFGCPCManager.Setup.Core.Dependencies;
using AFGCPCManager.Setup.Core.Models;

namespace AFGCPCManager.Setup.Core.Tests.Dependencies;

public sealed class DependencyPlanBuilderTests
{
    [Fact]
    public void MissingDependencyOnFirstInstall_IsInstalledAndOwned()
    {
        var plan = DependencyPlanBuilder.Build(new(DependencyId.VJoy, false), new(2, 2, 2), null);
        Assert.Equal(DependencyAction.Install, plan.Action);
        Assert.True(plan.ManagedByAfgc);
    }

    [Fact]
    public void ExistingDependencyOnFirstInstall_IsPreserved()
    {
        var plan = DependencyPlanBuilder.Build(new(DependencyId.HidHide, true, new(1, 5)), new(1, 5), null);
        Assert.Equal(DependencyAction.None, plan.Action);
        Assert.False(plan.ManagedByAfgc);
    }

    [Fact]
    public void MissingManagedDependency_IsRepaired()
    {
        InstallationJournal journal = JournalWith(installed: ["VJoy"]);
        var plan = DependencyPlanBuilder.Build(new(DependencyId.VJoy, false), new(2, 2, 2), journal);
        Assert.Equal(DependencyAction.Repair, plan.Action);
    }

    [Fact]
    public void OutdatedManagedDependency_IsUpdated()
    {
        InstallationJournal journal = JournalWith(installed: ["HidHide"]);
        var plan = DependencyPlanBuilder.Build(new(DependencyId.HidHide, true, new(1, 4)), new(1, 5), journal);
        Assert.Equal(DependencyAction.Update, plan.Action);
    }

    [Fact]
    public void OutdatedPreexistingDependency_IsOnlyReported()
    {
        InstallationJournal journal = JournalWith(preexisting: ["VJoy"]);
        var plan = DependencyPlanBuilder.Build(new(DependencyId.VJoy, true, new(2, 1)), new(2, 2), journal);
        Assert.Equal(DependencyAction.ReportOutdated, plan.Action);
        Assert.False(plan.ManagedByAfgc);
    }

    private static InstallationJournal JournalWith(string[]? installed = null, string[]? preexisting = null) => new()
    {
        InstallDirectory = @"C:\Program Files\AFGC PC Manager",
        Version = "1.0.0",
        DependenciesInstalledBySetup = installed is null ? [] : [.. installed],
        DependenciesPresentBeforeSetup = preexisting is null ? [] : [.. preexisting]
    };
}
