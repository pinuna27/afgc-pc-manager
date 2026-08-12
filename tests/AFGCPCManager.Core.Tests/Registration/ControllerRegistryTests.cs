using AFGCPCManager.Core.Devices;
using AFGCPCManager.Core.Mapping;
using AFGCPCManager.Core.Registration;
using AFGCPCManager.Core.Settings;

namespace AFGCPCManager.Core.Tests.Registration;

public sealed class ControllerRegistryTests
{
    private static FireControllerIdentity Identity(string id) => new(id, "Amazon Fire Game Controller", 0x1949, 0x0402);

    [Fact]
    public void RegistrationOrderIsStableAndMonotonic()
    {
        var registry = new ControllerRegistry(new()); DateTimeOffset now = DateTimeOffset.UtcNow;
        Assert.Equal(1, registry.Register(Identity("a"), now).RegistrationOrder);
        Assert.Equal(2, registry.Register(Identity("b"), now).RegistrationOrder);
        Assert.Equal(1, registry.Register(Identity("a"), now.AddMinutes(1)).RegistrationOrder);
    }

    [Fact]
    public void RemovalExcludesAndDeletesOverride()
    {
        var initial = new SettingsDocument { Controllers = [new() { StableId = "a", DisplayName = "Controller", RegistrationOrder = 1 }], Overrides = new() { ["a"] = new() { HomeButton = HomeButtonMode.Disabled } } };
        var registry = new ControllerRegistry(initial);
        Assert.True(registry.RemoveAndExclude("a"));
        Assert.Contains("a", registry.Snapshot.ExcludedControllerIds); Assert.Empty(registry.Snapshot.Overrides);
    }

    [Fact]
    public void ExplicitRegistrationReAddsExcludedController()
    {
        var registry = new ControllerRegistry(new SettingsDocument { ExcludedControllerIds = ["a"] });
        registry.Register(Identity("a"), DateTimeOffset.UtcNow);
        Assert.DoesNotContain("a", registry.Snapshot.ExcludedControllerIds);
    }

    [Fact]
    public void RegisteringRenamedIdentityPreservesAssignmentAndOverride()
    {
        var initial = new SettingsDocument
        {
            Controllers = [new()
            {
                StableId = "a", DisplayName = "Old name", RegistrationOrder = 2,
                PreferredVJoyId = 7, PreferredXInputSlot = 3
            }],
            Overrides = new() { ["a"] = new() { HomeButton = HomeButtonMode.Disabled } }
        };
        var registry = new ControllerRegistry(initial);

        RegisteredController updated = registry.Register(
            new("a", "New name", 0x1949, 0x0402), DateTimeOffset.UtcNow);

        Assert.Equal("New name", updated.DisplayName);
        Assert.Equal(2, updated.RegistrationOrder);
        Assert.Equal((uint)7, updated.PreferredVJoyId);
        Assert.Equal((uint)3, updated.PreferredXInputSlot);
        Assert.Equal(HomeButtonMode.Disabled,
            registry.Snapshot.Overrides["a"].HomeButton);
    }

    [Fact]
    public void IdentityMigrationPreservesOrderAssignmentAndOverride()
    {
        var initial = new SettingsDocument
        {
            Controllers = [new()
            {
                StableId = "legacy", DisplayName = "Controller",
                RegistrationOrder = 3, PreferredVJoyId = 7, PreferredXInputSlot = 2
            }],
            Overrides = new() { ["legacy"] = new() { HomeButton = HomeButtonMode.Disabled } }
        };
        var registry = new ControllerRegistry(initial);

        RegisteredController migrated = registry.MigrateIdentity(
            "legacy", Identity("persistent"), DateTimeOffset.UtcNow);

        Assert.Equal(3, migrated.RegistrationOrder);
        Assert.Equal((uint)7, migrated.PreferredVJoyId);
        Assert.Equal((uint)2, migrated.PreferredXInputSlot);
        Assert.DoesNotContain("legacy", registry.Snapshot.Overrides.Keys);
        Assert.Equal(HomeButtonMode.Disabled,
            registry.Snapshot.Overrides["persistent"].HomeButton);
    }


    [Fact]
    public void OutputAssignmentsAreStoredIndependently()
    {
        var registry = new ControllerRegistry(new SettingsDocument
        {
            Controllers = [new()
            {
                StableId = "a", DisplayName = "Controller", RegistrationOrder = 1
            }]
        });

        Assert.True(registry.SetPreferredVJoyId("a", 7));
        Assert.True(registry.SetPreferredXInputSlot("a", 3));

        RegisteredController saved = Assert.Single(registry.Snapshot.Controllers);
        Assert.Equal((uint)7, saved.PreferredVJoyId);
        Assert.Equal((uint)3, saved.PreferredXInputSlot);
    }

    [Fact]
    public void ExcludedIdentityMigrationKeepsControllerExcludedAfterRePair()
    {
        var registry = new ControllerRegistry(new SettingsDocument
        {
            ExcludedControllerIds = ["legacy"]
        });

        Assert.True(registry.MigrateExcludedIdentity("legacy", "persistent"));

        Assert.DoesNotContain("legacy", registry.Snapshot.ExcludedControllerIds);
        Assert.Contains("persistent", registry.Snapshot.ExcludedControllerIds);
    }
}
