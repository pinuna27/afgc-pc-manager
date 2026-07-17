namespace AFGCPCManager.Core.Input;

public readonly record struct PhysicalControllerState(
    byte LeftX,
    byte LeftY,
    byte RightX,
    byte RightY,
    byte LeftTrigger,
    byte RightTrigger,
    FireButtons Buttons,
    DPadDirection DPad,
    ConsumerButtons ConsumerButtons,
    byte BatteryCandidate)
{
    public static PhysicalControllerState Neutral { get; } = new(
        LeftX: 128,
        LeftY: 127,
        RightX: 128,
        RightY: 127,
        LeftTrigger: 0,
        RightTrigger: 0,
        Buttons: FireButtons.None,
        DPad: DPadDirection.Neutral,
        ConsumerButtons: ConsumerButtons.None,
        BatteryCandidate: 0);
}
