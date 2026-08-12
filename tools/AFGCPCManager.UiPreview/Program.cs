using AFGCPCManager.App;
using AFGCPCManager.Bootstrapper;
using AFGCPCManager.Core.Devices;
using AFGCPCManager.Core.Settings;
using AFGCPCManager.Setup.Core.Dependencies;
using AFGCPCManager.Uninstaller;
using AFGCPCManager.UI;
using System.Diagnostics;
using System.Drawing.Imaging;

namespace AFGCPCManager.UiPreview;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        float simulatedScale = float.TryParse(Value(args, "--simulate-scale"), out float scale)
            ? scale
            : 0F;
        Application.SetHighDpiMode(simulatedScale > 0F
            ? HighDpiMode.DpiUnaware
            : HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        try
        {
            if (args.Contains("--audit-layout", StringComparer.OrdinalIgnoreCase))
                return AuditLayouts(simulatedScale);
            return Run(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        string surface = args.FirstOrDefault() ?? "main";
        string? capturePath = Value(args, "--capture");
        bool dumpLayout = args.Contains("--dump-layout", StringComparer.OrdinalIgnoreCase);
        using Form form = Create(surface);
        if (int.TryParse(Value(args, "--width"), out int width)) form.Width = width;
        if (int.TryParse(Value(args, "--height"), out int height)) form.Height = height;
        if (capturePath is not null)
        {
            form.Shown += async (_, _) =>
            {
                await Task.Delay(surface == "uninstall-progress" ? 450 : 150);
                if (dumpLayout) DumpLayout(form);
                string fullPath = Path.GetFullPath(capturePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(fullPath, ImageFormat.Png);
                form.Hide();
                form.Dispose();
                Application.ExitThread();
            };
        }
        Application.Run(form);
        return 0;
    }

    private static void DumpLayout(Control root, int depth = 0)
    {
        Console.WriteLine($"{new string(' ', depth * 2)}{root.GetType().Name} " +
            $"Text='{root.Text}' Bounds={root.Bounds} Client={root.ClientSize} " +
            $"Preferred={root.PreferredSize} Visible={root.Visible} Dpi={root.DeviceDpi}");
        foreach (Control child in root.Controls) DumpLayout(child, depth + 1);
    }

    private static int AuditLayouts(float simulatedScale)
    {
        (string Surface, Size Minimum)[] cases =
        [
            ("main", new Size(760, 480)),
            ("settings", new Size(760, 580)),
            ("settings-controller", new Size(760, 580)),
            ("settings-diagnostics", new Size(760, 580)),
            ("add", new Size(540, 380)),
            ("add-empty", new Size(540, 380)),
            ("setup", new Size(660, 460)),
            ("uninstall", new Size(590, 430)),
            ("uninstall-progress", new Size(660, 460))
        ];
        var failures = new List<string>();
        foreach ((string surface, Size minimum) in cases)
        {
            foreach (bool useMinimum in new[] { false, true })
            {
                using Form form = Create(surface);
                if (useMinimum) form.Size = minimum;
                if (simulatedScale > 0F && Math.Abs(simulatedScale - 1F) > 0.001F)
                    form.Scale(new SizeF(simulatedScale, simulatedScale));
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = Screen.PrimaryScreen?.WorkingArea.Location ?? Point.Empty;
                form.Opacity = 0.01;
                form.Show();
                PumpMessages(surface == "uninstall-progress" ? 300 : 40);
                string context = $"{surface}/{(useMinimum ? "minimum" : "default")}";
                AuditControlTree(form, context, failures);
                form.Hide();
            }
        }

        string scaleLabel = simulatedScale > 0F
            ? $"simulated {simulatedScale:P0}"
            : $"native {Control.DefaultFont.Height}px font / {GetSystemDpi()} DPI";
        if (failures.Count == 0)
        {
            Console.WriteLine($"LAYOUT AUDIT PASSED: {cases.Length * 2} surfaces at {scaleLabel}.");
            return 0;
        }
        foreach (string failure in failures) Console.Error.WriteLine(failure);
        Console.Error.WriteLine($"LAYOUT AUDIT FAILED: {failures.Count} issue(s) at {scaleLabel}.");
        return 1;
    }

    private static void PumpMessages(int milliseconds)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        do
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        while (stopwatch.ElapsedMilliseconds < milliseconds);
        Application.DoEvents();
    }

    private static void AuditControlTree(Control root, string context, List<string> failures)
    {
        foreach (Control control in Descendants(root).Where(control => control.Visible))
        {
            string name = $"{context}: {control.GetType().Name} '{control.Text}'";
            if (control is AfgcButton button)
            {
                Size preferred = button.GetPreferredSize(Size.Empty);
                if (button.ClientSize.Width + 1 < preferred.Width ||
                    button.ClientSize.Height + 1 < preferred.Height)
                    failures.Add($"{name} is {button.ClientSize}, needs {preferred}.");
            }
            if (control is ComboBox combo &&
                (combo.ClientSize.Width <= 0 || combo.ClientSize.Height <= 0))
                failures.Add($"{name} has an empty {combo.ClientSize} client area.");
            if (control is Label { AutoSize: false } label && label.Text.Length > 0)
            {
                int width = Math.Max(1, label.ClientSize.Width - label.Padding.Horizontal);
                Size needed = TextRenderer.MeasureText(label.Text, label.Font,
                    new Size(width, int.MaxValue), TextFormatFlags.WordBreak |
                    TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                int availableHeight = label.ClientSize.Height - label.Padding.Vertical;
                if (needed.Height > availableHeight + 1)
                    failures.Add($"{name} needs {needed.Height}px text height; " +
                        $"only {availableHeight}px is available.");
            }
            if (control.AutoSize && control is Label or CheckBox or RadioButton or AfgcButton &&
                IsClippedByFixedAncestor(control))
                failures.Add($"{name} extends outside a non-scrollable ancestor.");
            if (control is AfgcTabControl tabs)
            {
                for (int index = 0; index < tabs.TabCount; index++)
                {
                    Rectangle bounds = tabs.GetTabRect(index);
                    Size text = TextRenderer.MeasureText(tabs.TabPages[index].Text,
                        UiTheme.BodyFont, Size.Empty, TextFormatFlags.NoPrefix |
                        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    if (text.Width > bounds.Width - UiTheme.Scale(tabs, 16) ||
                        text.Height > bounds.Height)
                        failures.Add($"{context}: tab '{tabs.TabPages[index].Text}' " +
                            $"text {text} does not fit {bounds.Size}.");
                }
            }
            if (control is DataGridView grid) AuditGrid(grid, context, failures);
        }
    }

    private static void AuditGrid(DataGridView grid, string context, List<string> failures)
    {
        int minimumHeight = TextRenderer.MeasureText("Ag", grid.Font).Height +
            UiTheme.Scale(grid, 8);
        if (grid.ColumnHeadersHeight < minimumHeight)
            failures.Add($"{context}: grid header height {grid.ColumnHeadersHeight} " +
                $"is below {minimumHeight}.");
        foreach (DataGridViewRow row in grid.Rows)
            if (row.Visible && row.Height < minimumHeight)
                failures.Add($"{context}: grid row height {row.Height} is below {minimumHeight}.");

        foreach (DataGridViewColumn column in grid.Columns)
        {
            if (!column.Visible) continue;
            int needed = TextRenderer.MeasureText(column.HeaderText,
                grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font).Width;
            foreach (DataGridViewRow row in grid.Rows)
            {
                string text = Convert.ToString(row.Cells[column.Index].FormattedValue) ?? string.Empty;
                needed = Math.Max(needed, TextRenderer.MeasureText(text,
                    row.Cells[column.Index].InheritedStyle.Font ?? grid.Font).Width);
            }
            needed += grid.DefaultCellStyle.Padding.Horizontal + UiTheme.Scale(grid, 8);
            if (column.Width < needed)
                failures.Add($"{context}: grid column '{column.HeaderText}' is " +
                    $"{column.Width}px wide and needs {needed}px for preview data.");
        }
    }

    private static bool IsClippedByFixedAncestor(Control control)
    {
        Rectangle bounds = control.RectangleToScreen(control.ClientRectangle);
        for (Control? parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is ScrollableControl { AutoScroll: true }) return false;
            Rectangle parentBounds = parent.RectangleToScreen(parent.ClientRectangle);
            parentBounds.Inflate(2, 2);
            if (!parentBounds.Contains(bounds)) return true;
        }
        return false;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child)) yield return descendant;
        }
    }

    private static int GetSystemDpi()
    {
        using Graphics graphics = Graphics.FromHwnd(IntPtr.Zero);
        return (int)Math.Round(graphics.DpiX);
    }

    private static Form Create(string surface) => surface switch
    {
        "main" => MainPreview(),
        "settings" => SettingsPreview(0),
        "settings-controller" => SettingsPreview(1),
        "settings-diagnostics" => SettingsPreview(2),
        "add" => AddPreview(),
        "add-empty" => new AddControllerForm([]),
        "setup" => new SetupWizardForm([]),
        "uninstall" => new UninstallForm(new DependencyUninstallOptions(false, false, false)),
        "uninstall-progress" => new UninstallProgressForm([], FakeUninstallAsync),
        _ => throw new ArgumentException($"Unknown UI surface '{surface}'.")
    };

    private static Form MainPreview()
    {
        var form = new MainForm(new BridgeRuntime());
        form.ApplyRows([
            new ControllerRowModel("preview-controller", "Amazon Fire Game Controller",
                1, true, 2, null, 0b0001),
            new ControllerRowModel("preview-controller-2", "Amazon Fire Game Controller",
                2, false, null, "Reconnect required", 0b0010)
        ]);
        form.SetStatus("Running — 1 of 2 Fire controllers mapped.");
        return form;
    }

    private static Form SettingsPreview(int tabIndex)
    {
        var settings = new SettingsDocument
        {
            Controllers = [new RegisteredController
            {
                StableId = "preview-controller",
                DisplayName = "Amazon Fire Game Controller",
                RegistrationOrder = 1,
                PreferredVJoyId = 2,
                LastSeenUtc = DateTimeOffset.UtcNow
            }]
        };
        var form = new SettingsForm(settings, null, () => new DiagnosticSnapshot(
            "0.1.0", "Running — 1 of 1 Fire controllers mapped.", 1, 1, 1,
            [new ControllerRowModel("preview-controller", "Amazon Fire Game Controller",
                1, true, 2, null, 0b0001)], "Available", true,
            ["15:24:10 Controller isolation verified.", "15:24:10 vJoy 2 acquired."]));
        Find<TabControl>(form).SelectedIndex = tabIndex;
        return form;
    }

    private static Form AddPreview() => new AddControllerForm([
        new DiscoveredFireController(
            new FireControllerIdentity("preview-controller", "Amazon Fire Game Controller",
                FireControllerConstants.VendorId, FireControllerConstants.ProductId),
            [], true)
    ]);

    private static async Task<int> FakeUninstallAsync(string[] _, Action<string>? progress)
    {
        progress?.Invoke("Restoring physical controller visibility...");
        await Task.Delay(80);
        progress?.Invoke("Removing AFGC PC Manager application files...");
        await Task.Delay(80);
        progress?.Invoke("Removed 5 files.");
        return 0;
    }

    private static string? Value(string[] args, string key)
    {
        int index = Array.FindIndex(args,
            value => value.Equals(key, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static T Find<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match) return match;
            try { return Find<T>(child); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException($"Preview control {typeof(T).Name} was not found.");
    }
}
