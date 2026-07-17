using AFGCPCManager.Core.Input;
using AFGCPCManager.Core.Mapping;
using AFGCPCManager.Core.Output;

namespace AFGCPCManager.Core.Tests.Mapping;

public sealed class ControllerStateMapperTests
{
    [Fact]
    public void MapsFixedButtonsInContractOrder()
    {
        var mapper = new ControllerStateMapper();
        PhysicalControllerState physical = PhysicalControllerState.Neutral with
        {
            Buttons = FireButtons.A | FireButtons.B | FireButtons.X | FireButtons.Y |
                FireButtons.LeftShoulder | FireButtons.RightShoulder |
                FireButtons.Back | FireButtons.Menu |
                FireButtons.LeftThumb | FireButtons.RightThumb
        };

        MappingResult result = mapper.Map(physical, ControllerMappingProfile.Default);

        for (int button = 1; button <= 10; button++)
            Assert.True(result.Gamepad.IsButtonPressed(button));
    }

    [Fact]
    public void PassesRawAxesAndTriggersThroughCoreUnchanged()
    {
        var mapper = new ControllerStateMapper();
        PhysicalControllerState physical = PhysicalControllerState.Neutral with
        {
            LeftX = 1,
            LeftY = 2,
            RightX = 253,
            RightY = 254,
            LeftTrigger = 37,
            RightTrigger = 241
        };

        VirtualGamepadState output =
            mapper.Map(physical, ControllerMappingProfile.Default).Gamepad;

        Assert.Equal(1, output.LeftX);
        Assert.Equal(2, output.LeftY);
        Assert.Equal(253, output.RightX);
        Assert.Equal(254, output.RightY);
        Assert.Equal(37, output.LeftTrigger);
        Assert.Equal(241, output.RightTrigger);
    }

    [Fact]
    public void TwoGuideSourcesReleaseIndependentlyByAggregateState()
    {
        var mapper = new ControllerStateMapper();
        PhysicalControllerState both = PhysicalControllerState.Neutral with
        {
            Buttons = FireButtons.GameCircle,
            ConsumerButtons = ConsumerButtons.Home
        };

        Assert.True(mapper.Map(both, ControllerMappingProfile.Default)
            .Gamepad.IsButtonPressed(11));
        Assert.True(mapper.Map(both with { ConsumerButtons = ConsumerButtons.None },
            ControllerMappingProfile.Default).Gamepad.IsButtonPressed(11));
        Assert.False(mapper.Map(PhysicalControllerState.Neutral,
            ControllerMappingProfile.Default).Gamepad.IsButtonPressed(11));
    }

    [Fact]
    public void MediaActionsOnlyEmitOnRisingEdge()
    {
        var mapper = new ControllerStateMapper();
        PhysicalControllerState pressed = PhysicalControllerState.Neutral with
        {
            ConsumerButtons = ConsumerButtons.Rewind | ConsumerButtons.PlayPause |
                ConsumerButtons.FastForward
        };

        MappingResult first = mapper.Map(pressed, ControllerMappingProfile.Default);
        MappingResult held = mapper.Map(pressed, ControllerMappingProfile.Default);

        Assert.Equal(
            [ConsumerAction.Rewind, ConsumerAction.PlayPause, ConsumerAction.FastForward],
            first.ConsumerActions);
        Assert.Empty(held.ConsumerActions);
    }

    [Fact]
    public void NavigationModeMapsMediaRowToBackGuideMenu()
    {
        var mapper = new ControllerStateMapper();
        ControllerMappingProfile profile = new() { MediaRow = MediaRowMode.Navigation };
        PhysicalControllerState physical = PhysicalControllerState.Neutral with
        {
            ConsumerButtons = ConsumerButtons.Rewind | ConsumerButtons.PlayPause |
                ConsumerButtons.FastForward
        };

        VirtualGamepadState output = mapper.Map(physical, profile).Gamepad;

        Assert.True(output.IsButtonPressed(7));
        Assert.True(output.IsButtonPressed(11));
        Assert.True(output.IsButtonPressed(8));
    }

    [Fact]
    public void OriginalHomeEmitsBrowserActionOnce()
    {
        var mapper = new ControllerStateMapper();
        ControllerMappingProfile profile = new() { HomeButton = HomeButtonMode.Original };
        PhysicalControllerState pressed = PhysicalControllerState.Neutral with
        {
            ConsumerButtons = ConsumerButtons.Home
        };

        MappingResult first = mapper.Map(pressed, profile);
        MappingResult held = mapper.Map(pressed, profile);

        Assert.Equal([ConsumerAction.BrowserHome], first.ConsumerActions);
        Assert.Empty(held.ConsumerActions);
        Assert.False(first.Gamepad.IsButtonPressed(11));
    }
}
