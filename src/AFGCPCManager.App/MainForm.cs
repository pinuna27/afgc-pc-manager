namespace AFGCPCManager.App;

internal sealed class MainForm : Form
{
    private readonly BridgeRuntime _runtime;
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 42, Padding = new(12), AutoEllipsis = true };
    private readonly ListView _controllers = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false };

    public MainForm(BridgeRuntime runtime)
    {
        _runtime = runtime; Text = "AFGC PC Manager"; ClientSize = new(680, 380); MinimumSize = new(540, 300);
        _controllers.Columns.Add("Controller", 300); _controllers.Columns.Add("Status", 150); _controllers.Columns.Add("Output", 130);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new(8), FlowDirection = FlowDirection.RightToLeft };
        var add = new Button { Text = "Add controller", AutoSize = true }; add.Click += async (_, _) => await AddControllerAsync();
        var settings = new Button { Text = "Settings", AutoSize = true }; settings.Click += async (_, _) => await OpenSettingsAsync(null);
        toolbar.Controls.Add(add); toolbar.Controls.Add(settings);
        Controls.Add(_controllers); Controls.Add(toolbar); Controls.Add(_status);
        var rowMenu = new ContextMenuStrip(); rowMenu.Items.Add("Edit controller settings", null, async (_, _) => await OpenSettingsAsync(SelectedId)); rowMenu.Items.Add("Remove controller", null, async (_, _) => await RemoveSelectedAsync());
        _controllers.ContextMenuStrip = rowMenu; rowMenu.Opening += (_, e) => e.Cancel = SelectedId is null;
        runtime.StatusChanged += (_, value) => SetStatus(value); runtime.ControllersChanged += (_, rows) => ApplyRows(rows);
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
    }

    private string? SelectedId => _controllers.SelectedItems.Count == 1 ? _controllers.SelectedItems[0].Name : null;
    public void SetStatus(string value) { if (InvokeRequired) { BeginInvoke(() => SetStatus(value)); return; } _status.Text = value; }
    private void ApplyRows(IReadOnlyList<ControllerRowModel> rows)
    {
        if (InvokeRequired) { BeginInvoke(() => ApplyRows(rows)); return; }
        _controllers.BeginUpdate(); _controllers.Items.Clear();
        foreach (var row in rows)
        {
            var item = new ListViewItem($"Controller {row.RegistrationOrder}: {row.DisplayName}") { Name = row.StableId };
            item.SubItems.Add(row.IsConnected ? (row.VJoyDeviceId is null ? "Needs vJoy" : "Connected") : "Disconnected");
            item.SubItems.Add(row.VJoyDeviceId is uint id ? $"vJoy {id}" : "—"); _controllers.Items.Add(item);
        }
        _controllers.EndUpdate();
    }
    private async Task AddControllerAsync()
    {
        using var dialog = new AddControllerForm(_runtime.GetAddCandidates());
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedStableId is string id) await _runtime.AddControllerAsync(id);
    }
    private async Task RemoveSelectedAsync()
    {
        string? id = SelectedId; if (id is null) return;
        if (MessageBox.Show(this, "Remove this controller from AFGC PC Manager? Bluetooth pairing will not be removed.", "Remove controller", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) await _runtime.RemoveControllerAsync(id);
    }
    private async Task OpenSettingsAsync(string? controllerId)
    {
        using var dialog = new SettingsForm(_runtime.GetSettings(), controllerId, _runtime.GetDiagnostics);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result is not null) await _runtime.SaveSettingsAsync(dialog.Result);
    }
}
