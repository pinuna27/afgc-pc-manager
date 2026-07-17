using AFGCPCManager.Core.Input;

namespace AFGCPCManager.Core.Tests.Input;

public sealed class PhysicalStateAccumulatorTests
{
    [Fact]
    public void CombinesLatestGamepadAndConsumerCollections()
    {
        var accumulator = new PhysicalStateAccumulator();
        var gamepad = new FireGamepadReport(
            1, 2, 3, 4, 5, 6, FireButtons.A, DPadDirection.Right, 96);

        Assert.True(accumulator.Apply(gamepad));
        Assert.True(accumulator.Apply(new FireConsumerReport(ConsumerButtons.Home)));

        Assert.Equal(FireButtons.A, accumulator.Current.Buttons);
        Assert.Equal(ConsumerButtons.Home, accumulator.Current.ConsumerButtons);
        Assert.Equal(5, accumulator.Current.LeftTrigger);
        Assert.Equal(6, accumulator.Current.RightTrigger);
    }

    [Fact]
    public void IdenticalReportsDoNotPublishAChange()
    {
        var accumulator = new PhysicalStateAccumulator();
        var report = new FireConsumerReport(ConsumerButtons.PlayPause);

        Assert.True(accumulator.Apply(report));
        Assert.False(accumulator.Apply(report));
    }

    [Fact]
    public void ResetReturnsToNeutral()
    {
        var accumulator = new PhysicalStateAccumulator();
        accumulator.Apply(new FireConsumerReport(ConsumerButtons.Home));

        Assert.Equal(PhysicalControllerState.Neutral, accumulator.Reset());
        Assert.Equal(PhysicalControllerState.Neutral, accumulator.Current);
    }
}
