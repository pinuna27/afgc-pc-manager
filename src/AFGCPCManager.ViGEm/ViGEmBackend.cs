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
    private readonly HashSet<uint> _owned = [];
    private bool _disposed;

    public ViGEmBackend() : this(new NativeViGEmClient()) { }
    internal ViGEmBackend(IViGEmClientApi client) => _client = client;

    public IReadOnlyList<OutputDeviceInfo> EnumerateDevices()
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

    public IGamepadOutputSession? TryAcquire(uint? preferredDeviceId = null)
    {
        ThrowIfDisposed();
        if (preferredDeviceId is > MaximumDeviceId or 0)
            throw new ArgumentOutOfRangeException(nameof(preferredDeviceId));

        IEnumerable<uint> ids = Enumerable.Range(1, (int)MaximumDeviceId)
            .Select(value => (uint)value);
        if (preferredDeviceId is uint preferred)
            ids = ids.OrderBy(id => id == preferred ? 0 : 1).ThenBy(id => id);

        uint? id = ids.FirstOrDefault(candidate => !_owned.Contains(candidate));
        if (id is null or 0) return null;
        IXbox360TargetApi target = _client.CreateXbox360Target();
        try
        {
            if (!target.TryConnect())
            {
                target.Dispose();
                return null;
            }
            target.Submit(ViGEmStateConverter.Convert(VirtualGamepadState.Neutral));
            _owned.Add(id.Value);
            return new ViGEmOutputSession(target, id.Value, () => _owned.Remove(id.Value));
        }
        catch
        {
            target.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_owned.Count > 0)
            throw new InvalidOperationException(
                "ViGEm output sessions must be disposed before their backend.");
        _client.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
