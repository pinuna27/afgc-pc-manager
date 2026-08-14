using AFGCPCManager.Core.Devices;
using AFGCPCManager.Core.Input;
using AFGCPCManager.Core.Output;
using AFGCPCManager.VJoy.Native;

namespace AFGCPCManager.VJoy;

internal static class VJoyStateConverter
{
    internal const uint NeutralPov = uint.MaxValue;

    public static VJoyPosition Convert(uint deviceId, VirtualGamepadState state, IReadOnlyDictionary<VirtualAxis, AxisRange> ranges) => new()
    {
        Device = checked((byte)deviceId),
        AxisX = checked((int)AxisScaler.ScaleStickByte(state.LeftX, FireControllerConstants.LeftXCenter, ranges[VirtualAxis.LeftX])),
        AxisY = checked((int)AxisScaler.ScaleStickByte(state.LeftY, FireControllerConstants.LeftYCenter, ranges[VirtualAxis.LeftY])),
        AxisXRot = checked((int)AxisScaler.ScaleStickByte(state.RightX, FireControllerConstants.RightXCenter, ranges[VirtualAxis.RightX])),
        AxisYRot = checked((int)AxisScaler.ScaleStickByte(state.RightY, FireControllerConstants.RightYCenter, ranges[VirtualAxis.RightY])),
        AxisZ = checked((int)AxisScaler.ScaleTriggerByte(state.LeftTrigger, ranges[VirtualAxis.LeftTrigger])),
        AxisZRot = checked((int)AxisScaler.ScaleTriggerByte(state.RightTrigger, ranges[VirtualAxis.RightTrigger])),
        Buttons = (uint)(state.Buttons & 0x7ff),
        Hats = ToPov(state.DPad),
        HatsEx1 = NeutralPov,
        HatsEx2 = NeutralPov,
        HatsEx3 = NeutralPov
    };

    private static uint ToPov(DPadDirection direction) => direction switch
    {
        DPadDirection.Up => 0,
        DPadDirection.UpRight => 4500,
        DPadDirection.Right => 9000,
        DPadDirection.DownRight => 13500,
        DPadDirection.Down => 18000,
        DPadDirection.DownLeft => 22500,
        DPadDirection.Left => 27000,
        DPadDirection.UpLeft => 31500,
        _ => NeutralPov
    };
}
