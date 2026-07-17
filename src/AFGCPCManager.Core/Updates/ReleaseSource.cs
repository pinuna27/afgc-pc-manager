namespace AFGCPCManager.Core.Updates;

public sealed record ReleaseSource(ReleaseComponent Component, string Owner, string Repository, Version InstalledVersion);
