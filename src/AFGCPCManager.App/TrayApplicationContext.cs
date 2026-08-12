using AFGCPCManager.Windows.SingleInstance;
using AFGCPCManager.Core.Updates;
using System.Diagnostics;
using AFGCPCManager.UI;

namespace AFGCPCManager.App;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly BridgeRuntime _runtime = new(); private readonly MainForm _main; private readonly NotifyIcon _tray; private bool _exiting;
    private IReadOnlyList<UpdateCheckResult.Available> _pendingUpdates = [];
    public TrayApplicationContext(bool automaticStart)
    {
        _main = new MainForm(_runtime);
        // CreateControl() alone does not guarantee a native handle for a Form
        // that has never been shown. Background startup therefore had no UI
        // thread target for BeginInvoke, and a later single-instance Show command
        // was silently dropped. Force the handle while still on the UI thread.
        _ = _main.Handle;
        var menu = new ContextMenuStrip { Font = UiTheme.BodyFont, ShowImageMargin = false, BackColor = UiTheme.Surface, ForeColor = UiTheme.Text, Padding = new Padding(4) }; menu.Items.Add("Open AFGC PC Manager", null, (_, _) => ShowMain()); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Exit", null, async (_, _) => await ExitAsync());
        _tray = new NotifyIcon { Text = "AFGC PC Manager", Icon = AfgcIcon.CreateIcon(), Visible = false, ContextMenuStrip = menu }; _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };
        _tray.BalloonTipClicked += (_, _) => OpenPendingUpdate();
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
            _pendingUpdates = updates;
            UpdateCheckResult.Available? manager = updates.FirstOrDefault(
                update => update.Component == ReleaseComponent.AfgcPcManager);
            string components = string.Join(", ", updates.Select(update => update.Component switch
            {
                ReleaseComponent.AfgcPcManager => "AFGC PC Manager",
                ReleaseComponent.VJoy => "vJoy",
                ReleaseComponent.ViGEmBus => "ViGEmBus",
                ReleaseComponent.HidHide => "HidHide",
                _ => update.Component.ToString()
            }));
            _tray.BalloonTipTitle = manager is null
                ? "Updates available"
                : "AFGC PC Manager update available";
            _tray.BalloonTipText = manager is null
                ? $"{components}. Click to view the release."
                : $"Version {manager.Latest} is ready. Click to install it.";
            _tray.ShowBalloonTip(8000);
        });
    }
    private void OpenPendingUpdate()
    {
        UpdateCheckResult.Available? manager = _pendingUpdates.FirstOrDefault(
            update => update.Component == ReleaseComponent.AfgcPcManager);
        if (manager is not null)
        {
            try { ManagerUpdateLauncher.PromptAndStart(_main, manager, AppContext.BaseDirectory); }
            catch (Exception ex)
            {
                MessageBox.Show(_main,
                    $"The update could not be started.\n\n{ex.Message}\n\n" +
                    $"You can download it manually from:\n{manager.ReleasePage}",
                    "AFGC PC Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return;
        }
        UpdateCheckResult.Available? first = _pendingUpdates.FirstOrDefault();
        if (first is not null)
            Process.Start(new ProcessStartInfo(first.ReleasePage.AbsoluteUri)
                { UseShellExecute = true });
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
