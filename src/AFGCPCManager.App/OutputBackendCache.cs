using AFGCPCManager.Core.Output;
using AFGCPCManager.Core.Settings;

namespace AFGCPCManager.App;

internal sealed class OutputBackendCache(
    Func<GamepadOutputMode, IGamepadOutputBackend> create) : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<GamepadOutputMode, IGamepadOutputBackend> _backends = [];
    private bool _disposed;

    public IGamepadOutputBackend GetOrCreate(GamepadOutputMode mode)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_backends.TryGetValue(mode, out IGamepadOutputBackend? existing))
                return existing;

            IGamepadOutputBackend backend = create(mode);
            _backends.Add(mode, backend);
            return backend;
        }
    }

    public void Remove(GamepadOutputMode mode, IGamepadOutputBackend backend)
    {
        lock (_gate)
        {
            if (!_backends.TryGetValue(mode, out IGamepadOutputBackend? existing)
                || !ReferenceEquals(existing, backend))
                return;
            _backends.Remove(mode);
        }
        backend.Dispose();
    }

    public void Dispose()
    {
        IGamepadOutputBackend[] backends;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            backends = _backends.Values.ToArray();
            _backends.Clear();
        }

        List<Exception>? failures = null;
        foreach (IGamepadOutputBackend backend in backends)
        {
            try { backend.Dispose(); }
            catch (Exception ex) { (failures ??= []).Add(ex); }
        }
        if (failures is { Count: > 0 })
            throw new AggregateException("One or more virtual-output backends could not be released.", failures);
    }
}
