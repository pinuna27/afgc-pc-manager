using AFGCPCManager.Core.Devices;
using AFGCPCManager.Core.Mapping;
using AFGCPCManager.Core.Registration;
using AFGCPCManager.Core.Settings;
using Xunit;

namespace AFGCPCManager.App.Tests;

public sealed class ControllerRegistrationReconcilerTests
{
    [Fact]
    public void RenamedStableIdentityUpdatesLabelAndPreservesRegistration()
    {
        var registry = new ControllerRegistry(new SettingsDocument
        {
            Controllers = [new()
            {
                StableId = "same", DisplayName = "Old name", RegistrationOrder = 3,
                PreferredVJoyId = 7
            }],
            Overrides = new() { ["same"] = new() { HomeButton = HomeButtonMode.Disabled } }
        });

        bool changed = ControllerRegistrationReconciler.Reconcile(registry,
            [Controller("same", "Renamed controller")], DateTimeOffset.UtcNow);

        RegisteredController updated = Assert.Single(registry.Snapshot.Controllers);
        Assert.True(changed);
        Assert.Equal("Renamed controller", updated.DisplayName);
        Assert.Equal(3, updated.RegistrationOrder);
        Assert.Equal((uint)7, updated.PreferredVJoyId);
        Assert.Equal(HomeButtonMode.Disabled,
            registry.Snapshot.Overrides["same"].HomeButton);
    }

    [Fact]
    public void UnknownControllerDoesNotReplaceSoleExistingRegistration()
    {
        var registry = new ControllerRegistry(new SettingsDocument
        {
            Controllers = [new()
            {
                StableId = "old", DisplayName = "Old controller", RegistrationOrder = 1,
                PreferredVJoyId = 4
            }]
        });

        bool changed = ControllerRegistrationReconciler.Reconcile(registry,
            [Controller("new", "Different controller")], DateTimeOffset.UtcNow);

        Assert.True(changed);
        Assert.Collection(registry.Snapshot.Controllers,
            old =>
            {
                Assert.Equal("old", old.StableId);
                Assert.Equal((uint)4, old.PreferredVJoyId);
            },
            added =>
            {
                Assert.Equal("new", added.StableId);
                Assert.Equal(2, added.RegistrationOrder);
            });
    }

    [Fact]
    public void UnknownControllerWaitsForExplicitAddWhenAutoFindIsOff()
    {
        var registry = new ControllerRegistry(new SettingsDocument
        {
            Application = new() { AutomaticallyFindControllers = false }
        });

        bool changed = ControllerRegistrationReconciler.Reconcile(registry,
            [Controller("new", "Different controller")], DateTimeOffset.UtcNow);

        Assert.False(changed);
        Assert.Empty(registry.Snapshot.Controllers);
    }

    [Fact]
    public void UnknownControllerDoesNotInheritAnUnrelatedExclusion()
    {
        var registry = new ControllerRegistry(new SettingsDocument
        {
            ExcludedControllerIds = ["removed"]
        });

        bool changed = ControllerRegistrationReconciler.Reconcile(registry,
            [Controller("new", "Different controller")], DateTimeOffset.UtcNow);

        Assert.True(changed);
        Assert.Contains("removed", registry.Snapshot.ExcludedControllerIds);
        Assert.Equal("new", Assert.Single(registry.Snapshot.Controllers).StableId);
    }

    private static DiscoveredFireController Controller(string id, string name) =>
        new(new(id, name, 0x1949, 0x0402),
            [new PhysicalDeviceEndpoint($@"\\?\HID#{id}", 1, 5)], true);
}
