using AFGCPCManager.Core.Input;
using AFGCPCManager.Core.Output;

namespace AFGCPCManager.VJoy.Tests;

public sealed class VJoyStateConverterTests
{
    private static readonly IReadOnlyDictionary<VirtualAxis, AxisRange> Ranges = Enum.GetValues<VirtualAxis>().ToDictionary(x => x, _ => new AxisRange(0, 32768));

    [Fact]
    public void ConvertsAxesButtonsAndDiagonalPov()
    {
        var state = new VirtualGamepadState(0, 255, 128, 127, 255, 0, DPadDirection.DownRight, 0xffff);
        var result = VJoyStateConverter.Convert(3, state, Ranges);
        Assert.Equal(0, result.AxisX); Assert.Equal(32768, result.AxisY);
        Assert.Equal(16384, result.AxisXRot); Assert.Equal(16384, result.AxisYRot);
        Assert.Equal(32768, result.AxisZ); Assert.Equal(0, result.AxisZRot);
        Assert.Equal(0x7ffu, result.Buttons); Assert.Equal(13500u, result.Hats);
    }

    [Fact]
    public void NeutralPovUsesVJoySentinel()
    {
        var result = VJoyStateConverter.Convert(1, VirtualGamepadState.Neutral, Ranges);
        Assert.Equal(uint.MaxValue, result.Hats);
    }
}
