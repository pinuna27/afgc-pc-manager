using AFGCPCManager.Core.Devices;

namespace AFGCPCManager.Core.Input;

public static class FireReportDecoder
{
    public static bool TryDecodeGamepad(
        ReadOnlySpan<byte> report,
        out FireGamepadReport decoded)
    {
        decoded = default;
        if (report.Length != FireControllerConstants.GamepadReportLength ||
            report[0] != FireControllerConstants.GamepadReportId ||
            report[9] > (byte)DPadDirection.UpLeft)
        {
            return false;
        }

        FireButtons buttons = FireButtons.None;
        byte first = report[7];
        byte second = report[8];

        AddIfSet(ref buttons, first, 0x01, FireButtons.A);
        AddIfSet(ref buttons, first, 0x02, FireButtons.B);
        AddIfSet(ref buttons, first, 0x08, FireButtons.X);
        AddIfSet(ref buttons, first, 0x10, FireButtons.Y);
        AddIfSet(ref buttons, first, 0x40, FireButtons.LeftShoulder);
        AddIfSet(ref buttons, first, 0x80, FireButtons.RightShoulder);
        AddIfSet(ref buttons, second, 0x04, FireButtons.Back);
        AddIfSet(ref buttons, second, 0x08, FireButtons.Menu);
        AddIfSet(ref buttons, second, 0x10, FireButtons.GameCircle);
        AddIfSet(ref buttons, second, 0x20, FireButtons.LeftThumb);
        AddIfSet(ref buttons, second, 0x40, FireButtons.RightThumb);

        decoded = new FireGamepadReport(
            report[1], report[2], report[3], report[4], report[5], report[6],
            buttons, (DPadDirection)report[9], report[10]);
        return true;
    }

    public static bool TryDecodeConsumer(
        ReadOnlySpan<byte> report,
        out FireConsumerReport decoded)
    {
        decoded = default;
        if (report.Length != FireControllerConstants.ConsumerReportLength ||
            report[0] != FireControllerConstants.ConsumerReportId)
        {
            return false;
        }

        ConsumerButtons buttons = ConsumerButtons.None;
        byte raw = report[1];
        if ((raw & 0x02) != 0) buttons |= ConsumerButtons.FastForward;
        if ((raw & 0x04) != 0) buttons |= ConsumerButtons.Rewind;
        if ((raw & 0x08) != 0) buttons |= ConsumerButtons.PlayPause;
        if ((raw & 0x10) != 0) buttons |= ConsumerButtons.Home;

        decoded = new FireConsumerReport(buttons);
        return true;
    }

    private static void AddIfSet(
        ref FireButtons destination,
        byte source,
        byte mask,
        FireButtons button)
    {
        if ((source & mask) != 0)
            destination |= button;
    }
}
