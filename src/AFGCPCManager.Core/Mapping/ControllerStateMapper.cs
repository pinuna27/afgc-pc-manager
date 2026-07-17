using AFGCPCManager.Core.Input;
using AFGCPCManager.Core.Output;

namespace AFGCPCManager.Core.Mapping;

public sealed class ControllerStateMapper
{
    private ConsumerButtons _previousConsumerButtons;

    public MappingResult Map(
        PhysicalControllerState physical,
        ControllerMappingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        ushort buttons = MapStandardButtons(physical.Buttons);
        List<ConsumerAction> actions = [];

        bool home = physical.ConsumerButtons.HasFlag(ConsumerButtons.Home);
        bool gameCircle = physical.Buttons.HasFlag(FireButtons.GameCircle);
        bool rewind = physical.ConsumerButtons.HasFlag(ConsumerButtons.Rewind);
        bool playPause = physical.ConsumerButtons.HasFlag(ConsumerButtons.PlayPause);
        bool fastForward = physical.ConsumerButtons.HasFlag(ConsumerButtons.FastForward);

        if (profile.HomeButton == HomeButtonMode.Guide && home)
            SetButton(ref buttons, 11);
        else if (profile.HomeButton == HomeButtonMode.Original &&
                 Rising(physical.ConsumerButtons, ConsumerButtons.Home))
            actions.Add(ConsumerAction.BrowserHome);

        GameCircleButtonMode gameCircleMode = profile.HomeButton == HomeButtonMode.Guide
            ? profile.GameCircleButton
            : GameCircleButtonMode.Guide;
        if (gameCircleMode == GameCircleButtonMode.Guide && gameCircle)
            SetButton(ref buttons, 11);

        switch (profile.MediaRow)
        {
            case MediaRowMode.Media:
                AddOnRising(actions, physical.ConsumerButtons,
                    ConsumerButtons.Rewind, ConsumerAction.Rewind);
                AddOnRising(actions, physical.ConsumerButtons,
                    ConsumerButtons.PlayPause, ConsumerAction.PlayPause);
                AddOnRising(actions, physical.ConsumerButtons,
                    ConsumerButtons.FastForward, ConsumerAction.FastForward);
                break;
            case MediaRowMode.Navigation:
                if (rewind) SetButton(ref buttons, 7);
                if (playPause) SetButton(ref buttons, 11);
                if (fastForward) SetButton(ref buttons, 8);
                break;
            case MediaRowMode.Disabled:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(profile));
        }

        _previousConsumerButtons = physical.ConsumerButtons;
        return new MappingResult(
            new VirtualGamepadState(
                physical.LeftX,
                physical.LeftY,
                physical.RightX,
                physical.RightY,
                physical.LeftTrigger,
                physical.RightTrigger,
                physical.DPad,
                buttons),
            actions);
    }

    public MappingResult Reset()
    {
        _previousConsumerButtons = ConsumerButtons.None;
        return new MappingResult(VirtualGamepadState.Neutral, []);
    }

    private bool Rising(ConsumerButtons current, ConsumerButtons button) =>
        current.HasFlag(button) && !_previousConsumerButtons.HasFlag(button);

    private void AddOnRising(
        List<ConsumerAction> actions,
        ConsumerButtons current,
        ConsumerButtons button,
        ConsumerAction action)
    {
        if (Rising(current, button))
            actions.Add(action);
    }

    private static ushort MapStandardButtons(FireButtons physical)
    {
        ushort result = 0;
        Copy(physical, FireButtons.A, ref result, 1);
        Copy(physical, FireButtons.B, ref result, 2);
        Copy(physical, FireButtons.X, ref result, 3);
        Copy(physical, FireButtons.Y, ref result, 4);
        Copy(physical, FireButtons.LeftShoulder, ref result, 5);
        Copy(physical, FireButtons.RightShoulder, ref result, 6);
        Copy(physical, FireButtons.Back, ref result, 7);
        Copy(physical, FireButtons.Menu, ref result, 8);
        Copy(physical, FireButtons.LeftThumb, ref result, 9);
        Copy(physical, FireButtons.RightThumb, ref result, 10);
        return result;
    }

    private static void Copy(
        FireButtons physical,
        FireButtons source,
        ref ushort destination,
        int target)
    {
        if (physical.HasFlag(source))
            SetButton(ref destination, target);
    }

    private static void SetButton(ref ushort buttons, int oneBasedButton) =>
        buttons |= (ushort)(1 << (oneBasedButton - 1));
}
