using AFGCPCManager.Core.Mapping;
using AFGCPCManager.Core.Settings;
using System.Diagnostics;

namespace AFGCPCManager.App;

internal sealed class SettingsForm : Form
{
    private readonly SettingsDocument _source;
    private readonly Func<DiagnosticSnapshot> _diagnostics;
    private readonly TextBox _diagnosticReport = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericMonospace, 9) };
    private readonly CheckBox _startup = Check("Start with Windows"), _tray = Check("Show tray icon on automatic start"), _autoFind = Check("Automatically find controllers"), _hide = Check("Hide original controllers with HidHide"), _updates = Check("Automatically check for updates"), _autoInstall = Check("Automatically install updates"), _notifications = Check("Show notifications");
    private readonly RadioButton _defaults = new() { Text = "Default mappings", AutoSize = true }, _override = new() { Text = "Controller override", AutoSize = true };
    private readonly ComboBox _controller = Combo(), _home = Combo(), _circle = Combo(), _media = Combo();
    private readonly Label _circleLabel = new() { Text = "GameCircle button", AutoSize = true };
    private readonly HashSet<string> _resetOverrides = new(StringComparer.Ordinal);
    public SettingsDocument? Result { get; private set; }

    public SettingsForm(SettingsDocument source, string? initialController, Func<DiagnosticSnapshot> diagnostics)
    {
        _source = source; _diagnostics = diagnostics; Text = "AFGC PC Manager Settings"; ClientSize = new(720, 510); MinimumSize = new(620, 450); StartPosition = FormStartPosition.CenterParent;
        var tabs = new TabControl { Dock = DockStyle.Fill, Alignment = TabAlignment.Left, Multiline = true, ItemSize = new(36, 125), SizeMode = TabSizeMode.Fixed };
        tabs.TabPages.Add(BuildGeneral()); tabs.TabPages.Add(BuildDevice()); tabs.TabPages.Add(BuildDiagnostics()); Controls.Add(tabs);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 55, Padding = new(10), FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Text = "Save", AutoSize = true }; save.Click += (_, _) => SaveAndClose(); var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        footer.Controls.Add(save); footer.Controls.Add(cancel); Controls.Add(footer); CancelButton = cancel;
        LoadGeneral(); _home.DataSource = Enum.GetValues<HomeButtonMode>(); _circle.DataSource = Enum.GetValues<GameCircleButtonMode>(); _media.DataSource = Enum.GetValues<MediaRowMode>();
        _controller.DisplayMember = "Value"; _controller.ValueMember = "Key"; _controller.DataSource = source.Controllers.Select(x => new KeyValuePair<string, string>(x.StableId, $"Controller {x.RegistrationOrder}: {x.DisplayName}")).ToList();
        _home.SelectionChangeCommitted += (_, _) => MarkOverrideEdited(); _circle.SelectionChangeCommitted += (_, _) => MarkOverrideEdited(); _media.SelectionChangeCommitted += (_, _) => MarkOverrideEdited();
        if (initialController is not null) { _override.Checked = true; _controller.SelectedValue = initialController; tabs.SelectedIndex = 1; } else _defaults.Checked = true; LoadMapping();
        RefreshDiagnostics();
    }
    private TabPage BuildGeneral()
    {
        var page = new TabPage("General"); var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new(24), WrapContents = false, AutoScroll = true };
        flow.Controls.AddRange([_startup, _tray, _autoFind, _hide, _updates, _autoInstall, _notifications]); _startup.CheckedChanged += (_, _) => _tray.Visible = _startup.Checked; _updates.CheckedChanged += (_, _) => _autoInstall.Enabled = _updates.Checked; page.Controls.Add(flow); return page;
    }
    private TabPage BuildDevice()
    {
        var page = new TabPage("Device Settings"); var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(24), ColumnCount = 2, RowCount = 7, AutoScroll = true };
        grid.ColumnStyles.Add(new(SizeType.Absolute, 180)); grid.ColumnStyles.Add(new(SizeType.Percent, 100)); var modes = new FlowLayoutPanel { AutoSize = true }; modes.Controls.Add(_defaults); modes.Controls.Add(_override); grid.Controls.Add(modes, 0, 0); grid.SetColumnSpan(modes, 2);
        AddRow(grid, 1, new Label { Text = "Controller", AutoSize = true }, _controller); AddRow(grid, 2, new Label { Text = "Home button", AutoSize = true }, _home); AddRow(grid, 3, _circleLabel, _circle); AddRow(grid, 4, new Label { Text = "Bottom media row", AutoSize = true }, _media);
        var reset = new Button { Text = "Reset mapping", AutoSize = true }; reset.Click += (_, _) => ResetMapping(); grid.Controls.Add(reset, 1, 5);
        _defaults.CheckedChanged += (_, _) => { _controller.Enabled = _override.Checked; LoadMapping(); }; _controller.SelectedIndexChanged += (_, _) => { if (_override.Checked) LoadMapping(); }; _home.SelectedIndexChanged += (_, _) => UpdateCircleVisibility(); page.Controls.Add(grid); return page;
    }
    private TabPage BuildDiagnostics()
    {
        var page = new TabPage("Diagnostics"); var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, Padding = new(6), FlowDirection = FlowDirection.RightToLeft };
        var refresh = new Button { Text = "Refresh", AutoSize = true }; refresh.Click += (_, _) => RefreshDiagnostics();
        var copy = new Button { Text = "Copy report", AutoSize = true }; copy.Click += (_, _) => { if (_diagnosticReport.TextLength > 0) Clipboard.SetText(_diagnosticReport.Text); };
        string setupPath = Path.Combine(AppContext.BaseDirectory, "AFGCPCManager.Setup.exe"); var repair = new Button { Text = "Repair setup", AutoSize = true, Enabled = File.Exists(setupPath) };
        repair.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(setupPath, "--repair") { UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Repair setup", MessageBoxButtons.OK, MessageBoxIcon.Error); } };
        buttons.Controls.Add(refresh); buttons.Controls.Add(copy); buttons.Controls.Add(repair); page.Controls.Add(_diagnosticReport); page.Controls.Add(buttons); return page;
    }
    private void RefreshDiagnostics() => _diagnosticReport.Text = _diagnostics().ToReport();
    private static void AddRow(TableLayoutPanel grid, int row, Control label, Control value) { grid.Controls.Add(label, 0, row); grid.Controls.Add(value, 1, row); value.Dock = DockStyle.Top; }
    private void LoadGeneral() { var x = _source.Application; _startup.Checked = x.StartWithWindows; _tray.Checked = x.ShowTrayOnAutomaticStart; _autoFind.Checked = x.AutomaticallyFindControllers; _hide.Checked = x.HidePhysicalControllers; _updates.Checked = x.AutomaticallyCheckForUpdates; _autoInstall.Checked = x.AutomaticallyInstallUpdates; _autoInstall.Enabled = _updates.Checked; _notifications.Checked = x.ShowNotifications; }
    private string? SelectedController => _controller.SelectedValue as string;
    private void LoadMapping() { var p = _source.DefaultMapping; if (_override.Checked && SelectedController is string id) p = EffectiveMappingResolver.Resolve(p, _source.Overrides.GetValueOrDefault(id)); _home.SelectedItem = p.HomeButton; _circle.SelectedItem = p.GameCircleButton; _media.SelectedItem = p.MediaRow; _controller.Enabled = _override.Checked; UpdateCircleVisibility(); }
    private void UpdateCircleVisibility() { bool show = _home.SelectedItem is HomeButtonMode.Guide; _circle.Visible = _circleLabel.Visible = show; }
    private ControllerMappingProfile Current() => new() { HomeButton = (HomeButtonMode)_home.SelectedItem!, GameCircleButton = (GameCircleButtonMode)_circle.SelectedItem!, MediaRow = (MediaRowMode)_media.SelectedItem! };
    private void ResetMapping() { if (_defaults.Checked) { _home.SelectedItem = HomeButtonMode.Guide; _circle.SelectedItem = GameCircleButtonMode.Guide; _media.SelectedItem = MediaRowMode.Media; } else { _home.SelectedItem = _source.DefaultMapping.HomeButton; _circle.SelectedItem = _source.DefaultMapping.GameCircleButton; _media.SelectedItem = _source.DefaultMapping.MediaRow; if (SelectedController is string id) _resetOverrides.Add(id); } }
    private void MarkOverrideEdited() { if (_override.Checked && SelectedController is string id) _resetOverrides.Remove(id); }
    private void SaveAndClose()
    {
        var app = _source.Application with { StartWithWindows = _startup.Checked, ShowTrayOnAutomaticStart = _tray.Checked, AutomaticallyFindControllers = _autoFind.Checked, HidePhysicalControllers = _hide.Checked, AutomaticallyCheckForUpdates = _updates.Checked, AutomaticallyInstallUpdates = _updates.Checked && _autoInstall.Checked, ShowNotifications = _notifications.Checked };
        var overrides = new Dictionary<string, ControllerMappingOverrides>(_source.Overrides, StringComparer.Ordinal); var defaults = _source.DefaultMapping;
        foreach (string id in _resetOverrides) overrides.Remove(id);
        if (_defaults.Checked) defaults = Current(); else if (SelectedController is string id && !_resetOverrides.Contains(id)) { var p = Current(); overrides[id] = new() { HomeButton = p.HomeButton, GameCircleButton = p.GameCircleButton, MediaRow = p.MediaRow }; }
        Result = _source with { Application = app, DefaultMapping = defaults, Overrides = overrides }; DialogResult = DialogResult.OK; Close();
    }
    private static CheckBox Check(string text) => new() { Text = text, AutoSize = true, Margin = new(4, 7, 4, 7) };
    private static ComboBox Combo() => new() { DropDownStyle = ComboBoxStyle.DropDownList };
}
