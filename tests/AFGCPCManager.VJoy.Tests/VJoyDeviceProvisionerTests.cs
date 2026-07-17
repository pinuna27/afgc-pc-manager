using AFGCPCManager.Core.Output;

namespace AFGCPCManager.VJoy.Tests;

public sealed class VJoyDeviceProvisionerTests
{
    [Fact]
    public void KeepsExistingCompatibleFreeDevice()
    {
        VJoyProvisioningPlan plan = VJoyDeviceProvisioner.SelectPlan(
            [Device(1, OutputDeviceStatus.Free, compatible: true), Device(2, OutputDeviceStatus.Missing)]);
        Assert.Equal(new(1, VJoyProvisioningAction.None), plan);
        Assert.Empty(plan.Arguments);
    }

    [Fact]
    public void CreatesLowestMissingDeviceWithoutForce()
    {
        VJoyProvisioningPlan plan = VJoyDeviceProvisioner.SelectPlan(
            [Device(1, OutputDeviceStatus.Busy), Device(2, OutputDeviceStatus.Missing), Device(3, OutputDeviceStatus.Missing)]);
        Assert.Equal(VJoyProvisioningAction.Create, plan.Action);
        Assert.Equal("2", plan.Arguments[0]);
        Assert.DoesNotContain("-f", plan.Arguments);
    }

    [Fact]
    public void ReconfiguresFreeIncompatibleDeviceButNeverBusyDevice()
    {
        VJoyProvisioningPlan plan = VJoyDeviceProvisioner.SelectPlan(
            [Device(1, OutputDeviceStatus.Busy), Device(2, OutputDeviceStatus.Free)]);
        Assert.Equal(new(2, VJoyProvisioningAction.Reconfigure), plan);
        Assert.Contains("-f", plan.Arguments);
        Assert.Equal(["2", "-f", "-a", "x", "y", "z", "rx", "ry", "rz", "-b", "11", "-p", "1"], plan.Arguments);
    }

    [Fact]
    public void RefusesToModifyWhenEveryDeviceIsBusy()
    {
        Assert.Throws<InvalidOperationException>(() => VJoyDeviceProvisioner.SelectPlan(
            [Device(1, OutputDeviceStatus.Busy), Device(2, OutputDeviceStatus.Busy)]));
    }

    [Fact]
    public void ExpandsToRequestedCountWithoutTouchingBusyDevices()
    {
        IReadOnlyList<VJoyProvisioningPlan> plans = VJoyDeviceProvisioner.SelectPlans(
            [Device(1, OutputDeviceStatus.Free, compatible: true), Device(2, OutputDeviceStatus.Busy),
             Device(3, OutputDeviceStatus.Missing), Device(4, OutputDeviceStatus.Free)], 3);

        Assert.Equal([new(1, VJoyProvisioningAction.None), new(3, VJoyProvisioningAction.Create),
            new(4, VJoyProvisioningAction.Reconfigure)], plans);
    }

    [Fact]
    public void RejectsRequestBeyondAvailableNonBusyDevices()
    {
        Assert.Throws<InvalidOperationException>(() => VJoyDeviceProvisioner.SelectPlans(
            [Device(1, OutputDeviceStatus.Free, compatible: true), Device(2, OutputDeviceStatus.Busy)], 2));
    }

    private static OutputDeviceInfo Device(uint id, OutputDeviceStatus status, bool compatible = false) =>
        new(id, status, compatible ? new(new Dictionary<VirtualAxis, AxisRange>(), 11, 1) : null);
}
