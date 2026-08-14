using AFGCPCManager.HidHide;
using AFGCPCManager.App.Settings;
using AFGCPCManager.Core.Devices;
using AFGCPCManager.Windows.Devices;
using AFGCPCManager.Windows.SingleInstance;
using System.Diagnostics;
using System.Text.Json;

namespace AFGCPCManager.App;

internal static class Program
{
    internal const int HidHideRecoveryNoChangesExitCode = 2;

    [STAThread]
    private static int Main(string[] args)
    {
        bool recovery = args.Contains("--recover-hidhide", StringComparer.OrdinalIgnoreCase);
        bool resetLights = args.Contains("--reset-lights", StringComparer.OrdinalIgnoreCase);
        bool exit = args.Contains("--exit", StringComparer.OrdinalIgnoreCase);
        bool maintenance = recovery || resetLights;
        using var instance = AcquireInstance(maintenance);
        if (!instance.IsPrimaryInstance)
        {
            if (maintenance)
                return 1;
            InstanceCommand command = exit ? InstanceCommand.Exit : InstanceCommand.Show;
            return instance.SendAsync(command).GetAwaiter().GetResult() ? 0 : 1;
        }
        if (exit)
            return 0;
        if (resetLights)
            ResetIdentificationLights();
        if (recovery)
            return RecoverHidHide();
        if (maintenance)
            return 0;
        if (InstallationRestartPending())
            return PromptForRestart();

        ApplicationConfiguration.Initialize();
        bool automaticStart = args.Contains(
            "--background", StringComparer.OrdinalIgnoreCase);
        using var context = new TrayApplicationContext(automaticStart);
        instance.StartServer(context.HandleCommand);
        Application.Run(context);
        return 0;
    }
    private static SingleInstanceCoordinator AcquireInstance(bool waitForPrimary)
    {
        var coordinator = new SingleInstanceCoordinator("AFGC-PC-Manager");
        if (!waitForPrimary || coordinator.IsPrimaryInstance)
            return coordinator;
        coordinator.SendAsync(InstanceCommand.Exit).GetAwaiter().GetResult();
        coordinator.Dispose();
        for (int attempt = 0; attempt < 50; attempt++)
        {
            Thread.Sleep(100);
            var retry = new SingleInstanceCoordinator("AFGC-PC-Manager");
            if (retry.IsPrimaryInstance)
                return retry;
            retry.Dispose();
        }
        return new SingleInstanceCoordinator("AFGC-PC-Manager");
    }
    private static int RecoverHidHide()
    {
        try
        {
            HidHideRecoveryResult result = new HidHideService(
                    new DeviceInstanceResolver(),
                    new HidHideJournalStore(AppIdentity.HidHideJournalPath))
                .RecoverOwnedEntriesAsync().GetAwaiter().GetResult();
            return result.Changed ? 0 : HidHideRecoveryNoChangesExitCode;
        }
        catch (Exception ex)
        {
            RuntimeEventLog.Write($"HidHide recovery failed: {ex}");
            return 1;
        }
    }

    private static void ResetIdentificationLights()
    {
        try
        {
            var settingsStore = new JsonSettingsStore(AppIdentity.SettingsPath);
            var settings = settingsStore.LoadAsync().GetAwaiter().GetResult();
            if (!settings.Application.ControlIdentificationLights)
                return;
            IReadOnlyList<DiscoveredFireController> discovered =
                new FireControllerDiscovery().SnapshotAsync().GetAwaiter().GetResult();
            ControllerIdentificationLightResetResult result =
                new ControllerIdentificationLightManager().ResetRegistered(
                    discovered, settings.Controllers);
            if (result.Attempted != result.Succeeded)
                RuntimeEventLog.Write(
                    $"Uninstall reset {result.Succeeded} of {result.Attempted} connected controller light sets.");
        }
        catch (Exception ex)
        {
            // A disconnected controller cannot be reset until it reconnects or power-cycles.
            // Light cleanup is best-effort and must not bypass the visibility safety cleanup.
            RuntimeEventLog.Write($"Controller light reset during uninstall was skipped: {ex}");
        }
    }
    private static bool InstallationRestartPending()
    {
        try
        {
            string path = AppIdentity.InstallJournalPath;
            if (!File.Exists(path))
                return false;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("PendingDependencyOperation", out JsonElement pending)
                && pending.ValueKind == JsonValueKind.Object
                && pending.TryGetProperty("RestartRequired", out JsonElement restartRequired)
                && restartRequired.ValueKind == JsonValueKind.True;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or System.Text.Json.JsonException)
        {
            RuntimeEventLog.Write($"Install restart state could not be read: {ex.Message}");
            return false;
        }
    }
    private static int PromptForRestart()
    {
        ApplicationConfiguration.Initialize();
        DialogResult result = MessageBox.Show(
            "AFGC PC Manager installation is waiting for Windows to restart. The controller bridge cannot start until then.\n\nRestart now?",
            "Restart required", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (result == DialogResult.Yes)
            Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { UseShellExecute = true });
        return 0;
    }
}
