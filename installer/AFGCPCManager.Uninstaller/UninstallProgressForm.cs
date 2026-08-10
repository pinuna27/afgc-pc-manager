using AFGCPCManager.UI;
using System.Diagnostics;

namespace AFGCPCManager.Uninstaller;

internal sealed class UninstallProgressForm : Form
{
    private readonly string[] _args;
    private readonly Func<string[], Action<string>?, Task<int>> _operation;
    private readonly Label _heading = UiTheme.Heading(
        "Uninstalling AFGC PC Manager", dialog: true);
    private readonly Label _description = UiTheme.Body(
        "Please wait while controller visibility is restored and the selected components are removed.",
        muted: true);
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
    private readonly AfgcButton _primary = new("Please wait…", AfgcButtonKind.Primary)
    {
        Enabled = false
    };
    private readonly AfgcButton _secondary = new("Cancel") { Enabled = false };
    private readonly AfgcCallout _result = new(string.Empty) { Visible = false };
    private bool _running = true;

    internal int ResultCode { get; private set; }

    public UninstallProgressForm(string[] args,
        Func<string[], Action<string>?, Task<int>> operation)
    {
        _args = args;
        _operation = operation;
        Text = "AFGC PC Manager Uninstall";
        ClientSize = new Size(780, 540);
        MinimumSize = new Size(660, 460);
        UiTheme.StyleProgressLog(_progress);

        Panel header = UiTheme.FormHeader(
            "AFGC PC Manager Uninstall",
            "Safe removal and controller visibility recovery",
            compact: true);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(26, 24, 26, 24),
            BackColor = UiTheme.Surface
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_heading, 0, 0);
        _description.Margin = new Padding(0, 8, 0, 16);
        layout.Controls.Add(_description, 0, 1);
        _progressBar.Margin = new Padding(0, 0, 0, 14);
        layout.Controls.Add(_progressBar, 0, 2);
        layout.Controls.Add(_progress, 0, 3);
        _result.Dock = DockStyle.Bottom;
        _result.Height = 60;
        _result.Margin = new Padding(0, 14, 0, 0);
        layout.Controls.Add(_result, 0, 4);

        var card = new AfgcCard { Dock = DockStyle.Fill, Padding = new Padding(1) };
        card.Controls.Add(layout);
        var content = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 24, 28, 24),
            BackColor = UiTheme.Canvas
        };
        content.Controls.Add(card);

        Controls.Add(content);
        Controls.Add(UiTheme.FormFooter(_primary, _secondary));
        Controls.Add(header);
        _primary.Click += PrimaryClick;
        _secondary.Click += (_, _) => Close();
        FormClosing += (_, e) => { if (_running) e.Cancel = true; };
        Shown += async (_, _) => await RunAsync();
        UiTheme.Apply(this);
    }

    private async Task RunAsync()
    {
        try { ResultCode = await _operation(_args, Append); }
        catch (Exception ex)
        {
            ResultCode = 1;
            Append($"Uninstall failed: {ex.Message}");
        }
        _running = false;
        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.Value = 100;
        _result.Visible = true;
        if (ResultCode == 0)
        {
            _heading.Text = "Uninstall complete";
            _description.Text = "AFGC PC Manager was removed successfully.";
            _result.Tone = AfgcCalloutTone.Info;
            _result.Text = "Controller visibility was restored before application removal.";
            _primary.Text = "Finish";
            _primary.Enabled = true;
        }
        else if (ResultCode == 3010)
        {
            _heading.Text = "Restart required";
            _description.Text = "Windows must restart to finish removing the selected components.";
            _result.Tone = AfgcCalloutTone.Warning;
            _result.Text = "Pending cleanup will resume automatically after sign-in.";
            _primary.Text = "Restart now";
            _primary.Enabled = true;
            _secondary.Text = "Restart later";
            _secondary.Enabled = true;
        }
        else
        {
            _heading.Text = "Uninstall could not complete";
            _description.Text = "Review the log below. No unsafe cleanup will be attempted.";
            _result.Tone = AfgcCalloutTone.Danger;
            _result.Text = "The app stopped before an unsafe or incomplete removal step.";
            _primary.Text = "Close";
            _primary.Enabled = true;
        }
    }

    private void Append(string message)
    {
        if (InvokeRequired) { BeginInvoke(() => Append(message)); return; }
        _progress.AppendText(message + Environment.NewLine);
        _progress.SelectionStart = _progress.TextLength;
        _progress.ScrollToCaret();
    }

    private void PrimaryClick(object? sender, EventArgs e)
    {
        if (ResultCode == 3010)
        {
            try
            {
                Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0")
                    { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Windows could not restart automatically. Restart it from the Start menu.\n\n{ex.Message}",
                    "AFGC PC Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }
        Close();
    }
}
