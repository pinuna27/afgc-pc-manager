using AFGCPCManager.Core.Input;
using AFGCPCManager.Core.Output;

namespace AFGCPCManager.ViGEm.Tests;

public sealed class ViGEmStateConverterTests
{
    [Fact]
    public void ConvertsButtonsTriggersAxesAndDiagonalDPad()
    {
        var state = new VirtualGamepadState(
            0, 0, 255, 255, 17, 231, DPadDirection.UpRight, 0x07ff);

        ViGEmReport report = ViGEmStateConverter.Convert(state);

        Assert.Equal(0xF7F9, report.Buttons);
        Assert.Equal(17, report.LeftTrigger);
        Assert.Equal(231, report.RightTrigger);
        Assert.Equal(short.MinValue, report.LeftThumbX);
        Assert.Equal(short.MaxValue, report.LeftThumbY);
        Assert.Equal(short.MaxValue, report.RightThumbX);
        Assert.Equal(short.MinValue, report.RightThumbY);
    }

    [Fact]
    public void CapturedCentersBecomeExactXInputNeutral()
    {
        ViGEmReport report = ViGEmStateConverter.Convert(VirtualGamepadState.Neutral);

        Assert.Equal(0, report.LeftThumbX);
        Assert.Equal(0, report.LeftThumbY);
        Assert.Equal(0, report.RightThumbX);
        Assert.Equal(0, report.RightThumbY);
        Assert.Equal(0, report.Buttons);
    }
}
