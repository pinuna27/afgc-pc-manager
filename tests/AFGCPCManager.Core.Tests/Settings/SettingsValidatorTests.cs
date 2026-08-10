using AFGCPCManager.Core.Devices;
using AFGCPCManager.Core.Mapping;
using AFGCPCManager.Core.Settings;

namespace AFGCPCManager.Core.Tests.Settings;

public sealed class SettingsValidatorTests
{
    [Fact]
    public void AcceptsDefaultDocument() => Assert.NotNull(SettingsValidator.Validate(new()));

    [Fact]
    public void RejectsMissingRequiredCollections()
    {
        SettingsDocument document = new() { Controllers = null! };

        Assert.Throws<InvalidDataException>(() => SettingsValidator.Validate(document));
    }

    [Fact]
    public void RejectsUndefinedDefaultMappingValue()
    {
        SettingsDocument document = new()
        {
            DefaultMapping = new ControllerMappingProfile { HomeButton = (HomeButtonMode)999 }
        };

        Assert.Throws<InvalidDataException>(() => SettingsValidator.Validate(document));
    }

    [Fact]
    public void RejectsUndefinedOverrideMappingValue()
    {
        const string id = "controller";
        SettingsDocument document = new()
        {
            Controllers = [new RegisteredController { StableId = id, DisplayName = "Fire controller", RegistrationOrder = 1 }],
            Overrides = new() { [id] = new ControllerMappingOverrides { MediaRow = (MediaRowMode)999 } }
        };

        Assert.Throws<InvalidDataException>(() => SettingsValidator.Validate(document));
    }
}
