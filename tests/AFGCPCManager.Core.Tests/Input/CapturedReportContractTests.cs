using AFGCPCManager.Core.Input;
using AFGCPCManager.Core.Mapping;
using AFGCPCManager.Core.Output;

namespace AFGCPCManager.Core.Tests.Input;

public sealed class CapturedReportContractTests
{
    public static TheoryData<string, int> CapturedGamepadButtons => new()
    {
        { "01807F807F00000100005C", 1 }, // A
        { "01807F807F00000200005C", 2 }, // B
        { "01807F807F00000800005C", 3 }, // X
        { "01807F807F00001000005C", 4 }, // Y
        { "01807F807F00004000005C", 5 }, // L1
        { "01807F807F00008000005C", 6 }, // R1
        { "01807F807F00000004005C", 7 }, // Back
        { "01807F807F00000008005C", 8 }, // Menu
        { "01807F807F00000020005C", 9 }, // L3
        { "01807F807F00000040005C", 10 }, // R3
        { "01807F807F00000010005C", 11 }  // Game Circle
    };

    [Theory]
    [MemberData(nameof(CapturedGamepadButtons))]
    public void CapturedGamepadButtonMapsToExpectedVJoyButton(
        string reportHex, int expectedButton)
    {
        Assert.True(FireReportDecoder.TryDecodeGamepad(
            Convert.FromHexString(reportHex), out FireGamepadReport report));
        var accumulator = new PhysicalStateAccumulator();
        Assert.True(accumulator.Apply(report));

        VirtualGamepadState mapped = new ControllerStateMapper()
            .Map(accumulator.Current, ControllerMappingProfile.Default).Gamepad;

        Assert.True(mapped.IsButtonPressed(expectedButton));
    }

    public static TheoryData<string, DPadDirection> CapturedDPad => new()
    {
        { "01807F807F00000000015C", DPadDirection.Up },
        { "01807F807F00000000025C", DPadDirection.UpRight },
        { "01807F807F00000000035C", DPadDirection.Right },
        { "01807F807F00000000045C", DPadDirection.DownRight },
        { "01807F807F00000000055C", DPadDirection.Down },
        { "01807F807F00000000065C", DPadDirection.DownLeft },
        { "01807F807F00000000075C", DPadDirection.Left },
        { "01807F807F00000000085C", DPadDirection.UpLeft }
    };

    [Theory]
    [MemberData(nameof(CapturedDPad))]
    public void CapturedDPadMapsWithoutReordering(
        string reportHex, DPadDirection expected)
    {
        Assert.True(FireReportDecoder.TryDecodeGamepad(
            Convert.FromHexString(reportHex), out FireGamepadReport report));
        Assert.Equal(expected, report.DPad);
    }

    [Fact]
    public void CapturedAnalogExtremesRemainIndependentAndFullRange()
    {
        byte[] bytes = Convert.FromHexString("0100FF00FFFFFFFF00005C");

        Assert.True(FireReportDecoder.TryDecodeGamepad(bytes, out var report));

        Assert.Equal(0, report.LeftX);
        Assert.Equal(255, report.LeftY);
        Assert.Equal(0, report.RightX);
        Assert.Equal(255, report.RightY);
        Assert.Equal(255, report.LeftTrigger);
        Assert.Equal(255, report.RightTrigger);
    }

    public static TheoryData<string, ConsumerAction> CapturedConsumerButtons => new()
    {
        { "0204", ConsumerAction.Rewind },
        { "0208", ConsumerAction.PlayPause },
        { "0202", ConsumerAction.FastForward }
    };

    [Theory]
    [MemberData(nameof(CapturedConsumerButtons))]
    public void CapturedMediaReportEmitsExactlyOneAction(
        string reportHex, ConsumerAction expected)
    {
        Assert.True(FireReportDecoder.TryDecodeConsumer(
            Convert.FromHexString(reportHex), out FireConsumerReport report));
        var accumulator = new PhysicalStateAccumulator();
        accumulator.Apply(report);
        var mapper = new ControllerStateMapper();

        MappingResult first = mapper.Map(accumulator.Current,
            ControllerMappingProfile.Default);
        MappingResult held = mapper.Map(accumulator.Current,
            ControllerMappingProfile.Default);

        Assert.Equal([expected], first.ConsumerActions);
        Assert.Empty(held.ConsumerActions);
    }

    [Fact]
    public void CapturedHomeAndGameCircleShareGuideWithoutStuckRelease()
    {
        var accumulator = new PhysicalStateAccumulator();
        var mapper = new ControllerStateMapper();
        FireReportDecoder.TryDecodeGamepad(Convert.FromHexString(
            "01807F807F00000010005C"), out var gameCircle);
        FireReportDecoder.TryDecodeConsumer(Convert.FromHexString("0210"), out var home);
        accumulator.Apply(gameCircle);
        accumulator.Apply(home);
        Assert.True(mapper.Map(accumulator.Current, ControllerMappingProfile.Default)
            .Gamepad.IsButtonPressed(11));

        FireReportDecoder.TryDecodeConsumer(Convert.FromHexString("0200"), out home);
        accumulator.Apply(home);
        Assert.True(mapper.Map(accumulator.Current, ControllerMappingProfile.Default)
            .Gamepad.IsButtonPressed(11));
        FireReportDecoder.TryDecodeGamepad(Convert.FromHexString(
            "01807F807F00000000005C"), out gameCircle);
        accumulator.Apply(gameCircle);
        Assert.False(mapper.Map(accumulator.Current, ControllerMappingProfile.Default)
            .Gamepad.IsButtonPressed(11));
    }
}
