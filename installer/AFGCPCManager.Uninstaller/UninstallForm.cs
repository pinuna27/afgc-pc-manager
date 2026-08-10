using AFGCPCManager.Setup.Core.Dependencies;
using AFGCPCManager.UI;

namespace AFGCPCManager.Uninstaller;

internal sealed class UninstallForm : Form
{
    private readonly CheckBox _vjoy = new()
    {
        Text = "Also uninstall vJoy",
        AutoSize = true
    };
    private readonly CheckBox _hidHide = new()
    {
        Text = "Also uninstall HidHide",
        AutoSize = true
    };

    public DependencyUninstallOptions Options =>
        new(_vjoy.Checked, _hidHide.Checked);

    public UninstallForm(DependencyUninstallOptions defaults)
    {
        Text = "Uninstall AFGC PC Manager";
        ClientSize = new Size(680, 500);
        MinimumSize = new Size(590, 430);
        _vjoy.Checked = defaults.UninstallVJoy;
        _hidHide.Checked = defaults.UninstallHidHide;

        var remove = new AfgcButton("Uninstall", AfgcButtonKind.Danger)
        {
            DialogResult = DialogResult.OK
        };
        var cancel = new AfgcButton("Cancel") { DialogResult = DialogResult.Cancel };
        Panel header = UiTheme.FormHeader(
            "Uninstall AFGC PC Manager",
            "Remove the app and restore controller visibility",
            compact: true);

        var title = UiTheme.Heading("Remove AFGC PC Manager?", dialog: true);
        var explanation = UiTheme.Body(
            "The application and its saved installation state will be removed. Your Bluetooth pairing is not affected.",
            muted: true);
        explanation.MaximumSize = new Size(560, 0);
        explanation.Margin = new Padding(0, 8, 0, 22);

        var dependencyHeading = UiTheme.SectionHeading("Shared components");
        dependencyHeading.Margin = new Padding(0, 0, 0, 8);
        var dependencies = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty
        };
        _vjoy.Margin = new Padding(0, 6, 0, 6);
        _hidHide.Margin = new Padding(0, 6, 0, 14);
        dependencies.Controls.Add(_vjoy);
        dependencies.Controls.Add(_hidHide);

        var warning = new AfgcCallout(
            "vJoy and HidHide may be used by other controller software. Leave them unchecked unless you know they are no longer needed.",
            AfgcCalloutTone.Warning)
        {
            Dock = DockStyle.Top,
            Height = 68
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(26, 24, 26, 24),
            BackColor = UiTheme.Surface
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(explanation, 0, 1);
        layout.Controls.Add(dependencyHeading, 0, 2);
        layout.Controls.Add(dependencies, 0, 3);
        layout.Controls.Add(warning, 0, 4);

        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = UiTheme.Surface
        };
        scrollHost.Controls.Add(layout);
        var card = new AfgcCard { Dock = DockStyle.Fill, Padding = new Padding(1) };
        card.Controls.Add(scrollHost);
        var content = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            BackColor = UiTheme.Canvas
        };
        content.Controls.Add(card);

        Controls.Add(content);
        Controls.Add(UiTheme.FormFooter(remove, cancel));
        Controls.Add(header);
        AcceptButton = remove;
        CancelButton = cancel;
        UiTheme.Apply(this);
    }
}
