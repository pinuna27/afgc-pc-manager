namespace AFGCPCManager.Core.Input;

public interface IRawControllerInput : IAsyncDisposable
{
    IAsyncEnumerable<RawControllerReport> ReadReportsAsync(CancellationToken cancellationToken = default);
}
