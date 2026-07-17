using AFGCPCManager.Core.Devices;
using AFGCPCManager.Core.Output;

namespace AFGCPCManager.Core.Tests.Output;

public sealed class AxisScalerTests
{
    private static readonly AxisRange VJoyRange = new(0, 32767);

    [Theory]
    [InlineData(0, 128, 0)]
    [InlineData(128, 128, 16384)]
    [InlineData(255, 128, 32767)]
    [InlineData(0, 127, 0)]
    [InlineData(127, 127, 16384)]
    [InlineData(255, 127, 32767)]
    public void StickScalingPreservesEndpointsAndCapturedCenters(
        byte value,
        byte center,
        long expected) =>
        Assert.Equal(expected, AxisScaler.ScaleStickByte(value, center, VJoyRange));

    [Fact]
    public void CapturedAxisCentersAllMapToExactNeutral()
    {
        Assert.Equal(VJoyRange.Midpoint,
            AxisScaler.ScaleStickByte(FireControllerConstants.LeftXCenter,
                FireControllerConstants.LeftXCenter, VJoyRange));
        Assert.Equal(VJoyRange.Midpoint,
            AxisScaler.ScaleStickByte(FireControllerConstants.LeftYCenter,
                FireControllerConstants.LeftYCenter, VJoyRange));
        Assert.Equal(VJoyRange.Midpoint,
            AxisScaler.ScaleStickByte(FireControllerConstants.RightXCenter,
                FireControllerConstants.RightXCenter, VJoyRange));
        Assert.Equal(VJoyRange.Midpoint,
            AxisScaler.ScaleStickByte(FireControllerConstants.RightYCenter,
                FireControllerConstants.RightYCenter, VJoyRange));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(255, 32767)]
    public void TriggerScalingIsLinear(byte value, long expected) =>
        Assert.Equal(expected, AxisScaler.ScaleTriggerByte(value, VJoyRange));
}
