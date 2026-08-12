using AFGCPCManager.Core.Devices;
using AFGCPCManager.Core.Input;
using AFGCPCManager.Core.Output;

namespace AFGCPCManager.ViGEm;

internal static class ViGEmStateConverter
{
    private static readonly AxisRange XInputAxisRange = new(short.MinValue, short.MaxValue);

    public static ViGEmReport Convert(VirtualGamepadState state)
    {
        ushort buttons = 0;
        Copy(state, 1, ref buttons, 0x1000); // A
        Copy(state, 2, ref buttons, 0x2000); // B
        Copy(state, 3, ref buttons, 0x4000); // X
        Copy(state, 4, ref buttons, 0x8000); // Y
        Copy(state, 5, ref buttons, 0x0100); // LB
        Copy(state, 6, ref buttons, 0x0200); // RB
        Copy(state, 7, ref buttons, 0x0020); // Back
        Copy(state, 8, ref buttons, 0x0010); // Start
        Copy(state, 9, ref buttons, 0x0040); // Left thumb
        Copy(state, 10, ref buttons, 0x0080); // Right thumb
        Copy(state, 11, ref buttons, 0x0400); // Guide
        buttons |= DPadButtons(state.DPad);

        return new(
            buttons,
            state.LeftTrigger,
            state.RightTrigger,
            Stick(state.LeftX, FireControllerConstants.LeftXCenter, invert: false),
            Stick(state.LeftY, FireControllerConstants.LeftYCenter, invert: true),
            Stick(state.RightX, FireControllerConstants.RightXCenter, invert: false),
            Stick(state.RightY, FireControllerConstants.RightYCenter, invert: true));
    }

    private static short Stick(byte value, byte center, bool invert)
    {
        long scaled = AxisScaler.ScaleStickByte(value, center, XInputAxisRange);
        if (invert)
            scaled = scaled switch
            {
                short.MinValue => short.MaxValue,
                short.MaxValue => short.MinValue,
                _ => -scaled
            };
        return checked((short)scaled);
    }

    private static void Copy(VirtualGamepadState state, int source,
        ref ushort destination, ushort target)
    {
        if (state.IsButtonPressed(source)) destination |= target;
    }

    private static ushort DPadButtons(DPadDirection direction) => direction switch
    {
        DPadDirection.Up => 0x0001,
        DPadDirection.UpRight => 0x0001 | 0x0008,
        DPadDirection.Right => 0x0008,
        DPadDirection.DownRight => 0x0002 | 0x0008,
        DPadDirection.Down => 0x0002,
        DPadDirection.DownLeft => 0x0002 | 0x0004,
        DPadDirection.Left => 0x0004,
        DPadDirection.UpLeft => 0x0001 | 0x0004,
        _ => 0
    };
}
