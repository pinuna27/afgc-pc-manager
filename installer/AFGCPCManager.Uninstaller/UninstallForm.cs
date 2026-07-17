using AFGCPCManager.Setup.Core.Dependencies;

namespace AFGCPCManager.Uninstaller;

internal sealed class UninstallForm : Form
{
    private readonly CheckBox _vjoy = new() { Text = "Uninstall vJoy", AutoSize = true };
    private readonly CheckBox _hidHide = new() { Text = "Uninstall HidHide", AutoSize = true };
    public DependencyUninstallOptions Options => new(_vjoy.Checked, _hidHide.Checked);

    public UninstallForm(DependencyUninstallOptions defaults)
    {
        Text = "Uninstall AFGC PC Manager"; StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        ClientSize = new(470, 250); AutoScaleMode = AutoScaleMode.Dpi;
        _vjoy.Checked = defaults.UninstallVJoy; _hidHide.Checked = defaults.UninstallHidHide;
        var title = new Label { Text = "Remove AFGC PC Manager?", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        var explanation = new Label { Text = "The application will be removed. Choose whether to also remove its controller dependencies.", AutoSize = false, Width = 420, Height = 42 };
        var warning = new Label { Text = "vJoy and HidHide may be shared by other controller software. Removing them can stop that software from working.", AutoSize = false, Width = 420, Height = 45, ForeColor = Color.DarkRed };
        var remove = new Button { Text = "Uninstall", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.Add(remove); buttons.Controls.Add(cancel);
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new(18) };
        layout.Controls.Add(title); layout.Controls.Add(explanation); layout.Controls.Add(_vjoy); layout.Controls.Add(_hidHide); layout.Controls.Add(warning); layout.Controls.Add(buttons);
        Controls.Add(layout); AcceptButton = remove; CancelButton = cancel;
    }
}
