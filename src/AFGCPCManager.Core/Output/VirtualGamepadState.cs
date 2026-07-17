using AFGCPCManager.Core.Devices;
using AFGCPCManager.Core.Input;

namespace AFGCPCManager.Core.Output;

public readonly record struct VirtualGamepadState(
    byte LeftX,
    byte LeftY,
    byte RightX,
    byte RightY,
    byte LeftTrigger,
    byte RightTrigger,
    DPadDirection DPad,
    ushort Buttons)
{
    public static VirtualGamepadState Neutral { get; } = new(
        FireControllerConstants.LeftXCenter,
        FireControllerConstants.LeftYCenter,
        FireControllerConstants.RightXCenter,
        FireControllerConstants.RightYCenter,
        0,
        0,
        DPadDirection.Neutral,
        0);

    public bool IsButtonPressed(int oneBasedButton)
    {
        if (oneBasedButton is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(oneBasedButton));

        return (Buttons & (1 << (oneBasedButton - 1))) != 0;
    }
}
