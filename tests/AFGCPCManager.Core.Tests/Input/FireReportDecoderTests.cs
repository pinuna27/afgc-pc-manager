using AFGCPCManager.Core.Input;

namespace AFGCPCManager.Core.Tests.Input;

public sealed class FireReportDecoderTests
{
    public static TheoryData<byte, FireButtons> GamepadButtons => new()
    {
        { 0x01, FireButtons.A },
        { 0x02, FireButtons.B },
        { 0x08, FireButtons.X },
        { 0x10, FireButtons.Y },
        { 0x40, FireButtons.LeftShoulder },
        { 0x80, FireButtons.RightShoulder }
    };

    public static TheoryData<byte, FireButtons> SecondaryButtons => new()
    {
        { 0x04, FireButtons.Back },
        { 0x08, FireButtons.Menu },
        { 0x10, FireButtons.GameCircle },
        { 0x20, FireButtons.LeftThumb },
        { 0x40, FireButtons.RightThumb }
    };

    [Theory]
    [MemberData(nameof(GamepadButtons))]
    public void DecodesPrimaryButtons(byte mask, FireButtons expected)
    {
        byte[] report = NeutralGamepad();
        report[7] = mask;

        Assert.True(FireReportDecoder.TryDecodeGamepad(report, out var decoded));
        Assert.Equal(expected, decoded.Buttons);
    }

    [Theory]
    [MemberData(nameof(SecondaryButtons))]
    public void DecodesSecondaryButtons(byte mask, FireButtons expected)
    {
        byte[] report = NeutralGamepad();
        report[8] = mask;

        Assert.True(FireReportDecoder.TryDecodeGamepad(report, out var decoded));
        Assert.Equal(expected, decoded.Buttons);
    }

    [Theory]
    [InlineData(0, DPadDirection.Neutral)]
    [InlineData(1, DPadDirection.Up)]
    [InlineData(2, DPadDirection.UpRight)]
    [InlineData(3, DPadDirection.Right)]
    [InlineData(4, DPadDirection.DownRight)]
    [InlineData(5, DPadDirection.Down)]
    [InlineData(6, DPadDirection.DownLeft)]
    [InlineData(7, DPadDirection.Left)]
    [InlineData(8, DPadDirection.UpLeft)]
    public void DecodesEveryDPadValue(byte raw, DPadDirection expected)
    {
        byte[] report = NeutralGamepad();
        report[9] = raw;

        Assert.True(FireReportDecoder.TryDecodeGamepad(report, out var decoded));
        Assert.Equal(expected, decoded.DPad);
    }

    [Fact]
    public void KeepsTriggersIndependent()
    {
        byte[] report = NeutralGamepad();
        report[5] = 37;
        report[6] = 241;

        Assert.True(FireReportDecoder.TryDecodeGamepad(report, out var decoded));
        Assert.Equal(37, decoded.LeftTrigger);
        Assert.Equal(241, decoded.RightTrigger);
    }

    [Fact]
    public void DecodesCombinedButtonsAndAxes()
    {
        byte[] report =
        [0x01, 0, 255, 64, 192, 255, 128, 0xDB, 0x7C, 4, 96];

        Assert.True(FireReportDecoder.TryDecodeGamepad(report, out var decoded));
        Assert.Equal(0, decoded.LeftX);
        Assert.Equal(255, decoded.LeftY);
        Assert.Equal(64, decoded.RightX);
        Assert.Equal(192, decoded.RightY);
        Assert.Equal(DPadDirection.DownRight, decoded.DPad);
        Assert.Equal(96, decoded.BatteryPercentage);
        Assert.True(decoded.Buttons.HasFlag(FireButtons.A));
        Assert.True(decoded.Buttons.HasFlag(FireButtons.B));
        Assert.True(decoded.Buttons.HasFlag(FireButtons.X));
        Assert.True(decoded.Buttons.HasFlag(FireButtons.Y));
        Assert.True(decoded.Buttons.HasFlag(FireButtons.LeftShoulder));
        Assert.True(decoded.Buttons.HasFlag(FireButtons.RightShoulder));
        Assert.True(decoded.Buttons.HasFlag(FireButtons.Back));
        Assert.True(decoded.Buttons.HasFlag(FireButtons.Menu));
        Assert.True(decoded.Buttons.HasFlag(FireButtons.GameCircle));
        Assert.True(decoded.Buttons.HasFlag(FireButtons.LeftThumb));
        Assert.True(decoded.Buttons.HasFlag(FireButtons.RightThumb));
    }

    [Theory]
    [InlineData(0x02, ConsumerButtons.FastForward)]
    [InlineData(0x04, ConsumerButtons.Rewind)]
    [InlineData(0x08, ConsumerButtons.PlayPause)]
    [InlineData(0x10, ConsumerButtons.Home)]
    [InlineData(0x1E, ConsumerButtons.FastForward | ConsumerButtons.Rewind |
        ConsumerButtons.PlayPause | ConsumerButtons.Home)]
    public void DecodesConsumerButtons(byte raw, ConsumerButtons expected)
    {
        Assert.True(FireReportDecoder.TryDecodeConsumer(
            [0x02, raw], out var decoded));
        Assert.Equal(expected, decoded.Buttons);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x01 })]
    [InlineData(new byte[] { 0x02, 128, 127, 128, 127, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0x01, 128, 127, 128, 127, 0, 0, 0, 0, 9, 0 })]
    public void RejectsMalformedGamepadReports(byte[] report) =>
        Assert.False(FireReportDecoder.TryDecodeGamepad(report, out _));

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x02 })]
    [InlineData(new byte[] { 0x01, 0 })]
    [InlineData(new byte[] { 0x02, 0, 0 })]
    public void RejectsMalformedConsumerReports(byte[] report) =>
        Assert.False(FireReportDecoder.TryDecodeConsumer(report, out _));

    private static byte[] NeutralGamepad() =>
        [0x01, 128, 127, 128, 127, 0, 0, 0, 0, 0, 0];
}
