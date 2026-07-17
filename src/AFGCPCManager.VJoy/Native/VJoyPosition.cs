using System.Runtime.InteropServices;

namespace AFGCPCManager.VJoy.Native;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct VJoyPosition
{
    public byte Device;
    public int Throttle, Rudder, Aileron, AxisX, AxisY, AxisZ;
    public int AxisXRot, AxisYRot, AxisZRot, Slider, Dial, Wheel;
    public int Accelerator, Brake, Clutch, Steering, AxisVX, AxisVY;
    public uint Buttons, Hats, HatsEx1, HatsEx2, HatsEx3;
    public uint ButtonsEx1, ButtonsEx2, ButtonsEx3;
    public int AxisVZ, AxisVBRX, AxisVBRY, AxisVBRZ;
}
