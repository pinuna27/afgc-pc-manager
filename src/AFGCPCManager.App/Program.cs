using AFGCPCManager.HidHide;
using AFGCPCManager.Windows.SingleInstance;

namespace AFGCPCManager.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool recovery = args.Contains("--recover-hidhide", StringComparer.OrdinalIgnoreCase);
        using var instance = AcquireInstance(recovery);
        if (!instance.IsPrimaryInstance)
        {
            if (recovery) return 1;
            InstanceCommand command = args.Contains("--exit", StringComparer.OrdinalIgnoreCase) ? InstanceCommand.Exit : InstanceCommand.Show;
            return instance.SendAsync(command).GetAwaiter().GetResult() ? 0 : 1;
        }
        if (recovery) return RecoverHidHide();
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
}
