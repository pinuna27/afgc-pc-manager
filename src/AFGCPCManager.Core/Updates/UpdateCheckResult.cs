namespace AFGCPCManager.Core.Updates;

public abstract record UpdateCheckResult(ReleaseComponent Component)
{
    public sealed record UpToDate(ReleaseComponent Target, Version Current) : UpdateCheckResult(Target);
    public sealed record Available(ReleaseComponent Target, Version Current, Version Latest, Uri ReleasePage, DateTimeOffset PublishedAt) : UpdateCheckResult(Target);
    public sealed record Failed(ReleaseComponent Target, string Message) : UpdateCheckResult(Target);
}
