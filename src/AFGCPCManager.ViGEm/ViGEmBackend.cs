using AFGCPCManager.Core.Output;

namespace AFGCPCManager.ViGEm;

public sealed class ViGEmBackend : IGamepadOutputBackend
{
    public const uint MaximumDeviceId = 4;
    private static readonly VirtualGamepadCapabilities Capabilities = new(
        new Dictionary<VirtualAxis, AxisRange>
        {
            [VirtualAxis.LeftX] = new(short.MinValue, short.MaxValue),
            [VirtualAxis.LeftY] = new(short.MinValue, short.MaxValue),
            [VirtualAxis.RightX] = new(short.MinValue, short.MaxValue),
            [VirtualAxis.RightY] = new(short.MinValue, short.MaxValue),
            [VirtualAxis.LeftTrigger] = new(byte.MinValue, byte.MaxValue),
            [VirtualAxis.RightTrigger] = new(byte.MinValue, byte.MaxValue)
        }, 11, 1);

    private readonly IViGEmClientApi _client;
    private readonly object _gate = new();
    private readonly HashSet<uint> _owned = [];
    private bool _disposed;
    private bool _clientDisposed;

    public ViGEmBackend() : this(new NativeViGEmClient()) { }
    internal ViGEmBackend(IViGEmClientApi client) => _client = client;

    public IReadOnlyList<OutputDeviceInfo> EnumerateDevices()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return Enumerable.Range(1, (int)MaximumDeviceId)
                .Select(id => new OutputDeviceInfo((uint)id,
                    _owned.Contains((uint)id)
                        ? OutputDeviceStatus.Owned
                        : OutputDeviceStatus.Free,
                    Capabilities))
                .ToArray();
        }
    }

    public IGamepadOutputSession? TryAcquire(uint? preferredDeviceId = null)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (preferredDeviceId is > MaximumDeviceId or 0)
                throw new ArgumentOutOfRangeException(nameof(preferredDeviceId));

            IEnumerable<uint> ids = Enumerable.Range(1, (int)MaximumDeviceId)
                .Select(value => (uint)value);
            if (preferredDeviceId is uint preferred)
                ids = ids.OrderBy(id => id == preferred ? 0 : 1).ThenBy(id => id);

            uint id = ids.FirstOrDefault(candidate => !_owned.Contains(candidate));
            if (id == 0) return null;
            IXbox360TargetApi target = _client.CreateXbox360Target();
            try
            {
                if (!target.TryConnect())
                {
                    target.Dispose();
                    return null;
                }
                target.Submit(ViGEmStateConverter.Convert(VirtualGamepadState.Neutral));
                _owned.Add(id);
                return new ViGEmOutputSession(target, id,
                    () => Release(id));
            }
            catch (Exception acquisitionFailure)
            {
                try { target.Dispose(); }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(
                        "ViGEm output acquisition and cleanup both failed.",
                        acquisitionFailure, cleanupFailure);
                }
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(acquisitionFailure).Throw();
                throw;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            DisposeClientIfReady();
        }
    }

    private void Release(uint id)
    {
        lock (_gate)
        {
            _owned.Remove(id);
            DisposeClientIfReady();
        }
    }

    private void DisposeClientIfReady()
    {
        if (!_disposed || _clientDisposed || _owned.Count > 0) return;
        _clientDisposed = true;
        _client.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
