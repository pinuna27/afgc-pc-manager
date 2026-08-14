using AFGCPCManager.Core.Output;
using AFGCPCManager.VJoy.Native;

namespace AFGCPCManager.VJoy;

public sealed class VJoyBackend : IGamepadOutputBackend
{
    public const uint MaximumDeviceId = 16;
    private readonly IVJoyNativeApi _api;
    private readonly object _gate = new();
    private readonly HashSet<uint> _owned = [];
    private bool _disposed;
    private bool _apiDisposed;

    public VJoyBackend() : this(VJoyNativeLibrary.LoadInstalled()) { }
    internal VJoyBackend(IVJoyNativeApi api) => _api = api;
    public bool IsDriverEnabled
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                return _api.IsEnabled;
            }
        }
    }

    public IReadOnlyList<OutputDeviceInfo> EnumerateDevices()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return Enumerable.Range(1, (int)MaximumDeviceId)
                .Select(id => Describe((uint)id))
                .ToArray();
        }
    }

    public IGamepadOutputSession? TryAcquire(uint? preferredDeviceId = null)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_api.IsEnabled)
                throw new VJoyException("The vJoy driver is not enabled.");
            if (preferredDeviceId is > MaximumDeviceId or 0)
                throw new ArgumentOutOfRangeException(nameof(preferredDeviceId));

            IEnumerable<uint> ids = Enumerable.Range(1, (int)MaximumDeviceId)
                .Select(value => (uint)value);
            if (preferredDeviceId is uint preferred)
                ids = ids.OrderBy(id => id == preferred ? 0 : 1).ThenBy(id => id);
            foreach (uint id in ids)
            {
                if (_owned.Contains(id)) continue;
                if (_api.GetStatus(id) != VJoyDeviceStatus.Free
                    || !TryReadCapabilities(id, out var capabilities))
                    continue;
                if (!_api.Acquire(id)) continue;
                try
                {
                    var neutral = VJoyStateConverter.Convert(
                        id, VirtualGamepadState.Neutral, capabilities!.Axes);
                    if (!_api.Update(id, ref neutral))
                        throw new VJoyException(
                            $"vJoy rejected an update for device {id}.");
                }
                catch
                {
                    try { _api.Reset(id); }
                    finally { _api.Relinquish(id); }
                    throw;
                }
                _owned.Add(id);
                return new VJoyOutputSession(
                    _api, id, capabilities.Axes, () => Release(id));
            }
            return null;
        }
    }

    private OutputDeviceInfo Describe(uint id)
    {
        var native = _api.GetStatus(id);
        VirtualGamepadCapabilities? capabilities = null;
        bool compatible = native is VJoyDeviceStatus.Free or VJoyDeviceStatus.Owned
            && TryReadCapabilities(id, out capabilities);
        if (!compatible) capabilities = null;
        return new(id, native switch
        {
            VJoyDeviceStatus.Owned => OutputDeviceStatus.Owned,
            VJoyDeviceStatus.Free => OutputDeviceStatus.Free,
            VJoyDeviceStatus.Busy => OutputDeviceStatus.Busy,
            VJoyDeviceStatus.Missing => OutputDeviceStatus.Missing,
            _ => OutputDeviceStatus.Unknown
        }, capabilities);
    }

    private bool TryReadCapabilities(uint id, out VirtualGamepadCapabilities? capabilities)
    {
        var axes = new Dictionary<VirtualAxis, AxisRange>();
        foreach (var pair in AxisMap)
        {
            if (!_api.HasAxis(id, pair.Value)
                || !_api.TryGetAxisRange(id, pair.Value, out int min, out int max)
                || max <= min)
            {
                capabilities = null;
                return false;
            }
            axes[pair.Key] = new(min, max);
        }
        int buttons = _api.GetButtonCount(id);
        int povs = _api.GetContinuousPovCount(id);
        if (buttons < 11 || povs < 1)
        {
            capabilities = null;
            return false;
        }
        capabilities = new(axes, buttons, povs);
        return true;
    }

    private static readonly IReadOnlyDictionary<VirtualAxis, VJoyAxisUsage> AxisMap =
        new Dictionary<VirtualAxis, VJoyAxisUsage>
        {
            [VirtualAxis.LeftX] = VJoyAxisUsage.X,
            [VirtualAxis.LeftY] = VJoyAxisUsage.Y,
            [VirtualAxis.RightX] = VJoyAxisUsage.Rx,
            [VirtualAxis.RightY] = VJoyAxisUsage.Ry,
            [VirtualAxis.LeftTrigger] = VJoyAxisUsage.Z,
            [VirtualAxis.RightTrigger] = VJoyAxisUsage.Rz
        };

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            DisposeApiIfReady();
        }
    }

    private void Release(uint id)
    {
        lock (_gate)
        {
            _owned.Remove(id);
            DisposeApiIfReady();
        }
    }

    private void DisposeApiIfReady()
    {
        if (!_disposed || _apiDisposed || _owned.Count > 0) return;
        _apiDisposed = true;
        _api.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
