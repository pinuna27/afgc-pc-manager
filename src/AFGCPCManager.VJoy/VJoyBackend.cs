using AFGCPCManager.Core.Output;
using AFGCPCManager.VJoy.Native;

namespace AFGCPCManager.VJoy;

public sealed class VJoyBackend : IGamepadOutputBackend
{
    public const uint MaximumDeviceId = 16;
    private readonly IVJoyNativeApi _api;
    private readonly HashSet<uint> _owned = [];
    private bool _disposed;

    public VJoyBackend() : this(VJoyNativeLibrary.LoadInstalled()) { }
    internal VJoyBackend(IVJoyNativeApi api) => _api = api;

    public IReadOnlyList<OutputDeviceInfo> EnumerateDevices()
    {
        ThrowIfDisposed();
        return Enumerable.Range(1, (int)MaximumDeviceId).Select(id => Describe((uint)id)).ToArray();
    }

    public IGamepadOutputSession? TryAcquire(uint? preferredDeviceId = null)
    {
        ThrowIfDisposed();
        if (!_api.IsEnabled) throw new VJoyException("The vJoy driver is not enabled.");
        if (preferredDeviceId is > MaximumDeviceId or 0) throw new ArgumentOutOfRangeException(nameof(preferredDeviceId));

        IEnumerable<uint> ids = Enumerable.Range(1, (int)MaximumDeviceId).Select(x => (uint)x);
        if (preferredDeviceId is uint preferred) ids = ids.OrderBy(id => id == preferred ? 0 : 1).ThenBy(id => id);
        foreach (uint id in ids)
        {
            if (_api.GetStatus(id) != VJoyDeviceStatus.Free || !TryReadCapabilities(id, out var capabilities)) continue;
            if (!_api.Acquire(id)) continue;
            _owned.Add(id);
            var session = new VJoyOutputSession(_api, id, capabilities!.Axes, () => _owned.Remove(id));
            try { session.WriteAsync(VirtualGamepadState.Neutral).GetAwaiter().GetResult(); return session; }
            catch { session.Dispose(); throw; }
        }
        return null;
    }

    private OutputDeviceInfo Describe(uint id)
    {
        var native = _api.GetStatus(id);
        TryReadCapabilities(id, out var capabilities);
        return new(id, native switch { VJoyDeviceStatus.Owned => OutputDeviceStatus.Owned, VJoyDeviceStatus.Free => OutputDeviceStatus.Free, VJoyDeviceStatus.Busy => OutputDeviceStatus.Busy, VJoyDeviceStatus.Missing => OutputDeviceStatus.Missing, _ => OutputDeviceStatus.Unknown }, capabilities);
    }

    private bool TryReadCapabilities(uint id, out VirtualGamepadCapabilities? capabilities)
    {
        var axes = new Dictionary<VirtualAxis, AxisRange>();
        foreach (var pair in AxisMap)
        {
            if (!_api.HasAxis(id, pair.Value) || !_api.TryGetAxisRange(id, pair.Value, out int min, out int max) || max <= min) { capabilities = null; return false; }
            axes[pair.Key] = new(min, max);
        }
        int buttons = _api.GetButtonCount(id), povs = _api.GetContinuousPovCount(id);
        capabilities = new(axes, buttons, povs);
        return buttons >= 11 && povs >= 1;
    }

    private static readonly IReadOnlyDictionary<VirtualAxis, VJoyAxisUsage> AxisMap = new Dictionary<VirtualAxis, VJoyAxisUsage>
    { [VirtualAxis.LeftX] = VJoyAxisUsage.X, [VirtualAxis.LeftY] = VJoyAxisUsage.Y, [VirtualAxis.RightX] = VJoyAxisUsage.Rx, [VirtualAxis.RightY] = VJoyAxisUsage.Ry, [VirtualAxis.LeftTrigger] = VJoyAxisUsage.Z, [VirtualAxis.RightTrigger] = VJoyAxisUsage.Rz };

    public void Dispose() { if (_disposed) return; foreach (uint id in _owned.ToArray()) { _api.Reset(id); _api.Relinquish(id); } _owned.Clear(); _api.Dispose(); _disposed = true; }
    private void ThrowIfDisposed() { ObjectDisposedException.ThrowIf(_disposed, this); }
}
