namespace AFGCPCManager.Core.Settings;

public interface ISettingsStore
{
    Task<SettingsDocument> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(SettingsDocument document, CancellationToken cancellationToken = default);
}
