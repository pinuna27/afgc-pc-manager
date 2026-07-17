using System.Diagnostics;

namespace AFGCPCManager.Uninstaller;

internal sealed class UninstallProgressForm : Form
{
    private readonly string[] _args;
    private readonly Func<string[], Action<string>?, Task<int>> _operation;
    private readonly Label _heading = new() { Text = "Uninstalling AFGC PC Manager", Dock = DockStyle.Top, Height = 55, Font = new Font(SystemFonts.DefaultFont.FontFamily, 16, FontStyle.Bold) };
    private readonly Label _description = new() { Text = "Please wait while setup safely restores controller visibility and removes the selected components.", Dock = DockStyle.Top, Height = 65 };
    private readonly TextBox _progress = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = SystemColors.Window };
    private readonly Button _primary = new() { Text = "Please wait...", AutoSize = true, MinimumSize = new(96, 0), Enabled = false };
    private readonly Button _secondary = new() { Text = "Cancel", AutoSize = true, MinimumSize = new(96, 0), Enabled = false };
    private bool _running = true;
    internal int ResultCode { get; private set; }

    public UninstallProgressForm(string[] args, Func<string[], Action<string>?, Task<int>> operation)
    {
        _args = args; _operation = operation;
        Text = "AFGC PC Manager Uninstall"; StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ClientSize = new(780, 540); MinimumSize = new(780, 540); AutoScaleMode = AutoScaleMode.Dpi;
        var content = new Panel { Dock = DockStyle.Fill, Padding = new(24) }; content.Controls.Add(_progress); content.Controls.Add(_description); content.Controls.Add(_heading);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, Padding = new(10), FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange([_secondary, _primary]); Controls.Add(content); Controls.Add(buttons);
        _primary.Click += PrimaryClick; _secondary.Click += (_, _) => Close();
        FormClosing += (_, e) => { if (_running) e.Cancel = true; };
        Shown += async (_, _) => await RunAsync();
    }

    private async Task RunAsync()
    {
        try { ResultCode = await _operation(_args, Append); }
        catch (Exception ex) { ResultCode = 1; Append($"Uninstall failed: {ex.Message}"); }
        _running = false;
        if (ResultCode == 0) { _heading.Text = "Uninstall complete"; _description.Text = "AFGC PC Manager was removed successfully."; _primary.Text = "Finish"; _primary.Enabled = true; }
        else if (ResultCode == 3010) { _heading.Text = "Restart required"; _description.Text = "Windows must restart to finish uninstalling. Cleanup will resume automatically after sign-in."; _primary.Text = "Restart now"; _primary.Enabled = true; _secondary.Text = "Restart later"; _secondary.Enabled = true; }
        else { _heading.Text = "Uninstall could not complete"; _description.Text = "Review the message below. No unsafe cleanup will be attempted."; _primary.Text = "Close"; _primary.Enabled = true; }
    }

    private void Append(string message)
    {
        if (InvokeRequired) { BeginInvoke(() => Append(message)); return; }
        _progress.AppendText(message + Environment.NewLine); _progress.SelectionStart = _progress.TextLength; _progress.ScrollToCaret();
    }

    private void PrimaryClick(object? sender, EventArgs e)
    {
        if (ResultCode == 3010) Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { UseShellExecute = true });
        Close();
    }
}
