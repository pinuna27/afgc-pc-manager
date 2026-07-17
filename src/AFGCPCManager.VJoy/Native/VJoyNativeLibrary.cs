using System.Runtime.InteropServices;

namespace AFGCPCManager.VJoy.Native;

internal sealed class VJoyNativeLibrary : IVJoyNativeApi
{
    private readonly nint _handle;
    private readonly EnabledDelegate _enabled;
    private readonly StatusDelegate _status;
    private readonly CountDelegate _buttons, _povs;
    private readonly AxisExistsDelegate _axisExists;
    private readonly AxisLimitDelegate _axisMin, _axisMax;
    private readonly AcquireDelegate _acquire;
    private readonly UpdateDelegate _update;
    private readonly DeviceDelegate _reset, _relinquish;
    private bool _disposed;

    private VJoyNativeLibrary(string path)
    {
        _handle = NativeLibrary.Load(path);
        try
        {
            _enabled = Load<EnabledDelegate>("vJoyEnabled");
            _status = Load<StatusDelegate>("GetVJDStatus");
            _buttons = Load<CountDelegate>("GetVJDButtonNumber");
            _povs = Load<CountDelegate>("GetVJDContPovNumber");
            _axisExists = Load<AxisExistsDelegate>("GetVJDAxisExist");
            _axisMin = Load<AxisLimitDelegate>("GetVJDAxisMin");
            _axisMax = Load<AxisLimitDelegate>("GetVJDAxisMax");
            _acquire = Load<AcquireDelegate>("AcquireVJD");
            _update = Load<UpdateDelegate>("UpdateVJD");
            _reset = Load<DeviceDelegate>("ResetVJD");
            _relinquish = Load<DeviceDelegate>("RelinquishVJD");
        }
        catch { NativeLibrary.Free(_handle); throw; }
    }

    public static VJoyNativeLibrary LoadInstalled()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string[] candidates = [Path.Combine(programFiles, "vJoy", Environment.Is64BitProcess ? "x64" : "x86", "vJoyInterface.dll"), Path.Combine(programFiles, "vJoy", "vJoyInterface.dll")];
        string? path = candidates.FirstOrDefault(File.Exists);
        if (path is null) throw new VJoyException("vJoyInterface.dll was not found. Install vJoy 2.2.2 or later.");
        try { return new(path); }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        { throw new VJoyException($"The installed vJoy interface could not be loaded: {ex.Message}"); }
    }

    public bool IsEnabled => _enabled();
    public VJoyDeviceStatus GetStatus(uint id) => _status(id);
    public int GetButtonCount(uint id) => _buttons(id);
    public int GetContinuousPovCount(uint id) => _povs(id);
    public bool HasAxis(uint id, VJoyAxisUsage axis) => _axisExists(id, (uint)axis);
    public bool TryGetAxisRange(uint id, VJoyAxisUsage axis, out int minimum, out int maximum)
    {
        minimum = maximum = 0;
        return _axisMin(id, (uint)axis, out minimum) && _axisMax(id, (uint)axis, out maximum);
    }
    public bool Acquire(uint id) => _acquire(id);
    public bool Update(uint id, ref VJoyPosition position) => _update(id, ref position);
    public void Reset(uint id) => _reset(id);
    public void Relinquish(uint id) => _relinquish(id);

    public void Dispose() { if (!_disposed) { NativeLibrary.Free(_handle); _disposed = true; } }
    private T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_handle, name));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.Bool)] private delegate bool EnabledDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate VJoyDeviceStatus StatusDelegate(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CountDelegate(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.Bool)] private delegate bool AxisExistsDelegate(uint id, uint axis);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.Bool)] private delegate bool AxisLimitDelegate(uint id, uint axis, out int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.Bool)] private delegate bool AcquireDelegate(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.Bool)] private delegate bool UpdateDelegate(uint id, ref VJoyPosition value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DeviceDelegate(uint id);
}
