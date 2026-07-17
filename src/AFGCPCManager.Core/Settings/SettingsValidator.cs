namespace AFGCPCManager.Core.Settings;

public static class SettingsValidator
{
    public static SettingsDocument Validate(SettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != 1) throw new InvalidDataException($"Unsupported settings schema {document.SchemaVersion}.");
        if (document.Controllers.Any(x => string.IsNullOrWhiteSpace(x.StableId) || string.IsNullOrWhiteSpace(x.DisplayName))) throw new InvalidDataException("A registered controller has an invalid identity.");
        if (document.Controllers.GroupBy(x => x.StableId, StringComparer.Ordinal).Any(x => x.Count() > 1)) throw new InvalidDataException("Settings contain duplicate controller identities.");
        if (document.Controllers.GroupBy(x => x.RegistrationOrder).Any(x => x.Count() > 1)) throw new InvalidDataException("Settings contain duplicate registration numbers.");
        if (document.Controllers.Any(x => x.RegistrationOrder < 1 || x.PreferredVJoyId is 0 or > 16)) throw new InvalidDataException("Controller registration values are outside their valid ranges.");
        if (document.Overrides.Keys.Any(id => !document.Controllers.Any(x => x.StableId == id))) throw new InvalidDataException("Settings contain an override for an unregistered controller.");
        return document;
    }
}
