using System.Text.Json;
using AFGCPCManager.Setup.Core.Models;

namespace AFGCPCManager.Setup.Core.Installation;

public sealed class JournalStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public async Task SaveAsync(string path, InstallationJournal journal, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        await using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            await JsonSerializer.SerializeAsync(stream, journal, Options, cancellationToken);
        File.Move(temporary, path, true);
    }
    public async Task<InstallationJournal?> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return null;
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<InstallationJournal>(stream, Options, cancellationToken);
    }
}
