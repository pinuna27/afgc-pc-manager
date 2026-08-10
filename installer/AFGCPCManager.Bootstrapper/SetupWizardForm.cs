using AFGCPCManager.UI;
using System.Diagnostics;

namespace AFGCPCManager.Bootstrapper;

internal sealed class SetupWizardForm : Form
{
    private readonly string[] _originalArgs;
    private readonly Label _heading = UiTheme.Heading(string.Empty, dialog: true);
    private readonly Label _description = UiTheme.Body(string.Empty, muted: true);
    private readonly TextBox _progress = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill
    };
    private readonly ProgressBar _progressBar = new()
    {
        Dock = DockStyle.Top,
        Height = 6,
        Style = ProgressBarStyle.Marquee,
        MarqueeAnimationSpeed = 24
    };
    private readonly TextBox _destination = new() { Dock = DockStyle.Top, Height = 30 };
    private readonly Panel _content = new()
    {
        Dock = DockStyle.Fill,
        Padding = new Padding(28, 24, 28, 24),
        BackColor = UiTheme.Canvas
    };
    private readonly AfgcButton _back = new("Back", AfgcButtonKind.Quiet) { Enabled = false };
    private readonly AfgcButton _next = new("Install", AfgcButtonKind.Primary);
    private readonly AfgcButton _cancel = new("Cancel");
    private bool _running;
    private WizardPage _page = WizardPage.Welcome;

    internal int ResultCode { get; private set; }

    public SetupWizardForm(string[] args)
    {
        _originalArgs = args.Where(argument =>
                !argument.Equals("--wizard-run", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Text = "AFGC PC Manager Setup";
        ClientSize = new Size(780, 540);
        MinimumSize = new Size(660, 460);
        UiTheme.StyleInput(_destination);
        UiTheme.StyleProgressLog(_progress);

        Panel header = UiTheme.FormHeader(
            "AFGC PC Manager Setup",
            "Install and maintain controller compatibility components",
            compact: true);
        Controls.Add(_content);
        Controls.Add(UiTheme.FormFooter(_next, _cancel, _back));
        Controls.Add(header);

        _next.Click += NextClicked;
        _cancel.Click += CancelClicked;
        FormClosing += (_, e) => { if (_running) e.Cancel = true; };
        ShowWelcome();
        if (args.Any(argument => argument.Equals(
                "--wizard-run", StringComparison.OrdinalIgnoreCase)))
            Shown += async (_, _) => await BeginInstallAsync();
        UiTheme.Apply(this);
    }

    private void ShowWelcome()
    {
        _page = WizardPage.Welcome;
        _content.Controls.Clear();
        _heading.Text = OperationTitle();
        _description.Text = "Setup checks the app, vJoy, and HidHide and changes only the components that need attention.";
        _destination.Text = Get(_originalArgs, "--install-dir")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "AFGC PC Manager");

        var locationLabel = UiTheme.SectionHeading("Install location");
        locationLabel.Margin = new Padding(0, Px(22), 0, Px(6));
        var details = new AfgcCallout(
            "Windows may ask for administrator approval. If a signed dependency wizard opens, complete it before returning to setup.");
        details.Dock = DockStyle.Top;
        details.Height = Px(76);
        details.Margin = new Padding(0, Px(18), 0, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(Px(26), Px(24), Px(26), Px(24)),
            BackColor = UiTheme.Surface
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _description.MaximumSize = new Size(640, 0);
        layout.Controls.Add(_heading, 0, 0);
        _description.Margin = new Padding(0, Px(8), 0, 0);
        layout.Controls.Add(_description, 0, 1);
        layout.Controls.Add(locationLabel, 0, 2);
        layout.Controls.Add(_destination, 0, 3);
        layout.Controls.Add(details, 0, 4);
        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = UiTheme.Surface
        };
        scrollHost.Controls.Add(layout);
        ShowCard(scrollHost);
    }

    private async Task BeginInstallAsync()
    {
        if (_running) return;
        _running = true;
        _next.Enabled = false;
        _back.Enabled = false;
        _cancel.Enabled = false;
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
        _page = WizardPage.Progress;
        _content.Controls.Clear();
        _heading.Text = "Installing";
        _description.Text = "Please wait while setup prepares AFGC PC Manager and its controller components.";
        _progress.Clear();
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(Px(26), Px(24), Px(26), Px(24)),
            BackColor = UiTheme.Surface
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(_heading, 0, 0);
        _description.Margin = new Padding(0, Px(8), 0, Px(16));
        layout.Controls.Add(_description, 0, 1);
        _progressBar.Margin = new Padding(0, 0, 0, Px(14));
        layout.Controls.Add(_progressBar, 0, 2);
        layout.Controls.Add(_progress, 0, 3);
        ShowCard(layout);
    }

    private void ShowComplete()
    {
        _page = WizardPage.Complete;
        ShowMessage("Setup complete", "AFGC PC Manager is ready to use.",
            "The controller bridge and required components passed setup checks.",
            AfgcCalloutTone.Info);
        _next.Text = "Finish";
        _next.Enabled = true;
        _cancel.Visible = false;
    }

    private void ShowRestart()
    {
        _page = WizardPage.Restart;
        ShowMessage("Restart required",
            "Windows must restart before setup can continue.",
            "Setup will resume automatically after you sign in. You can restart now or later.",
            AfgcCalloutTone.Warning);
        _next.Text = "Restart now";
        _next.Enabled = true;
        _cancel.Text = "Restart later";
        _cancel.Enabled = true;
    }

    private void ShowError(string error)
    {
        _page = WizardPage.Error;
        ShowMessage("Setup could not complete",
            "No unsafe setup step will be continued.", error, AfgcCalloutTone.Danger);
        _next.Text = "Close";
        _next.Enabled = true;
        _cancel.Visible = false;
    }

    private void ShowMessage(string heading, string description, string detail,
        AfgcCalloutTone tone)
    {
        _content.Controls.Clear();
        _heading.Text = heading;
        _description.Text = description;
        var callout = new AfgcCallout(detail, tone)
        {
            Dock = DockStyle.Top,
            Height = Px(72),
            Margin = new Padding(0, Px(24), 0, 0)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(Px(26), Px(24), Px(26), Px(24)),
            BackColor = UiTheme.Surface
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(_heading, 0, 0);
        _description.Margin = new Padding(0, Px(8), 0, 0);
        layout.Controls.Add(_description, 0, 1);
        layout.Controls.Add(callout, 0, 2);
        ShowCard(layout);
    }

    private void ShowCard(Control body)
    {
        var card = new AfgcCard
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(Px(1))
        };
        card.Controls.Add(body);
        _content.Controls.Add(card);
    }

    private void AppendProgress(string message)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendProgress(message)); return; }
        _progress.AppendText(message + Environment.NewLine);
        _progress.SelectionStart = _progress.TextLength;
        _progress.ScrollToCaret();
    }

    private int Px(int logicalPixels) => UiTheme.Scale(this, logicalPixels);

    private string OperationTitle() =>
        _originalArgs.Contains("--repair", StringComparer.OrdinalIgnoreCase)
            ? "Repair AFGC PC Manager"
            : _originalArgs.Contains("--update", StringComparer.OrdinalIgnoreCase)
                ? "Update AFGC PC Manager"
                : "Install AFGC PC Manager";

    private static string[] WithDestination(string[] args, string destination) =>
        args.Contains("--install-dir", StringComparer.OrdinalIgnoreCase)
            ? args
            : [.. args, "--install-dir", destination];

    private static string? Get(string[] args, string key)
    {
        int index = Array.FindIndex(args,
            value => value.Equals(key, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private async void NextClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_page == WizardPage.Welcome) await BeginInstallAsync();
            else if (_page == WizardPage.Restart)
            {
                Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0")
                    { UseShellExecute = true });
                Close();
            }
            else if (_page is WizardPage.Complete or WizardPage.Error) Close();
        }
        catch (Exception ex)
        {
            ResultCode = 1;
            ShowError(_page == WizardPage.Restart
                ? $"Windows could not restart automatically. Restart it from the Start menu.\n\n{ex.Message}"
                : $"Setup could not complete.\n\n{ex.Message}");
        }
    }

    private void CancelClicked(object? sender, EventArgs e)
    {
        if (!_running) Close();
    }

    private enum WizardPage { Welcome, Progress, Restart, Complete, Error }
}
