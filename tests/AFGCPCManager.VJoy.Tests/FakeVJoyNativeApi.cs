using AFGCPCManager.VJoy.Native;

namespace AFGCPCManager.VJoy.Tests;

internal sealed class FakeVJoyNativeApi : IVJoyNativeApi
{
    public bool IsEnabled { get; set; } = true;
    public Dictionary<uint, VJoyDeviceStatus> Statuses { get; } = [];
    public HashSet<VJoyAxisUsage> Axes { get; } = Enum.GetValues<VJoyAxisUsage>().ToHashSet();
    public int Buttons { get; set; } = 11;
    public int Povs { get; set; } = 1;
    public List<uint> Acquired { get; } = [];
    public List<uint> Relinquished { get; } = [];
    public List<VJoyPosition> Updates { get; } = [];
    public bool UpdateResult { get; set; } = true;
    public VJoyDeviceStatus GetStatus(uint id) => Statuses.GetValueOrDefault(id, VJoyDeviceStatus.Missing);
    public int GetButtonCount(uint id) => Buttons;
    public int GetContinuousPovCount(uint id) => Povs;
    public bool HasAxis(uint id, VJoyAxisUsage axis) => Axes.Contains(axis);
    public bool TryGetAxisRange(uint id, VJoyAxisUsage axis, out int minimum, out int maximum) { minimum = 0; maximum = 32768; return true; }
    public bool Acquire(uint id) { Acquired.Add(id); return true; }
    public bool Update(uint id, ref VJoyPosition position) { Updates.Add(position); return UpdateResult; }
    public void Reset(uint id) { }
    public void Relinquish(uint id) => Relinquished.Add(id);
    public void Dispose() { }
}
