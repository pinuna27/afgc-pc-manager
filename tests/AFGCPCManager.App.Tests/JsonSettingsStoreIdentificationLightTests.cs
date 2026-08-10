using AFGCPCManager.App.Settings;
using AFGCPCManager.Core.Settings;
using Xunit;

namespace AFGCPCManager.App.Tests;

public sealed class JsonSettingsStoreIdentificationLightTests
{
    [Fact]
    public async Task ExistingSettingsWithoutLightOptionDefaultToNoLedControl()
    {
        string path = TemporarySettingsPath();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, "{\"schemaVersion\":1}", cancellationToken);

            SettingsDocument loaded = await new JsonSettingsStore(path).LoadAsync(cancellationToken);

            Assert.False(loaded.Application.ControlIdentificationLights);
        }
        finally { DeleteTemporarySettings(path); }
    }

    [Fact]
    public async Task EnabledLightOptionRoundTrips()
    {
        string path = TemporarySettingsPath();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        try
        {
            var store = new JsonSettingsStore(path);
            await store.SaveAsync(new SettingsDocument
            {
                Application = new AppSettings { ControlIdentificationLights = true }
            }, cancellationToken);

            SettingsDocument loaded = await store.LoadAsync(cancellationToken);

            Assert.True(loaded.Application.ControlIdentificationLights);
        }
        finally { DeleteTemporarySettings(path); }
    }

    private static string TemporarySettingsPath()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "AFGCPCManager.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "settings.json");
    }

    private static void DeleteTemporarySettings(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (directory is not null && Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
