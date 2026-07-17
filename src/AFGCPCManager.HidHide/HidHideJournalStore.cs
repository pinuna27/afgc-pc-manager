using System.Text.Json;

namespace AFGCPCManager.HidHide;

public sealed class HidHideJournalStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public async Task<HidHideJournal> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return new();
        await using FileStream stream = File.OpenRead(path);
        HidHideJournal? journal = await JsonSerializer.DeserializeAsync<HidHideJournal>(stream, Options, cancellationToken);
        if (journal is null || journal.SchemaVersion != 1) throw new InvalidDataException("The HidHide ownership journal is invalid.");
        return journal;
    }
    public async Task SaveAsync(HidHideJournal journal, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); string temporary = path + ".tmp";
        await using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) { await JsonSerializer.SerializeAsync(stream, journal, Options, cancellationToken); await stream.FlushAsync(cancellationToken); }
        File.Move(temporary, path, true);
    }
}
