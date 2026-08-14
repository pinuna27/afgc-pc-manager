using AFGCPCManager.Core.Settings;
using AFGCPCManager.Core.Updates;
using AFGCPCManager.UI;

namespace AFGCPCManager.App;

internal sealed class MainForm : Form
{
    private readonly BridgeRuntime _runtime;
    private readonly AfgcStatusBanner _status = new();
    private readonly DataGridView _controllers = new()
    {
        Dock = DockStyle.Fill,
        // DataGridView's native cell tooltips are topmost windows and can remain
        // visible after this form loses focus or is hidden to the notification area.
        ShowCellToolTips = false
    };
    private readonly Label _emptyState = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiTheme.TextMuted,
        Text = "No controllers are registered yet.\n\nConnect a Fire controller, then choose Add controller."
    };
    private readonly Label _controllerCount = UiTheme.Body("0 registered", muted: true);
    private readonly AfgcButton _update = new("Update available", AfgcButtonKind.Primary)
    {
        Visible = false
    };
    private UpdateCheckResult.Available? _managerUpdate;

    public MainForm(BridgeRuntime runtime)
    {
        _runtime = runtime;
        Text = "AFGC PC Manager";
        ClientSize = new Size(940, 590);
        MinimumSize = new Size(760, 480);

        ConfigureControllerGrid();

        var add = new AfgcButton("Add controller", AfgcButtonKind.Primary);
        add.Click += async (_, _) => await RunUiActionAsync(AddControllerAsync, "add the controller");
        var settings = new AfgcButton("Settings");
        settings.Click += async (_, _) => await RunUiActionAsync(
            () => OpenSettingsAsync(null), "save settings");
        _update.Click += (_, _) => StartManagerUpdate();
        _runtime.UpdatesAvailable += OnUpdatesAvailable;
        Panel header = UiTheme.FormHeader(
            "AFGC PC Manager",
            "Amazon Fire controller bridge",
            UiTheme.ButtonRow(add, settings, _update));

        var cardHeading = UiTheme.SectionHeading("Controllers");
        var cardHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 56,
            Padding = new Padding(18, 0, 18, 0),
            ColumnCount = 2,
            BackColor = UiTheme.Surface
        };
        cardHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cardHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        cardHeading.Anchor = AnchorStyles.Left;
        _controllerCount.Anchor = AnchorStyles.Right;
        cardHeader.Controls.Add(cardHeading, 0, 0);
        cardHeader.Controls.Add(_controllerCount, 1, 0);

        var tableHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };
        tableHost.Controls.Add(_controllers);
        tableHost.Controls.Add(_emptyState);
        var cardBody = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            BackColor = UiTheme.Surface
        };
        cardBody.Controls.Add(tableHost);
        cardBody.Controls.Add(cardHeader);
        var card = new AfgcCard { Dock = DockStyle.Fill, Padding = new Padding(1) };
        card.Controls.Add(cardBody);

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 24, 28, 24),
            BackColor = UiTheme.Canvas
        };
        content.Controls.Add(card);

        Controls.Add(content);
        Controls.Add(_status);
        Controls.Add(UiTheme.Divider());
        Controls.Add(header);

        var rowMenu = new ContextMenuStrip
        {
            Font = UiTheme.BodyFont,
            ShowImageMargin = false,
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.Text
        };
        rowMenu.Items.Add("Edit controller settings", null,
            async (_, _) => await RunUiActionAsync(
                () => OpenSettingsAsync(SelectedId), "save settings"));
        rowMenu.Items.Add(new ToolStripSeparator());
        rowMenu.Items.Add("Remove controller", null,
            async (_, _) => await RunUiActionAsync(RemoveSelectedAsync, "remove the controller"));
        _controllers.ContextMenuStrip = rowMenu;
        rowMenu.Opening += (_, e) => e.Cancel = SelectedId is null;
        _controllers.CellMouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
            _controllers.ClearSelection();
            _controllers.Rows[e.RowIndex].Selected = true;
            _controllers.CurrentCell = _controllers.Rows[e.RowIndex].Cells[0];
        };
        _controllers.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0)
                await RunUiActionAsync(() => OpenSettingsAsync(SelectedId), "save settings");
        };

        runtime.StatusChanged += OnStatusChanged;
        runtime.ControllersChanged += OnControllersChanged;
        FormClosing += (_, e) =>
        {
            if (e.CloseReason != CloseReason.UserClosing) return;
            e.Cancel = true;
            Hide();
        };
        UiTheme.Apply(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _runtime.StatusChanged -= OnStatusChanged;
            _runtime.ControllersChanged -= OnControllersChanged;
            _runtime.UpdatesAvailable -= OnUpdatesAvailable;
        }
        base.Dispose(disposing);
    }

    private void OnStatusChanged(object? sender, string value) => SetStatus(value);

    private void OnControllersChanged(
        object? sender, IReadOnlyList<ControllerRowModel> rows) => ApplyRows(rows);

    private void ConfigureControllerGrid()
    {
        UiTheme.StyleDataGrid(_controllers);
        _controllers.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Controller",
            HeaderText = "CONTROLLER",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 52,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _controllers.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Lights",
            HeaderText = "LIGHTS",
            ToolTipText = $"{IdentificationLightDisplay.OnGlyph} = light on    {IdentificationLightDisplay.OffGlyph} = light off",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 13,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _controllers.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Status",
            HeaderText = "STATUS",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 20,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _controllers.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Output",
            HeaderText = "OUTPUT",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 15,
            MinimumWidth = 100,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
    }

    private string? SelectedId => _controllers.SelectedRows.Count == 1
        ? _controllers.SelectedRows[0].Tag as string
        : null;

    public void SetStatus(string value)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => SetStatus(value)); }
            catch (InvalidOperationException) { }
            return;
        }
        _status.SetMessage(value);
    }

    internal void ApplyRows(IReadOnlyList<ControllerRowModel> rows)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => ApplyRows(rows)); }
            catch (InvalidOperationException) { }
            return;
        }

        string? selectedId = SelectedId;
        int selectedIndex = -1;
        _controllers.Rows.Clear();
        foreach (ControllerRowModel row in rows)
        {
            string state = row.IsConnected
                ? row.Issue ?? (row.OutputDeviceId is null
                    ? "Starting..."
                    : "Connected")
                : "Disconnected";
            if (state.StartsWith("Reconnect required:",
                    StringComparison.OrdinalIgnoreCase))
                state = "Turn off, then on";
            int index = _controllers.Rows.Add(
                $"Controller {row.RegistrationOrder}  ·  " +
                VirtualControllerDisplayName.Format(row.DisplayName, row.OutputMode),
                IdentificationLightDisplay.Format(row.IdentificationLightMask),
                state,
                row.OutputDeviceId is uint id
                    ? row.OutputMode == GamepadOutputMode.XInput
                        ? $"Xbox output {id}"
                        : $"vJoy {id}"
                    : "Not assigned");
            DataGridViewRow gridRow = _controllers.Rows[index];
            gridRow.Tag = row.StableId;
            if (row.StableId == selectedId) selectedIndex = index;
            gridRow.Cells[1].Style.ForeColor = row.IdentificationLightMask is null
                ? UiTheme.TextMuted : UiTheme.Primary;
            gridRow.Cells[2].Style.ForeColor = row.Issue is not null
                ? UiTheme.Warning
                : row.IsConnected ? UiTheme.Success : UiTheme.TextMuted;
            gridRow.Cells[3].Style.ForeColor = UiTheme.TextMuted;
        }
        bool hasRows = rows.Count > 0;
        _controllers.ClearSelection();
        if (selectedIndex >= 0)
        {
            _controllers.Rows[selectedIndex].Selected = true;
            _controllers.CurrentCell = _controllers.Rows[selectedIndex].Cells[0];
        }
        _controllers.Visible = hasRows;
        _emptyState.Visible = !hasRows;
        _controllerCount.Text = rows.Count == 1 ? "1 registered" : $"{rows.Count} registered";
    }

    private async Task AddControllerAsync()
    {
        using var dialog = new AddControllerForm(_runtime.GetAddCandidates());
        if (dialog.ShowDialog(this) == DialogResult.OK
            && dialog.SelectedStableId is string id)
            await _runtime.AddControllerAsync(id);
    }

    private async Task RemoveSelectedAsync()
    {
        string? id = SelectedId;
        if (id is null) return;
        if (MessageBox.Show(this,
                "Remove this controller from AFGC PC Manager?\n\nIts Bluetooth pairing will stay intact.",
                "Remove controller", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
            == DialogResult.Yes)
            await _runtime.RemoveControllerAsync(id);
    }

    private async Task OpenSettingsAsync(string? controllerId)
    {
        using var dialog = new SettingsForm(
            _runtime.GetSettings(), controllerId, _runtime.GetDiagnostics);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result is not null)
            await _runtime.SaveSettingsAsync(dialog.Result);
    }

    private void OnUpdatesAvailable(object? sender,
        IReadOnlyList<UpdateCheckResult.Available> updates)
    {
        UpdateCheckResult.Available? manager = updates.FirstOrDefault(
            update => update.Component == ReleaseComponent.AfgcPcManager);
        if (manager is null) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => ShowManagerUpdate(manager)); }
            catch (InvalidOperationException) { }
            return;
        }
        ShowManagerUpdate(manager);
    }

    private void ShowManagerUpdate(UpdateCheckResult.Available update)
    {
        _managerUpdate = update;
        _update.Text = $"Update to {update.Latest}";
        _update.Visible = true;
    }

    private void StartManagerUpdate()
    {
        if (_managerUpdate is null) return;
        try
        {
            ManagerUpdateLauncher.PromptAndStart(this, _managerUpdate,
                AppContext.BaseDirectory);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"The update could not be started.\n\n{ex.Message}\n\n" +
                $"You can download it manually from:\n{_managerUpdate.ReleasePage}",
                "AFGC PC Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RunUiActionAsync(Func<Task> action, string operation)
    {
        try { await action(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not {operation}.\n\n{ex.Message}",
                "AFGC PC Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
