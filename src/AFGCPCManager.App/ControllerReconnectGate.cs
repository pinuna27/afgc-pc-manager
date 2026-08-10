namespace AFGCPCManager.App;

internal sealed class ControllerReconnectGate
{
    private const int RequiredAbsentObservations = 2;
    private readonly Dictionary<string, PendingReset> _pending = new(StringComparer.Ordinal);

    public void Require(string stableControllerId, bool disconnectedObserved = false)
    {
        if (!_pending.TryGetValue(stableControllerId, out PendingReset? reset))
            _pending.Add(stableControllerId, new() { ConfirmedDisconnected = disconnectedObserved });
        else if (disconnectedObserved)
            reset.ConfirmedDisconnected = true;
    }

    public bool IsPending(string stableControllerId) => _pending.ContainsKey(stableControllerId);

    public ControllerReconnectObservation Observe(IReadOnlySet<string> connectedControllerIds)
    {
        var ready = new List<string>();
        var newlyDisconnected = new List<string>();
        foreach ((string id, PendingReset reset) in _pending)
        {
            if (!connectedControllerIds.Contains(id))
            {
                reset.ConsecutiveAbsentObservations++;
                if (!reset.ConfirmedDisconnected
                    && reset.ConsecutiveAbsentObservations >= RequiredAbsentObservations)
                {
                    reset.ConfirmedDisconnected = true;
                    newlyDisconnected.Add(id);
                }
                continue;
            }

            if (reset.ConfirmedDisconnected)
                ready.Add(id);
            else
                reset.ConsecutiveAbsentObservations = 0;
        }
        return new(newlyDisconnected, ready);
    }

    public void Complete(string stableControllerId) => _pending.Remove(stableControllerId);
    public void Forget(string stableControllerId) => _pending.Remove(stableControllerId);
    public void Clear() => _pending.Clear();

    private sealed class PendingReset
    {
        public int ConsecutiveAbsentObservations { get; set; }
        public bool ConfirmedDisconnected { get; set; }
    }
}

internal sealed record ControllerReconnectObservation(
    IReadOnlyList<string> NewlyDisconnectedControllerIds,
    IReadOnlyList<string> ReadyControllerIds);
