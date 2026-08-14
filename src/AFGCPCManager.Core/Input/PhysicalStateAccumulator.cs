namespace AFGCPCManager.Core.Input;

public sealed class PhysicalStateAccumulator
{
    public PhysicalControllerState Current { get; private set; } =
        PhysicalControllerState.Neutral;

    public bool Apply(FireGamepadReport report)
    {
        PhysicalControllerState next = Current with
        {
            LeftX = report.LeftX,
            LeftY = report.LeftY,
            RightX = report.RightX,
            RightY = report.RightY,
            LeftTrigger = report.LeftTrigger,
            RightTrigger = report.RightTrigger,
            Buttons = report.Buttons,
            DPad = report.DPad,
            BatteryPercentage = report.BatteryPercentage
        };
        return Replace(next);
    }

    public bool Apply(FireConsumerReport report) =>
        Replace(Current with { ConsumerButtons = report.Buttons });

    public PhysicalControllerState Reset()
    {
        Current = PhysicalControllerState.Neutral;
        return Current;
    }

    private bool Replace(PhysicalControllerState next)
    {
        if (next == Current)
            return false;

        Current = next;
        return true;
    }
}
