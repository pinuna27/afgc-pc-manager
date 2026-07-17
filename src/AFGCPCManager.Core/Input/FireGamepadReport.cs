namespace AFGCPCManager.Core.Input;

public readonly record struct FireGamepadReport(
    byte LeftX,
    byte LeftY,
    byte RightX,
    byte RightY,
    byte LeftTrigger,
    byte RightTrigger,
    FireButtons Buttons,
    DPadDirection DPad,
    byte BatteryCandidate);
