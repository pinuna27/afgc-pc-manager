using System.Diagnostics;

namespace AFGCPCManager.Bootstrapper;

internal sealed class SetupWizardForm : Form
{
    private readonly string[] _originalArgs;
    private readonly Label _heading = new() { AutoSize = false, Dock = DockStyle.Top, Height = 52, Font = new Font(SystemFonts.DefaultFont.FontFamily, 16, FontStyle.Bold) };
    private readonly Label _description = new() { AutoSize = false, Dock = DockStyle.Top, Height = 74 };
    private readonly TextBox _progress = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = SystemColors.Window };
    private readonly TextBox _destination = new() { Dock = DockStyle.Top };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, Padding = new(24) };
    private readonly Button _back = new() { Text = "< Back", Width = 92, Enabled = false };
    private readonly Button _next = new() { Text = "Install", Width = 92 };
    private readonly Button _cancel = new() { Text = "Cancel", Width = 92 };
    private bool _running;
    private WizardPage _page = WizardPage.Welcome;
    internal int ResultCode { get; private set; }

    public SetupWizardForm(string[] args)
    {
        _originalArgs = args.Where(x => !x.Equals("--wizard-run", StringComparison.OrdinalIgnoreCase)).ToArray();
        Text = "AFGC PC Manager Setup"; StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        ClientSize = new(680, 470); AutoScaleMode = AutoScaleMode.Dpi;

        var banner = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Color.FromArgb(245, 247, 250), Padding = new(22, 14, 22, 8) };
        banner.Controls.Add(new Label { Text = "AFGC PC Manager", Dock = DockStyle.Fill, Font = new Font(SystemFonts.DefaultFont.FontFamily, 18, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft });
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, Padding = new(10), FlowDirection = FlowDirection.RightToLeft, BackColor = SystemColors.Control };
        buttons.Controls.AddRange([_cancel, _next, _back]);
        Controls.Add(_content); Controls.Add(buttons); Controls.Add(banner);
        _next.Click += NextClicked;
        _cancel.Click += CancelClicked;
        FormClosing += (_, e) => { if (_running) e.Cancel = true; };
        ShowWelcome();
        if (args.Any(x => x.Equals("--wizard-run", StringComparison.OrdinalIgnoreCase))) Shown += async (_, _) => await BeginInstallAsync();
    }

    private void ShowWelcome()
    {
        _content.Controls.Clear();
        _heading.Text = OperationTitle();
        _description.Text = "Setup will install AFGC PC Manager and check the verified vJoy and HidHide components required for controller compatibility.";
        _destination.Text = Get(_originalArgs, "--install-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AFGC PC Manager");
        var locationLabel = new Label { Text = "Install location", AutoSize = true, Dock = DockStyle.Top, Padding = new(0, 8, 0, 5) };
        var details = new Label { Text = "You may be asked to approve administrator access and complete the signed vJoy and HidHide vendor wizards.", AutoSize = false, Dock = DockStyle.Top, Height = 65, Padding = new(0, 18, 0, 0) };
        _content.Controls.Add(details); _content.Controls.Add(_destination); _content.Controls.Add(locationLabel); _content.Controls.Add(_description); _content.Controls.Add(_heading);
    }

    private async Task BeginInstallAsync()
    {
        if (_running) return;
        _running = true; _next.Enabled = false; _back.Enabled = false; _cancel.Enabled = false;
        ShowProgress();
        Program.Progress = AppendProgress;
        string[] args = WithDestination(_originalArgs, _destination.Text);
        try { ResultCode = await Program.RunCoreAsync(args); }
        finally { Program.Progress = null; _running = false; }
        if (Program.DelegatedToElevatedWizard) { Close(); return; }
        if (ResultCode == 0) ShowComplete();
        else if (ResultCode == 3010) ShowRestart();
        else ShowError(Program.LastError ?? "Setup could not complete.");
    }

    private void ShowProgress()
    {
        _page = WizardPage.Progress; _content.Controls.Clear(); _heading.Text = "Installing"; _description.Text = "Please wait while setup prepares AFGC PC Manager and its controller components.";
        _progress.Clear(); _content.Controls.Add(_progress); _content.Controls.Add(_description); _content.Controls.Add(_heading);
    }

    private void ShowComplete()
    {
        _page = WizardPage.Complete; ShowMessage("Setup complete", "AFGC PC Manager is ready to use.");
        _next.Text = "Finish"; _next.Enabled = true;
        _cancel.Visible = false;
    }

    private void ShowRestart()
    {
        _page = WizardPage.Restart; ShowMessage("Restart required", "Windows must restart before setup can continue. Setup will resume automatically after you sign in.");
        _next.Text = "Restart now"; _next.Enabled = true;
        _cancel.Text = "Restart later"; _cancel.Enabled = true;
    }

    private void ShowError(string error)
    {
        _page = WizardPage.Error; ShowMessage("Setup could not complete", error);
        _next.Text = "Close"; _next.Enabled = true; _cancel.Visible = false;
    }

    private void ShowMessage(string heading, string description)
    {
        _content.Controls.Clear(); _heading.Text = heading; _description.Text = description;
        _content.Controls.Add(_description); _content.Controls.Add(_heading);
    }

    private void AppendProgress(string message)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendProgress(message)); return; }
        _progress.AppendText(message + Environment.NewLine); _progress.SelectionStart = _progress.TextLength; _progress.ScrollToCaret();
    }

    private string OperationTitle() => _originalArgs.Contains("--repair", StringComparer.OrdinalIgnoreCase) ? "Repair AFGC PC Manager" : _originalArgs.Contains("--update", StringComparer.OrdinalIgnoreCase) ? "Update AFGC PC Manager" : "Install AFGC PC Manager";
    private static string[] WithDestination(string[] args, string destination) => args.Contains("--install-dir", StringComparer.OrdinalIgnoreCase) ? args : [.. args, "--install-dir", destination];
    private static string? Get(string[] args, string key) { int index = Array.FindIndex(args, x => x.Equals(key, StringComparison.OrdinalIgnoreCase)); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
    private async void NextClicked(object? sender, EventArgs e)
    {
        if (_page == WizardPage.Welcome) await BeginInstallAsync();
        else if (_page == WizardPage.Restart) { Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { UseShellExecute = true }); Close(); }
        else if (_page is WizardPage.Complete or WizardPage.Error) Close();
    }
    private void CancelClicked(object? sender, EventArgs e) { if (!_running) Close(); }
    private enum WizardPage { Welcome, Progress, Restart, Complete, Error }
}
