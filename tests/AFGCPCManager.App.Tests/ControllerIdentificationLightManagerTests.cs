using AFGCPCManager.Core.Devices;
using Xunit;

namespace AFGCPCManager.App.Tests;

public sealed class ControllerIdentificationLightManagerTests
{
    [Fact]
    public void DisabledModeNeverWritesIncludingAfterEnabledModeWasApplied()
    {
        int writes = 0;
        var manager = new ControllerIdentificationLightManager((_, _) =>
        {
            writes++;
            return true;
        });
        DiscoveredFireController device = Controller("one", "path");
        RegisteredController registration = Registration("one", 1);

        manager.Reconcile(true, [device], [registration], _ => { });
        manager.Reconcile(false, [device], [registration], _ => { });

        Assert.Equal(1, writes);
    }

    [Fact]
    public void EnabledModeAppliesStablePatternOnlyOnceForSameConnection()
    {
        var writes = new List<(string[] Paths, byte Mask)>();
        var manager = new ControllerIdentificationLightManager((paths, mask) =>
        {
            writes.Add((paths.ToArray(), mask));
            return true;
        });
        DiscoveredFireController device = Controller("two", "b", "a");
        RegisteredController registration = Registration("two", 2);

        manager.Reconcile(true, [device], [registration], _ => { });
        manager.Reconcile(true, [device], [registration], _ => { });

        (string[] paths, byte mask) = Assert.Single(writes);
        Assert.Equal(["a", "b"], paths);
        Assert.Equal(0b0100, mask);
    }

    [Fact]
    public void ChangedEndpointSetAndReconnectBothReapplyPattern()
    {
        int writes = 0;
        var manager = new ControllerIdentificationLightManager((_, _) =>
        {
            writes++;
            return true;
        });
        RegisteredController registration = Registration("one", 1);

        manager.Reconcile(true, [Controller("one", "old")], [registration], _ => { });
        manager.Reconcile(true, [Controller("one", "new")], [registration], _ => { });
        manager.Reconcile(true, [], [registration], _ => { });
        manager.Reconcile(true, [Controller("one", "new")], [registration], _ => { });

        Assert.Equal(3, writes);
    }

    [Fact]
    public void FailedWriteRetriesWithoutRepeatingFailureEvent()
    {
        int attempts = 0;
        var events = new List<string>();
        var manager = new ControllerIdentificationLightManager((_, _) => ++attempts >= 2);
        DiscoveredFireController device = Controller("one", "path");
        RegisteredController registration = Registration("one", 1);

        manager.Reconcile(true, [device], [registration], events.Add);
        manager.Reconcile(true, [device], [registration], events.Add);
        manager.Reconcile(true, [device], [registration], events.Add);

        Assert.Equal(2, attempts);
        Assert.Equal(2, events.Count);
        Assert.Contains("Could not apply", events[0]);
        Assert.Contains("Applied", events[1]);
    }

    private static DiscoveredFireController Controller(string id, params string[] paths) =>
        new(new(id, "Controller", 0x1949, 0x0402), paths.Select(path =>
            new PhysicalDeviceEndpoint(path, 1, 5)).ToArray(), true);

    private static RegisteredController Registration(string id, int order) => new()
    {
        StableId = id,
        DisplayName = "Controller",
        RegistrationOrder = order
    };
}
