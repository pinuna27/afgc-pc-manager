using System.Text.Json;

namespace AFGCPCManager.HidHide;

public sealed class HidHideJournalStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public async Task<HidHideJournal> LoadAsync(CancellationToken cancellationToken = default)
    {
        Exception? primaryError = null;
        if (File.Exists(path))
        {
            try { return await ReadAsync(path, cancellationToken); }
            catch (Exception ex) when (ex is JsonException or InvalidDataException) { primaryError = ex; }
        }

        if (File.Exists(path + ".bak"))
        {
            try { return await ReadAsync(path + ".bak", cancellationToken); }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                throw new InvalidDataException("Both the HidHide ownership journal and its backup are invalid.",
                    primaryError is null ? ex : new AggregateException(primaryError, ex));
            }
        }

        if (primaryError is not null)
            throw new InvalidDataException("The HidHide ownership journal is invalid and no backup is available.", primaryError);
        return new();
    }
    public async Task SaveAsync(HidHideJournal journal, CancellationToken cancellationToken = default)
    {
        journal = ValidateAndNormalize(journal);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); string temporary = path + ".tmp";
        await using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) { await JsonSerializer.SerializeAsync(stream, journal, Options, cancellationToken); await stream.FlushAsync(cancellationToken); }
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
        else File.Move(temporary, path);
    }

    private static async Task<HidHideJournal> ReadAsync(string source, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        HidHideJournal? journal = await JsonSerializer.DeserializeAsync<HidHideJournal>(stream, Options, cancellationToken);
        return ValidateAndNormalize(journal);
    }

    private static HidHideJournal ValidateAndNormalize(HidHideJournal? journal)
    {
        if (journal is null || journal.SchemaVersion is not (1 or 2 or 3)
            || journal.AddedApplicationPaths is null
            || journal.AddedDeviceInstanceIds is null || journal.AddedApplicationPaths.Any(string.IsNullOrWhiteSpace)
            || journal.AddedDeviceInstanceIds.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Value is null
                || x.Value.Any(string.IsNullOrWhiteSpace)))
            throw new InvalidDataException("The HidHide ownership journal is invalid.");

        HashSet<string> pending = journal.SchemaVersion == 1
            ? journal.ActivatedByApplication
                ? new(journal.AddedDeviceInstanceIds.Keys, StringComparer.Ordinal)
                : new(StringComparer.Ordinal)
            : journal.PendingHandleResetControllerIds is null
                || journal.PendingHandleResetControllerIds.Any(string.IsNullOrWhiteSpace)
                || journal.PendingHandleResetControllerIds.Any(
                    id => !journal.AddedDeviceInstanceIds.ContainsKey(id))
                    ? throw new InvalidDataException("The HidHide ownership journal is invalid.")
                    : new(journal.PendingHandleResetControllerIds, StringComparer.Ordinal);
        HashSet<string> disconnected = journal.SchemaVersion < 3
            ? new(StringComparer.Ordinal)
            : journal.HandleResetDisconnectedControllerIds is null
                || journal.HandleResetDisconnectedControllerIds.Any(string.IsNullOrWhiteSpace)
                || journal.HandleResetDisconnectedControllerIds.Any(id => !pending.Contains(id))
                    ? throw new InvalidDataException("The HidHide ownership journal is invalid.")
                    : new(journal.HandleResetDisconnectedControllerIds, StringComparer.Ordinal);

        return journal with
        {
            SchemaVersion = 3,
            AddedApplicationPaths = new(journal.AddedApplicationPaths, StringComparer.OrdinalIgnoreCase),
            AddedDeviceInstanceIds = journal.AddedDeviceInstanceIds.ToDictionary(
                x => x.Key,
                x => new HashSet<string>(x.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.Ordinal),
            PendingHandleResetControllerIds = pending,
            HandleResetDisconnectedControllerIds = disconnected
        };
    }
}
