using AFGCPCManager.Core.Devices;

namespace AFGCPCManager.Core.Tests.Devices;

public sealed class ControllerIdentificationLightPatternTests
{
    [Fact]
    public void FirstFourControllersUseOneDistinctLightEach()
    {
        Assert.Equal(0b1000, ControllerIdentificationLightPattern.ForRegistrationOrder(1));
        Assert.Equal(0b0100, ControllerIdentificationLightPattern.ForRegistrationOrder(2));
        Assert.Equal(0b0010, ControllerIdentificationLightPattern.ForRegistrationOrder(3));
        Assert.Equal(0b0001, ControllerIdentificationLightPattern.ForRegistrationOrder(4));
    }

    [Fact]
    public void PatternsProgressFromOneLightToAllFour()
    {
        byte[] masks = Enumerable.Range(1, 15)
            .Select(ControllerIdentificationLightPattern.ForRegistrationOrder).ToArray();

        Assert.Equal(new byte[]
        {
            0b1000, 0b0100, 0b0010, 0b0001,
            0b1100, 0b1010, 0b0110, 0b1001, 0b0101, 0b0011,
            0b1110, 0b1101, 0b1011, 0b0111,
            0b1111
        }, masks);
    }

    [Fact]
    public void PatternRepeatsAfterFifteenRegistrationSlots() =>
        Assert.Equal(ControllerIdentificationLightPattern.ForRegistrationOrder(1),
            ControllerIdentificationLightPattern.ForRegistrationOrder(16));

    [Fact]
    public void RegistrationOrderMustBePositive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ControllerIdentificationLightPattern.ForRegistrationOrder(0));
}
