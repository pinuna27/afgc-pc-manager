namespace AFGCPCManager.VJoy.Native;

internal interface IVJoyNativeApi : IDisposable
{
    bool IsEnabled { get; }
    VJoyDeviceStatus GetStatus(uint id);
    int GetButtonCount(uint id);
    int GetContinuousPovCount(uint id);
    bool HasAxis(uint id, VJoyAxisUsage axis);
    bool TryGetAxisRange(uint id, VJoyAxisUsage axis, out int minimum, out int maximum);
    bool Acquire(uint id);
    bool Update(uint id, ref VJoyPosition position);
    void Reset(uint id);
    void Relinquish(uint id);
}
