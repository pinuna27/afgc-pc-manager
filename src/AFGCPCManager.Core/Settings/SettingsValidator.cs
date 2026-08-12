using AFGCPCManager.Core.Mapping;

namespace AFGCPCManager.Core.Settings;

public static class SettingsValidator
{
    public static SettingsDocument Validate(SettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != 1) throw new InvalidDataException($"Unsupported settings schema {document.SchemaVersion}.");
        if (document.Application is null || document.DefaultMapping is null || document.Controllers is null
            || document.ExcludedControllerIds is null || document.Overrides is null)
            throw new InvalidDataException("Settings are missing required values.");
        if (!Enum.IsDefined(document.Application.OutputMode))
            throw new InvalidDataException("Settings contain an invalid virtual controller output mode.");
        if (document.Controllers.Any(x => x is null || string.IsNullOrWhiteSpace(x.StableId) || string.IsNullOrWhiteSpace(x.DisplayName))) throw new InvalidDataException("A registered controller has an invalid identity.");
        if (document.Controllers.GroupBy(x => x.StableId, StringComparer.Ordinal).Any(x => x.Count() > 1)) throw new InvalidDataException("Settings contain duplicate controller identities.");
        if (document.Controllers.GroupBy(x => x.RegistrationOrder).Any(x => x.Count() > 1)) throw new InvalidDataException("Settings contain duplicate registration numbers.");
        if (document.Controllers.Any(x => x.RegistrationOrder < 1
            || x.PreferredVJoyId is 0 or > 16
            || x.PreferredXInputSlot is 0 or > 4))
            throw new InvalidDataException("Controller registration values are outside their valid ranges.");
        if (document.ExcludedControllerIds.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Settings contain an invalid excluded controller identity.");
        ValidateMapping(document.DefaultMapping);
        if (document.Overrides.Any(x => x.Value is null
            || x.Value.HomeButton is { } home && !Enum.IsDefined(home)
            || x.Value.GameCircleButton is { } circle && !Enum.IsDefined(circle)
            || x.Value.MediaRow is { } media && !Enum.IsDefined(media)))
            throw new InvalidDataException("Settings contain an invalid controller mapping override.");
        if (document.Overrides.Keys.Any(id => !document.Controllers.Any(x => x.StableId == id))) throw new InvalidDataException("Settings contain an override for an unregistered controller.");
        return document;
    }

    private static void ValidateMapping(ControllerMappingProfile mapping)
    {
        if (!Enum.IsDefined(mapping.HomeButton)
            || !Enum.IsDefined(mapping.GameCircleButton)
            || !Enum.IsDefined(mapping.MediaRow))
            throw new InvalidDataException("Settings contain an invalid default controller mapping.");
    }
}
