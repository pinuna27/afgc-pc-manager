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
}
