using AFGCPCManager.Windows.SingleInstance;
using AFGCPCManager.Core.Updates;
using System.Diagnostics;
using AFGCPCManager.UI;

namespace AFGCPCManager.App;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly BridgeRuntime _runtime = new(); private readonly MainForm _main; private readonly NotifyIcon _tray; private bool _exiting;
    public TrayApplicationContext(bool automaticStart)
    {
        _main = new MainForm(_runtime); _main.CreateControl(); var menu = new ContextMenuStrip { Font = UiTheme.BodyFont, ShowImageMargin = false, BackColor = UiTheme.Surface, ForeColor = UiTheme.Text, Padding = new Padding(4) }; menu.Items.Add("Open AFGC PC Manager", null, (_, _) => ShowMain()); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Exit", null, async (_, _) => await ExitAsync());
        _tray = new NotifyIcon { Text = "AFGC PC Manager", Icon = AfgcIcon.CreateIcon(), Visible = false, ContextMenuStrip = menu }; _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };
        _runtime.UpdatesAvailable += OnUpdatesAvailable;
        _main.SetStatus("Starting controller bridge...");
        if (!automaticStart) { _tray.Visible = true; ShowMain(); }
        _ = InitializeAsync(automaticStart);
    }
    private void OnUpdatesAvailable(object? sender, IReadOnlyList<UpdateCheckResult.Available> updates)
    {
        if (!_runtime.GetSettings().Application.ShowNotifications) return;
        PostToUi(() =>
        {
            string components = string.Join(", ", updates.Select(update => update.Component switch
            {
                ReleaseComponent.AfgcPcManager => "AFGC PC Manager",
                ReleaseComponent.VJoy => "vJoy",
                ReleaseComponent.HidHide => "HidHide",
                _ => update.Component.ToString()
            }));
            _tray.BalloonTipTitle = "Updates available"; _tray.BalloonTipText = components; _tray.ShowBalloonTip(5000);
            _tray.BalloonTipClicked += OpenFirst;
            void OpenFirst(object? s, EventArgs e) { _tray.BalloonTipClicked -= OpenFirst; Process.Start(new ProcessStartInfo(updates[0].ReleasePage.AbsoluteUri) { UseShellExecute = true }); }
        });
    }
    private async Task InitializeAsync(bool automaticStart)
    {
        try
        {
            await Task.Run(_runtime.StartAsync);
            if (automaticStart)
                PostToUi(() => _tray.Visible = _runtime.GetSettings().Application.ShowTrayOnAutomaticStart);
        }
        catch (Exception ex) { PostToUi(() => _main.SetStatus($"Controller bridge failed to start — {ex.Message}")); }
    }
    public void HandleCommand(InstanceCommand command)
    {
        PostToUi(async () => { if (command == InstanceCommand.Show) { _tray.Visible = true; ShowMain(); } else await ExitAsync(); });
    }
    private void ShowMain() { _main.Show(); _main.WindowState = FormWindowState.Normal; _main.Activate(); }
    private void PostToUi(Action action)
    {
        if (_main.IsDisposed || _main.Disposing) return;
        try { _main.BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }
    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        _tray.Visible = false;
        try { await _runtime.DisposeAsync(); }
        catch { }
        finally { _tray.Dispose(); _main.Dispose(); ExitThread(); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (!_exiting)
            {
                _exiting = true;
                try { _runtime.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
            }
            _tray.Dispose();
            _main.Dispose();
        }
        base.Dispose(disposing);
    }
}
