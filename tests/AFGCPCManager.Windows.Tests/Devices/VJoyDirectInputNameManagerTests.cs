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

        Assert.Equal("Lowest output (Corrected)", written);
        Assert.Equal(new("Lowest output (Corrected)", true), result);
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

        Assert.Equal("First (Corrected)", written);
    }

    [Fact]
    public void MatchingNameDoesNotRewriteRegistry()
    {
        int writes = 0;
        var manager = new VJoyDirectInputNameManager(
            () => "Amazon Fire Game Controller (Corrected)", _ => writes++);

        VJoyDisplayNameUpdate? result = manager.Synchronize(
            [Controller("Amazon Fire Game Controller", 1, 1)]);

        Assert.Equal(0, writes);
        Assert.Equal(new("Amazon Fire Game Controller (Corrected)", false), result);
    }

    [Theory]
    [InlineData("Amazon Fire Game Controller", "Amazon Fire Game Controller (Corrected)")]
    [InlineData(" Amazon Fire Game Controller (Corrected) ", "Amazon Fire Game Controller (Corrected)")]
    [InlineData("Pad (Corrected) (Corrected)", "Pad (Corrected)")]
    [InlineData("Pad\0Name", "PadName (Corrected)")]
    public void CorrectedNameIsCleanAndIdempotent(string original, string expected) =>
        Assert.Equal(expected, VJoyDirectInputNameManager.BuildCorrectedName(original));

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
