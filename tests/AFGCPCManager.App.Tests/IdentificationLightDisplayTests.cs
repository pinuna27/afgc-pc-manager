using AFGCPCManager.App;
using AFGCPCManager.Core.Devices;
using Xunit;

namespace AFGCPCManager.App.Tests;

public sealed class IdentificationLightDisplayTests
{
    [Fact]
    public void ControllerOneKeepsReversedPhysicalLightAndDisplaysItOnTheLeft()
    {
        byte physicalMask = ControllerIdentificationLightPattern.ForRegistrationOrder(1);

        Assert.Equal(0b1000, physicalMask);
        Assert.Equal("■ — — —", IdentificationLightDisplay.Format(physicalMask));
    }

    [Theory]
    [InlineData(0b0001, "— — — ■")]
    [InlineData(0b0010, "— — ■ —")]
    [InlineData(0b0100, "— ■ — —")]
    [InlineData(0b1000, "■ — — —")]
    public void Format_DisplaysLightsFromFrontToBack(byte mask, string expected)
    {
        Assert.Equal(expected, IdentificationLightDisplay.Format(mask));
    }

    [Fact]
    public void Format_WhenLightControlIsDisabled_ReturnsNotControlled()
    {
        Assert.Equal("Not controlled", IdentificationLightDisplay.Format(null));
    }
}
