namespace AFGCPCManager.ViGEm.Tests;

internal sealed class FakeViGEmClient : IViGEmClientApi
{
    public List<FakeXbox360Target> Targets { get; } = [];
    public bool CanConnect { get; set; } = true;
    public bool Disposed { get; private set; }

    public IXbox360TargetApi CreateXbox360Target()
    {
        var target = new FakeXbox360Target { CanConnect = CanConnect };
        Targets.Add(target);
        return target;
    }

    public void Dispose() => Disposed = true;
}

internal sealed class FakeXbox360Target : IXbox360TargetApi
{
    public bool CanConnect { get; init; } = true;
    public bool Connected { get; private set; }
    public bool Disposed { get; private set; }
    public List<ViGEmReport> Reports { get; } = [];

    public bool TryConnect() => Connected = CanConnect;
    public void Submit(ViGEmReport report) => Reports.Add(report);
    public void Disconnect() => Connected = false;
    public void Dispose() { Disconnect(); Disposed = true; }
}
