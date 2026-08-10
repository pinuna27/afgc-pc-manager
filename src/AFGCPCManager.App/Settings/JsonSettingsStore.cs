using System.Text.Json;
using System.Text.Json.Serialization;
using AFGCPCManager.Core.Settings;

namespace AFGCPCManager.App.Settings;

internal sealed class JsonSettingsStore(string path) : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<SettingsDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Exception? primaryError = null;
            try { return await ReadAsync(path, cancellationToken) ?? new(); }
            catch (Exception ex) when (ex is JsonException or InvalidDataException) { primaryError = ex; }
            try { return await ReadAsync(path + ".bak", cancellationToken) ?? throw primaryError; }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or FileNotFoundException)
            { throw new InvalidDataException("Both the settings file and its backup are invalid.", new AggregateException(primaryError!, ex)); }
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(SettingsDocument document, CancellationToken cancellationToken = default)
    {
        SettingsValidator.Validate(document);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + ".tmp";
            await using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            { await JsonSerializer.SerializeAsync(stream, document, Options, cancellationToken); await stream.FlushAsync(cancellationToken); }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
            else File.Move(temporary, path);
        }
        finally { _gate.Release(); }
    }

    private static async Task<SettingsDocument?> ReadAsync(string source, CancellationToken cancellationToken)
    {
        if (!File.Exists(source)) return null;
        await using FileStream stream = new(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        SettingsDocument? document = await JsonSerializer.DeserializeAsync<SettingsDocument>(stream, Options, cancellationToken);
        return document is null ? throw new InvalidDataException("Settings are empty.") : SettingsValidator.Validate(document);
    }
}
