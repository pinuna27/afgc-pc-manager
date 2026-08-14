using AFGCPCManager.Core.Mapping;
using AFGCPCManager.Core.Settings;
using Xunit;

namespace AFGCPCManager.App.Tests;

public sealed class BridgeRuntimeSettingsChangeTests
{
    [Fact]
    public async Task ConcurrentShutdownSharesOneSafeCompletion()
    {
        var runtime = new BridgeRuntime();

        await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(_ => runtime.DisposeAsync().AsTask()));

        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = runtime.StartAsync();
        });
    }

    [Fact]
    public void IdentificationLightOnlyChangeKeepsRunningBridgeAlive()
    {
        var before = new SettingsDocument();
        SettingsDocument after = before with
        {
            Application = before.Application with
            {
                ControlIdentificationLights = true
            }
        };

        Assert.False(BridgeRuntime.RequiresBridgeRestart(before, after));
    }

    [Fact]
    public void OutputHidingAndMappingChangesStillRestartBridge()
    {
        var before = new SettingsDocument();

        Assert.True(BridgeRuntime.RequiresBridgeRestart(before, before with
        {
            Application = before.Application with
            {
                OutputMode = GamepadOutputMode.XInput
            }
        }));
        Assert.True(BridgeRuntime.RequiresBridgeRestart(before, before with
        {
            Application = before.Application with
            {
                HidePhysicalControllers = !before.Application.HidePhysicalControllers
            }
        }));
        Assert.True(BridgeRuntime.RequiresBridgeRestart(before, before with
        {
            DefaultMapping = before.DefaultMapping with
            {
                HomeButton = HomeButtonMode.Disabled
            }
        }));
    }
}
