using AFGCPCManager.HidHide;
using AFGCPCManager.Windows.SingleInstance;
using System.Diagnostics;
using System.Text.Json;

namespace AFGCPCManager.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool recovery = args.Contains("--recover-hidhide", StringComparer.OrdinalIgnoreCase);
        bool exit = args.Contains("--exit", StringComparer.OrdinalIgnoreCase);
        using var instance = AcquireInstance(recovery);
        if (!instance.IsPrimaryInstance)
        {
            if (recovery) return 1;
            InstanceCommand command = exit ? InstanceCommand.Exit : InstanceCommand.Show;
            return instance.SendAsync(command).GetAwaiter().GetResult() ? 0 : 1;
        }
        if (exit) return 0;
        if (recovery) return RecoverHidHide();
        if (InstallationRestartPending()) return PromptForRestart();
        ApplicationConfiguration.Initialize(); bool automaticStart = args.Contains("--background", StringComparer.OrdinalIgnoreCase);
        using var context = new TrayApplicationContext(automaticStart); instance.StartServer(context.HandleCommand); Application.Run(context); return 0;
    }
    private static SingleInstanceCoordinator AcquireInstance(bool waitForPrimary)
    {
        var coordinator = new SingleInstanceCoordinator("AFGC-PC-Manager");
        if (!waitForPrimary || coordinator.IsPrimaryInstance) return coordinator;
        coordinator.SendAsync(InstanceCommand.Exit).GetAwaiter().GetResult(); coordinator.Dispose();
        for (int i = 0; i < 50; i++) { Thread.Sleep(100); var retry = new SingleInstanceCoordinator("AFGC-PC-Manager"); if (retry.IsPrimaryInstance) return retry; retry.Dispose(); }
        return new SingleInstanceCoordinator("AFGC-PC-Manager");
    }
    private static int RecoverHidHide()
    {
        try { new HidHideService(new DeviceInstanceResolver(), new HidHideJournalStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AFGC PC Manager", "hidhide-journal.json"))).RecoverOwnedEntriesAsync().GetAwaiter().GetResult(); return 0; }
        catch { return 1; }
    }
    private static bool InstallationRestartPending()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "install-journal.json");
            if (!File.Exists(path)) return false;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("PendingDependencyOperation", out JsonElement pending)
                && pending.ValueKind == JsonValueKind.Object
                && pending.TryGetProperty("RestartRequired", out JsonElement restartRequired)
                && restartRequired.ValueKind == JsonValueKind.True;
        }
        catch { return false; }
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
