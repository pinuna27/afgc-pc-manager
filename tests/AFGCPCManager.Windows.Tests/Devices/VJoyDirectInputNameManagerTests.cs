using AFGCPCManager.Core.Devices;
using AFGCPCManager.Windows.Devices;

namespace AFGCPCManager.Windows.Tests.Devices;

public sealed class VJoyDirectInputNameManagerTests
{
    [Fact]
    public void UsesControllerAssignedToLowestVJoyId()
    {
        string? written = null;
        var manager = new VJoyDirectInputNameManager(() => "vJoy Device", value => written = value);
        RegisteredController[] controllers =
        [
            Controller("First registered", registrationOrder: 1, vJoyId: 3),
            Controller("Lowest output", registrationOrder: 2, vJoyId: 1)
        ];

        VJoyDisplayNameUpdate? result = manager.Synchronize(controllers);

        Assert.Equal("Lowest output (DirectInput)", written);
        Assert.Equal(new("Lowest output (DirectInput)", true), result);
    }

    [Fact]
    public void FallsBackToRegistrationOrderBeforeOutputsAreAssigned()
    {
        string? written = null;
        var manager = new VJoyDirectInputNameManager(() => null, value => written = value);

        manager.Synchronize(
        [
            Controller("Second", registrationOrder: 2),
            Controller("First", registrationOrder: 1)
        ]);

        Assert.Equal("First (DirectInput)", written);
    }

    [Fact]
    public void MatchingNameDoesNotRewriteRegistry()
    {
        int writes = 0;
        var manager = new VJoyDirectInputNameManager(
            () => "Amazon Fire Game Controller (DirectInput)", _ => writes++);

        VJoyDisplayNameUpdate? result = manager.Synchronize(
            [Controller("Amazon Fire Game Controller", 1, 1)]);

        Assert.Equal(0, writes);
        Assert.Equal(new("Amazon Fire Game Controller (DirectInput)", false), result);
    }

    [Theory]
    [InlineData("Amazon Fire Game Controller", "Amazon Fire Game Controller (DirectInput)")]
    [InlineData(" Amazon Fire Game Controller (Corrected) ", "Amazon Fire Game Controller (DirectInput)")]
    [InlineData("Pad (DirectInput) (DirectInput)", "Pad (DirectInput)")]
    [InlineData("Pad (XInput)", "Pad (DirectInput)")]
    [InlineData("Pad\0Name", "PadName (DirectInput)")]
    public void DirectInputNameIsCleanMigratedAndIdempotent(string original, string expected) =>
        Assert.Equal(expected, VJoyDirectInputNameManager.BuildDirectInputName(original));

    [Fact]
    public void NoRegisteredControllersLeavesNameUntouched()
    {
        int reads = 0;
        int writes = 0;
        var manager = new VJoyDirectInputNameManager(() => { reads++; return "vJoy Device"; }, _ => writes++);

        Assert.Null(manager.Synchronize([]));
        Assert.Equal(0, reads);
        Assert.Equal(0, writes);
    }

    private static RegisteredController Controller(string name, int registrationOrder, uint? vJoyId = null) =>
        new()
        {
            StableId = $"controller-{registrationOrder}",
            DisplayName = name,
            RegistrationOrder = registrationOrder,
            PreferredVJoyId = vJoyId
        };
}
