using AFGCPCManager.Core.Mapping;
using AFGCPCManager.Core.Settings;
using AFGCPCManager.UI;
using System.Diagnostics;

namespace AFGCPCManager.App;

internal sealed class SettingsForm : Form
{
    private readonly SettingsDocument _source;
    private readonly Func<DiagnosticSnapshot> _diagnostics;
    private readonly TextBox _diagnosticReport = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Dock = DockStyle.Fill
    };
    private readonly CheckBox _startup = Check("Start with Windows");
    private readonly CheckBox _tray = Check("Show the tray icon when Windows starts the app");
    private readonly CheckBox _autoFind = Check("Automatically register new Fire controllers");
    private readonly CheckBox _hide = Check("Hide the original controller with HidHide");
    private readonly RadioButton _xInput = OutputChoice("Xbox (XInput)");
    private readonly RadioButton _directInput = OutputChoice("vJoy (DirectInput)");
    private readonly ToolTip _outputModeToolTip = new()
    {
        AutoPopDelay = 10000,
        InitialDelay = 350,
        ReshowDelay = 100,
        ShowAlways = false
    };
    private readonly CheckBox _identificationLights = Check("Use controller identification lights");
    private readonly CheckBox _updates = Check("Check for stable updates automatically");
    private readonly CheckBox _notifications = Check("Show update notifications");
    private readonly RadioButton _defaults = new() { Text = "Default mappings", AutoSize = true };
    private readonly RadioButton _override = new() { Text = "Controller override", AutoSize = true };
    private readonly ComboBox _controller = Combo();
    private readonly ComboBox _home = Combo();
    private readonly ComboBox _circle = Combo();
    private readonly ComboBox _media = Combo();
    private readonly Label _circleLabel = UiTheme.Body("GameCircle button");
    private readonly AfgcCallout _mappingHelp = new(string.Empty);
    private readonly HashSet<string> _resetOverrides = new(StringComparer.Ordinal);

    public SettingsDocument? Result { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _outputModeToolTip.Dispose();
        base.Dispose(disposing);
    }

    public SettingsForm(SettingsDocument source, string? initialController,
        Func<DiagnosticSnapshot> diagnostics)
    {
        _source = source;
        _diagnostics = diagnostics;
        Text = "AFGC PC Manager Settings";
        ClientSize = new Size(920, 680);
        MinimumSize = new Size(760, 580);

        LoadGeneral();
        ConfigureMappingInputs();

        var tabs = new AfgcTabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildGeneral());
        tabs.TabPages.Add(BuildDevice());
        tabs.TabPages.Add(BuildDiagnostics());

        var save = new AfgcButton("Save changes", AfgcButtonKind.Primary);
        save.Click += (_, _) => SaveAndClose();
        var cancel = new AfgcButton("Cancel") { DialogResult = DialogResult.Cancel };

        Panel header = UiTheme.FormHeader(
            "Settings",
            "Controller behavior, startup, and diagnostics",
            compact: true);
        var content = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 12, 20, 12),
            BackColor = UiTheme.Canvas
        };
        content.Controls.Add(tabs);

        Controls.Add(content);
        Controls.Add(UiTheme.FormFooter(save, cancel));
        Controls.Add(header);
        CancelButton = cancel;

        if (initialController is not null)
        {
            _override.Checked = true;
            _controller.SelectedValue = initialController;
            tabs.SelectedIndex = 1;
        }
        else _defaults.Checked = true;
        LoadMapping();
        RefreshDiagnostics();
        Shown += (_, _) => LoadMapping();
        UiTheme.Apply(this, centerParent: true);
    }

    private void ConfigureMappingInputs()
    {
        _home.DataSource = Enum.GetValues<HomeButtonMode>();
        _circle.DataSource = Enum.GetValues<GameCircleButtonMode>();
        _media.DataSource = Enum.GetValues<MediaRowMode>();
        _controller.DisplayMember = "Value";
        _controller.ValueMember = "Key";
        _controller.DataSource = _source.Controllers.Select(controller =>
                new KeyValuePair<string, string>(controller.StableId,
                    $"Controller {controller.RegistrationOrder}: " +
                    VirtualControllerDisplayName.Format(controller.DisplayName,
                        _source.Application.OutputMode)))
            .ToList();

        FormatCombo<HomeButtonMode>(_home, value => value switch
        {
            HomeButtonMode.Guide => "Guide button (recommended)",
            HomeButtonMode.Original => "Windows Home action",
            HomeButtonMode.Disabled => "Disabled",
            _ => value.ToString()!
        });
        FormatCombo<GameCircleButtonMode>(_circle, value => value switch
        {
            GameCircleButtonMode.Guide => "Guide button",
            GameCircleButtonMode.Disabled => "Disabled",
            _ => value.ToString()!
        });
        FormatCombo<MediaRowMode>(_media, value => value switch
        {
            MediaRowMode.Media => "Windows media controls (recommended)",
            MediaRowMode.Navigation => "Back / Guide / Menu buttons",
            MediaRowMode.Disabled => "Disabled",
            _ => value.ToString()!
        });

        foreach (ComboBox combo in new[] { _controller, _home, _circle, _media })
        {
            UiTheme.StyleInput(combo);
            combo.MinimumSize = new Size(240, 0);
        }
        _controller.SelectedIndexChanged += (_, _) =>
        {
            if (_override.Checked) LoadMapping();
        };
        _home.SelectedIndexChanged += (_, _) =>
        {
            UpdateCircleVisibility();
            UpdateMappingHelp();
        };
        _media.SelectedIndexChanged += (_, _) => UpdateMappingHelp();
        _home.SelectionChangeCommitted += (_, _) => MarkOverrideEdited();
        _circle.SelectionChangeCommitted += (_, _) => MarkOverrideEdited();
        _media.SelectionChangeCommitted += (_, _) => MarkOverrideEdited();
    }

    private TabPage BuildGeneral()
    {
        var page = NewPage("General");
        var preferences = new FlowLayoutPanel
        {
            Name = "GeneralPreferences",
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(18, 10, 18, 10),
            BackColor = UiTheme.Surface
        };
        preferences.Controls.Add(Preference(_startup,
            "Launch the controller bridge when you sign in to Windows."));
        preferences.Controls.Add(Preference(_tray,
            "Keep the app available from the notification area during automatic startup."));
        preferences.Controls.Add(Preference(_autoFind,
            "Register newly connected Amazon Fire controllers without opening Add controller."));
        preferences.Controls.Add(Preference(_hide,
            "Prevents games and Windows from receiving duplicate physical input."));
        preferences.Controls.Add(OutputModePreference());
        preferences.Controls.Add(Preference(_identificationLights,
            "Assign each registered controller a stable four-light pattern shown in the controller list."));
        preferences.Controls.Add(Preference(_updates,
            "Check the app, vJoy, ViGEmBus, and HidHide for newer stable versions."));
        preferences.Controls.Add(Preference(_notifications,
            "Show a Windows notification when a stable update is available."));
        bool fittingPreferences = false;
        void FitPreferenceWidths(object? _, EventArgs __)
        {
            if (fittingPreferences) return;
            fittingPreferences = true;
            try
            {
                int contentHeight = preferences.Padding.Vertical +
                    preferences.Controls.Cast<Control>()
                        .Sum(control => control.Height + control.Margin.Vertical);
                // FlowLayoutPanel's implicit display rectangle can stop as soon as
                // the final control is barely visible. Add a separate trailing
                // inset so the final preference and its bottom breathing room can
                // both be reached at the scrollbar's maximum position.
                int minimumHeight = contentHeight + preferences.Padding.Bottom;
                if (preferences.AutoScrollMinSize.Height != minimumHeight)
                    preferences.AutoScrollMinSize = new Size(0, minimumHeight);
                int scrollbar = minimumHeight > preferences.ClientSize.Height
                    ? SystemInformation.VerticalScrollBarWidth
                    : 0;
                int width = Math.Max(UiTheme.Scale(preferences, 240),
                    preferences.ClientSize.Width - preferences.Padding.Horizontal -
                    scrollbar - UiTheme.Scale(preferences, 4));
                foreach (Control preference in preferences.Controls)
                    preference.Width = width;
            }
            finally { fittingPreferences = false; }
        }
        preferences.Layout += FitPreferenceWidths;
        preferences.ClientSizeChanged += FitPreferenceWidths;
        _startup.CheckedChanged += (_, _) => _tray.Enabled = _startup.Checked;

        var card = new AfgcCard { Dock = DockStyle.Fill, Padding = new Padding(1) };
        card.Controls.Add(preferences);
        page.Controls.Add(card);
        return page;
    }

    private TabPage BuildDevice()
    {
        var page = NewPage("Controller");
        var modeRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 18)
        };
        _defaults.Margin = new Padding(0, 0, 24, 0);
        modeRow.Controls.Add(_defaults);
        modeRow.Controls.Add(_override);

        var grid = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(22, 18, 22, 18),
            BackColor = UiTheme.Surface
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.Controls.Add(modeRow, 0, 0);
        grid.SetColumnSpan(modeRow, 2);
        AddMappingRow(grid, 1, UiTheme.Body("Controller"), _controller);
        AddMappingRow(grid, 2, UiTheme.Body("Home button"), _home);
        AddMappingRow(grid, 3, _circleLabel, _circle);
        AddMappingRow(grid, 4, UiTheme.Body("Bottom media row"), _media);

        var reset = new AfgcButton("Reset mapping", AfgcButtonKind.Quiet);
        reset.Click += (_, _) => ResetMapping();
        var resetRow = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = UiTheme.Surface };
        reset.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        reset.Location = new Point(0, 6);
        resetRow.Controls.Add(reset);
        resetRow.Resize += (_, _) => reset.Left = Math.Max(0, resetRow.ClientSize.Width - reset.Width - 20);

        _mappingHelp.Dock = DockStyle.Top;
        _mappingHelp.Height = 66;

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            AutoScroll = true,
            AutoScrollMinSize = new Size(0, 370)
        };
        body.Controls.Add(resetRow);
        body.Controls.Add(_mappingHelp);
        body.Controls.Add(grid);
        var card = new AfgcCard { Dock = DockStyle.Fill, Padding = new Padding(1) };
        card.Controls.Add(body);
        page.Controls.Add(card);

        _defaults.CheckedChanged += (_, _) =>
        {
            _controller.Enabled = _override.Checked;
            LoadMapping();
        };
        return page;
    }

    private TabPage BuildDiagnostics()
    {
        var page = NewPage("Diagnostics");
        UiTheme.StyleProgressLog(_diagnosticReport);

        var refresh = new AfgcButton("Refresh");
        refresh.Click += (_, _) => RefreshDiagnostics();
        var copy = new AfgcButton("Copy report");
        copy.Click += (_, _) =>
        {
            if (_diagnosticReport.TextLength > 0)
                Clipboard.SetText(_diagnosticReport.Text);
        };
        var controllers = new AfgcButton("Open game controllers");
        controllers.Click += (_, _) => OpenExternal(
            new ProcessStartInfo("control.exe", "joy.cpl") { UseShellExecute = true },
            "Game controllers");
        string setupPath = Path.Combine(AppContext.BaseDirectory, "AFGCPCManager.Setup.exe");
        var repair = new AfgcButton("Repair setup", AfgcButtonKind.Quiet)
        {
            Enabled = File.Exists(setupPath)
        };
        repair.Click += (_, _) => OpenExternal(
            new ProcessStartInfo(setupPath, "--repair") { UseShellExecute = true },
            "Repair setup");

        var actionRow = UiTheme.ButtonRow(refresh, copy, controllers, repair);
        var cardHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 54,
            Padding = new Padding(18, 8, 18, 8),
            ColumnCount = 2,
            BackColor = UiTheme.Surface
        };
        cardHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cardHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Label title = UiTheme.SectionHeading("Runtime report");
        title.Anchor = AnchorStyles.Left;
        cardHeader.Controls.Add(title, 0, 0);
        cardHeader.Controls.Add(actionRow, 1, 0);

        var reportHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 0, 18, 18),
            BackColor = UiTheme.Surface
        };
        reportHost.Controls.Add(_diagnosticReport);
        var cardBody = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };
        cardBody.Controls.Add(reportHost);
        cardBody.Controls.Add(cardHeader);
        var card = new AfgcCard { Dock = DockStyle.Fill, Padding = new Padding(1) };
        card.Controls.Add(cardBody);
        page.Controls.Add(card);
        return page;
    }

    private void OpenExternal(ProcessStartInfo startInfo, string title)
    {
        try { Process.Start(startInfo); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, title,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshDiagnostics() =>
        _diagnosticReport.Text = _diagnostics().ToReport();

    private void LoadGeneral()
    {
        AppSettings settings = _source.Application;
        _startup.Checked = settings.StartWithWindows;
        _tray.Checked = settings.ShowTrayOnAutomaticStart;
        _autoFind.Checked = settings.AutomaticallyFindControllers;
        _hide.Checked = settings.HidePhysicalControllers;
        _xInput.Checked = settings.OutputMode == GamepadOutputMode.XInput;
        _directInput.Checked = settings.OutputMode == GamepadOutputMode.DirectInput;
        _identificationLights.Checked = settings.ControlIdentificationLights;
        _updates.Checked = settings.AutomaticallyCheckForUpdates;
        _notifications.Checked = settings.ShowNotifications;
        _tray.Enabled = _startup.Checked;
    }

    private string? SelectedController => _controller.SelectedValue as string;

    private void LoadMapping()
    {
        ControllerMappingProfile profile = _source.DefaultMapping;
        if (_override.Checked && SelectedController is string id)
            profile = EffectiveMappingResolver.Resolve(profile,
                _source.Overrides.GetValueOrDefault(id));
        _home.SelectedItem = profile.HomeButton;
        _circle.SelectedItem = profile.GameCircleButton;
        _media.SelectedItem = profile.MediaRow;
        _controller.Enabled = _override.Checked;
        UpdateCircleVisibility();
        UpdateMappingHelp();
    }

    private void UpdateCircleVisibility()
    {
        bool show = _home.SelectedItem is HomeButtonMode.Guide;
        _circle.Visible = _circleLabel.Visible = show;
    }

    private void UpdateMappingHelp()
    {
        string home = _home.SelectedItem switch
        {
            HomeButtonMode.Guide => "Home is sent only as the virtual Guide button.",
            HomeButtonMode.Original => "Home keeps its original Windows browser action.",
            HomeButtonMode.Disabled => "Home produces no action.",
            _ => string.Empty
        };
        string media = _media.SelectedItem switch
        {
            MediaRowMode.Media => "The media row seeks the active Windows media session by 10 seconds and controls play/pause; it does not appear in joy.cpl.",
            MediaRowMode.Navigation => "The media row appears in joy.cpl as Back, Guide, and Menu.",
            MediaRowMode.Disabled => "The media row produces no action.",
            _ => string.Empty
        };
        _mappingHelp.Text = $"{home}  {media}";
    }

    private ControllerMappingProfile Current() => new()
    {
        HomeButton = (HomeButtonMode)_home.SelectedItem!,
        GameCircleButton = (GameCircleButtonMode)_circle.SelectedItem!,
        MediaRow = (MediaRowMode)_media.SelectedItem!
    };

    private void ResetMapping()
    {
        if (_defaults.Checked)
        {
            _home.SelectedItem = HomeButtonMode.Guide;
            _circle.SelectedItem = GameCircleButtonMode.Guide;
            _media.SelectedItem = MediaRowMode.Media;
        }
        else
        {
            _home.SelectedItem = _source.DefaultMapping.HomeButton;
            _circle.SelectedItem = _source.DefaultMapping.GameCircleButton;
            _media.SelectedItem = _source.DefaultMapping.MediaRow;
            if (SelectedController is string id) _resetOverrides.Add(id);
        }
        UpdateMappingHelp();
    }

    private void MarkOverrideEdited()
    {
        if (_override.Checked && SelectedController is string id)
            _resetOverrides.Remove(id);
    }

    private void SaveAndClose()
    {
        AppSettings application = _source.Application with
        {
            StartWithWindows = _startup.Checked,
            ShowTrayOnAutomaticStart = _tray.Checked,
            AutomaticallyFindControllers = _autoFind.Checked,
            HidePhysicalControllers = _hide.Checked,
            OutputMode = _xInput.Checked
                ? GamepadOutputMode.XInput
                : GamepadOutputMode.DirectInput,
            ControlIdentificationLights = _identificationLights.Checked,
            AutomaticallyCheckForUpdates = _updates.Checked,
            ShowNotifications = _notifications.Checked
        };
        var overrides = new Dictionary<string, ControllerMappingOverrides>(
            _source.Overrides, StringComparer.Ordinal);
        ControllerMappingProfile defaults = _source.DefaultMapping;
        foreach (string id in _resetOverrides) overrides.Remove(id);
        if (_defaults.Checked) defaults = Current();
        else if (SelectedController is string id && !_resetOverrides.Contains(id))
        {
            ControllerMappingProfile profile = Current();
            overrides[id] = new ControllerMappingOverrides
            {
                HomeButton = profile.HomeButton,
                GameCircleButton = profile.GameCircleButton,
                MediaRow = profile.MediaRow
            };
        }
        Result = _source with
        {
            Application = application,
            DefaultMapping = defaults,
            Overrides = overrides
        };
        DialogResult = DialogResult.OK;
        Close();
    }

    private static TabPage NewPage(string text) => new(text)
    {
        BackColor = UiTheme.Canvas,
        Padding = new Padding(10, 10, 10, 10)
    };

    private static CheckBox Check(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = UiTheme.Text
    };

    private static RadioButton OutputChoice(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = UiTheme.Text
    };

    private Control OutputModePreference()
    {
        const string xInputHelp = "Native game support; up to 4 controllers.";
        const string directInputHelp = "Over 4 controllers; some games need a translator.";
        var panel = new Panel
        {
            Width = 760,
            Height = 88,
            Margin = Padding.Empty,
            BackColor = UiTheme.Surface
        };
        Label heading = UiTheme.Body("Virtual controller output");
        heading.Location = new Point(0, 2);
        _xInput.Location = new Point(20, 27);
        _directInput.Location = new Point(20, 57);
        Label xInputDetail = UiTheme.Body(xInputHelp, muted: true);
        xInputDetail.Location = new Point(160, 28);
        Label directInputDetail = UiTheme.Body(directInputHelp, muted: true);
        directInputDetail.Location = new Point(160, 58);
        _xInput.AccessibleDescription = xInputHelp;
        _directInput.AccessibleDescription = directInputHelp;
        _outputModeToolTip.SetToolTip(_xInput, xInputHelp);
        _outputModeToolTip.SetToolTip(xInputDetail, xInputHelp);
        _outputModeToolTip.SetToolTip(_directInput, directInputHelp);
        _outputModeToolTip.SetToolTip(directInputDetail, directInputHelp);
        panel.Controls.Add(heading);
        panel.Controls.Add(_xInput);
        panel.Controls.Add(xInputDetail);
        panel.Controls.Add(_directInput);
        panel.Controls.Add(directInputDetail);
        return panel;
    }

    private static ComboBox Combo() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList
    };

    private static Control Preference(CheckBox checkBox, string description)
    {
        var panel = new Panel
        {
            Width = 760,
            Height = 48,
            Margin = Padding.Empty,
            BackColor = UiTheme.Surface
        };
        checkBox.Location = new Point(0, 2);
        var detail = UiTheme.Body(description, muted: true);
        detail.Location = new Point(20, 23);
        panel.Controls.Add(checkBox);
        panel.Controls.Add(detail);
        return panel;
    }

    private static void AddMappingRow(TableLayoutPanel grid, int row,
        Control label, Control value)
    {
        label.Anchor = AnchorStyles.Left;
        value.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        grid.Controls.Add(label, 0, row);
        grid.Controls.Add(value, 1, row);
    }

    private static void FormatCombo<T>(ComboBox combo, Func<T, string> format)
        where T : struct, Enum
    {
        combo.Format += (_, e) =>
        {
            if (e.ListItem is T value) e.Value = format(value);
        };
        combo.FormattingEnabled = true;
    }
}
