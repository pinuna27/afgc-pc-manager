using AFGCPCManager.Windows.SingleInstance;
using AFGCPCManager.Core.Updates;
using System.Diagnostics;

namespace AFGCPCManager.App;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly BridgeRuntime _runtime = new(); private readonly MainForm _main; private readonly NotifyIcon _tray; private bool _exiting;
    public TrayApplicationContext(bool automaticStart)
    {
        _main = new MainForm(_runtime); _main.CreateControl(); var menu = new ContextMenuStrip(); menu.Items.Add("Open", null, (_, _) => ShowMain()); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Exit", null, async (_, _) => await ExitAsync());
        _tray = new NotifyIcon { Text = "AFGC PC Manager", Icon = SystemIcons.Application, Visible = false, ContextMenuStrip = menu }; _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };
        _runtime.UpdatesAvailable += OnUpdatesAvailable;
        _main.SetStatus("Starting controller bridge..."); _ = InitializeAsync(automaticStart);
    }
    private void OnUpdatesAvailable(object? sender, IReadOnlyList<UpdateCheckResult.Available> updates)
    {
        if (_main.IsDisposed) return; _main.BeginInvoke(() =>
        {
            _tray.BalloonTipTitle = "AFGC PC Manager updates"; _tray.BalloonTipText = $"{updates.Count} stable update{(updates.Count == 1 ? " is" : "s are")} available."; _tray.ShowBalloonTip(5000);
            _tray.BalloonTipClicked += OpenFirst;
            void OpenFirst(object? s, EventArgs e) { _tray.BalloonTipClicked -= OpenFirst; Process.Start(new ProcessStartInfo(updates[0].ReleasePage.AbsoluteUri) { UseShellExecute = true }); }
        });
    }
    private async Task InitializeAsync(bool automaticStart)
    {
        await _runtime.StartAsync(); bool showTray = !automaticStart || _runtime.GetSettings().Application.ShowTrayOnAutomaticStart; _tray.Visible = showTray; if (!automaticStart) ShowMain();
    }
    public void HandleCommand(InstanceCommand command)
    {
        if (_main.IsDisposed) return; _main.BeginInvoke(async () => { if (command == InstanceCommand.Show) { _tray.Visible = true; ShowMain(); } else await ExitAsync(); });
    }
    private void ShowMain() { _main.Show(); _main.WindowState = FormWindowState.Normal; _main.Activate(); }
    private async Task ExitAsync() { if (_exiting) return; _exiting = true; _tray.Visible = false; await _runtime.DisposeAsync(); _tray.Dispose(); _main.Dispose(); ExitThread(); }
    protected override void Dispose(bool disposing) { if (disposing) { _tray.Dispose(); _main.Dispose(); } base.Dispose(disposing); }
}
