using AFGCPCManager.Core.Settings;
using AFGCPCManager.UI;
using System.Reflection;
using Xunit;

namespace AFGCPCManager.App.Tests;

public sealed class UiSmokeTests
{
    [Fact]
    public void EmbeddedProductIconLoadsForFormsAndTray()
    {
        using Icon icon = AfgcIcon.CreateIcon();
        using Bitmap bitmap = AfgcIcon.CreateBitmap();

        Assert.True(icon.Width >= 16);
        Assert.True(icon.Height >= 16);
        Assert.True(bitmap.Width >= 256);
        Assert.True(bitmap.Height >= 256);
    }

    [Fact]
    public async Task PrimaryFormsConstructWithSharedProductChrome()
    {
        var runtime = new BridgeRuntime();
        try
        {
            using var main = new MainForm(runtime);
            using var settings = new SettingsForm(new SettingsDocument(), null,
                () => new DiagnosticSnapshot("test", "Waiting", 0, 0, 0, [],
                    "Available", false, []));
            using var add = new AddControllerForm([]);

            Assert.NotNull(main.Icon);
            Assert.NotNull(settings.Icon);
            Assert.NotNull(add.Icon);
            Assert.Contains(Controls<AfgcTabControl>(settings), _ => true);
            Assert.Contains(Controls<AfgcStatusBanner>(main), _ => true);
        }
        finally
        {
            await runtime.DisposeAsync();
        }
    }

    [Fact]
    public async Task MainFormCanOwnUiDispatchBeforeItsFirstShow()
    {
        var runtime = new BridgeRuntime();
        try
        {
            using var main = new MainForm(runtime);

            _ = main.Handle;

            Assert.True(main.IsHandleCreated);
            Assert.False(main.Visible);
        }
        finally
        {
            await runtime.DisposeAsync();
        }
    }

    [Fact]
    public void ProgrammaticFormsDeclareThe96DpiDesignBaseline()
    {
        using var form = new Form();

        UiTheme.Apply(form);

        Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
        Assert.Equal(new SizeF(96F, 96F), form.AutoScaleDimensions);
    }

    [Fact]
    public void SharedThemeFitsOversizedFormsInsideTheWorkingArea()
    {
        using var form = new Form
        {
            Size = new Size(2400, 1600),
            MinimumSize = new Size(2000, 1200),
            ShowInTaskbar = false,
            Opacity = 0.01
        };
        UiTheme.Apply(form);

        form.Show();
        Application.DoEvents();

        Rectangle workingArea = Screen.FromControl(form).WorkingArea;
        Assert.True(workingArea.Contains(form.Bounds),
            $"Form bounds {form.Bounds} exceed working area {workingArea}.");
        Assert.True(form.MinimumSize.Width <= workingArea.Width);
        Assert.True(form.MinimumSize.Height <= workingArea.Height);
    }

    [Fact]
    public async Task ControllerListShowsIdentificationLightPatternAndOffState()
    {
        Assert.Equal("■ — — —", IdentificationLightDisplay.Format(0b1000));
        Assert.Equal("— ■ — —", IdentificationLightDisplay.Format(0b0100));
        Assert.Equal("— — ■ —", IdentificationLightDisplay.Format(0b0010));
        Assert.Equal("— — — ■", IdentificationLightDisplay.Format(0b0001));

        var runtime = new BridgeRuntime();
        try
        {
            using var main = new MainForm(runtime);
            main.ApplyRows([
                new ControllerRowModel("one", "First", 1, true, 2, null, 0b0101),
                new ControllerRowModel("two", "Second", 2, false, null, null, null)
            ]);

            DataGridView grid = Assert.Single(Controls<DataGridView>(main));
            Assert.False(grid.ShowCellToolTips);
            Assert.Equal("— ■ — ■", grid.Rows[0].Cells["Lights"].Value);
            Assert.Equal("Not controlled", grid.Rows[1].Cells["Lights"].Value);
            DataGridViewColumn lightsColumn = Assert.IsType<DataGridViewTextBoxColumn>(grid.Columns["Lights"]);
            Assert.Equal("■ = light on    — = light off", lightsColumn.ToolTipText);
        }
        finally
        {
            await runtime.DisposeAsync();
        }
    }

    [Fact]
    public async Task RightClickSelectsControllerAndSelectionSurvivesRefresh()
    {
        var runtime = new BridgeRuntime();
        try
        {
            using var main = new MainForm(runtime);
            ControllerRowModel[] rows =
            [
                new("one", "First", 1, true, 1, null),
                new("two", "Second", 2, true, 2, null)
            ];
            main.ApplyRows(rows);
            DataGridView grid = Assert.Single(Controls<DataGridView>(main));
            Assert.Empty(grid.SelectedRows.Cast<DataGridViewRow>());

            MethodInfo onCellMouseDown = typeof(DataGridView).GetMethod(
                "OnCellMouseDown", BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null, [typeof(DataGridViewCellMouseEventArgs)], modifiers: null)!;
            onCellMouseDown.Invoke(grid,
            [
                new DataGridViewCellMouseEventArgs(0, 1, 1, 1,
                    new MouseEventArgs(MouseButtons.Right, 1, 1, 1, 0))
            ]);

            Assert.Single(grid.SelectedRows.Cast<DataGridViewRow>());
            Assert.Equal("two", grid.SelectedRows[0].Tag);

            main.ApplyRows(rows);
            Assert.Single(grid.SelectedRows.Cast<DataGridViewRow>());
            Assert.Equal("two", grid.SelectedRows[0].Tag);
        }
        finally
        {
            await runtime.DisposeAsync();
        }
    }

    [Fact]
    public async Task ControllerListDescribesTheSelectedOutputBackend()
    {
        var runtime = new BridgeRuntime();
        try
        {
            using var main = new MainForm(runtime);
            main.ApplyRows([
                new ControllerRowModel("xinput", "Xbox pad", 1, true, 2, null,
                    OutputMode: GamepadOutputMode.XInput),
                new ControllerRowModel("directinput", "DirectInput pad", 2, true, 7, null,
                    OutputMode: GamepadOutputMode.DirectInput),
                new ControllerRowModel("missing", "Missing bus", 3, true, null, null,
                    OutputMode: GamepadOutputMode.XInput)
            ]);

            DataGridView grid = Assert.Single(Controls<DataGridView>(main));
            Assert.Contains("Xbox pad (XInput)",
                grid.Rows[0].Cells["Controller"].Value?.ToString());
            Assert.Contains("DirectInput pad (DirectInput)",
                grid.Rows[1].Cells["Controller"].Value?.ToString());
            Assert.Equal("Xbox output 2", grid.Rows[0].Cells["Output"].Value);
            Assert.Equal("vJoy 7", grid.Rows[1].Cells["Output"].Value);
            Assert.Equal("Needs ViGEmBus", grid.Rows[2].Cells["Status"].Value);
        }
        finally
        {
            await runtime.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("Amazon Fire Game Controller", GamepadOutputMode.XInput,
        "Amazon Fire Game Controller (XInput)")]
    [InlineData("Amazon Fire Game Controller (Corrected)", GamepadOutputMode.DirectInput,
        "Amazon Fire Game Controller (DirectInput)")]
    [InlineData("Pad (DirectInput) (XInput)", GamepadOutputMode.XInput,
        "Pad (XInput)")]
    public void VirtualControllerNameUsesSelectedBackend(string original,
        GamepadOutputMode mode, string expected) =>
        Assert.Equal(expected, VirtualControllerDisplayName.Format(original, mode));

    [Fact]
    public void IdentificationLightSettingReflectsSavedValue()
    {
        using var settings = new SettingsForm(new SettingsDocument
        {
            Application = new AppSettings { ControlIdentificationLights = true }
        }, null, () => new DiagnosticSnapshot("test", "Waiting", 0, 0, 0, [],
            "Available", false, []));

        CheckBox option = Assert.Single(Controls<CheckBox>(settings), checkBox =>
            checkBox.Text == "Use controller identification lights");
        Assert.True(option.Checked);
    }

    [Theory]
    [InlineData(GamepadOutputMode.XInput, "Xbox (XInput)", "Native game support; up to 4 controllers.")]
    [InlineData(GamepadOutputMode.DirectInput, "vJoy (DirectInput)", "Over 4 controllers; some games need a translator.")]
    public void OutputModeSettingShowsShortTradeoff(
        GamepadOutputMode mode, string label, string help)
    {
        using var settings = new SettingsForm(new SettingsDocument
        {
            Application = new AppSettings { OutputMode = mode }
        }, null, () => new DiagnosticSnapshot("test", "Waiting", 0, 0, 0, [],
            "Available", false, []));

        RadioButton option = Assert.Single(Controls<RadioButton>(settings), radio =>
            radio.Text == label);
        Assert.True(option.Checked);
        Assert.Equal(help, option.AccessibleDescription);
        ToolTip toolTip = Assert.IsType<ToolTip>(typeof(SettingsForm)
            .GetField("_outputModeToolTip",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!
            .GetValue(settings));
        Assert.Equal(help, toolTip.GetToolTip(option));
        Assert.False(toolTip.ShowAlways);
    }

    [Fact]
    public void OutputModeChoiceIsIncludedInSavedSettings()
    {
        using var settings = new SettingsForm(new SettingsDocument
        {
            Application = new AppSettings { OutputMode = GamepadOutputMode.DirectInput }
        }, null, () => new DiagnosticSnapshot("test", "Waiting", 0, 0, 0, [],
            "Available", false, []));
        RadioButton xInput = Assert.Single(Controls<RadioButton>(settings), radio =>
            radio.Text == "Xbox (XInput)");
        AfgcButton save = Assert.Single(Controls<AfgcButton>(settings), button =>
            button.Text == "Save changes");

        xInput.Checked = true;
        Assert.NotNull(save);
        RadioButton defaults = Assert.IsType<RadioButton>(typeof(SettingsForm)
            .GetField("_defaults", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!
            .GetValue(settings));
        defaults.Checked = false;
        typeof(SettingsForm).GetMethod("SaveAndClose",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!.Invoke(settings, null);

        Assert.NotNull(settings.Result);
        Assert.Equal(GamepadOutputMode.XInput,
            settings.Result.Application.OutputMode);
    }

    [Fact]
    public void GeneralSettingsScrollExtentIncludesEveryCompactPreference()
    {
        using var settings = new SettingsForm(new SettingsDocument(), null,
            () => new DiagnosticSnapshot("test", "Waiting", 0, 0, 0, [],
                "Available", false, []));
        settings.Size = settings.MinimumSize;
        settings.ShowInTaskbar = false;
        settings.Opacity = 0.01;
        settings.Show();
        Application.DoEvents();
        settings.PerformLayout();

        FlowLayoutPanel preferences = Assert.Single(
            Controls<FlowLayoutPanel>(settings), panel =>
                panel.Name == "GeneralPreferences");
        preferences.PerformLayout();
        int requiredHeight = preferences.Padding.Vertical +
            preferences.Controls.Cast<Control>()
                .Sum(control => control.Height + control.Margin.Vertical);

        Assert.True(preferences.AutoScroll);
        Assert.True(preferences.AutoScrollMinSize.Height >= requiredHeight);
        Assert.All(preferences.Controls.Cast<Control>().Where(control =>
                !Controls<RadioButton>(control).Any()),
            control => Assert.True(control.Height <= 48));
        Assert.Equal("Show update notifications",
            Assert.IsType<CheckBox>(preferences.Controls[^1].Controls[0]).Text);

        Control lastPreference = preferences.Controls[^1];
        preferences.AutoScrollPosition = new Point(0,
            preferences.AutoScrollMinSize.Height);
        preferences.PerformLayout();
        Assert.True(lastPreference.Bottom <=
            preferences.ClientSize.Height - preferences.Padding.Bottom + 2,
            $"Last preference bottom {lastPreference.Bottom} exceeds the " +
            $"scroll viewport {preferences.ClientSize.Height}.");
    }

    [Theory]
    [InlineData("Settings", 9F)]
    [InlineData("Add controller", 11.25F)]
    [InlineData("Open game controllers", 13.5F)]
    [InlineData("Save changes", 18F)]
    public void SharedButtonsMeasureTheEntireLabel(string text, float fontSize)
    {
        using var button = new AfgcButton(text);
        using var font = new Font("Segoe UI", fontSize, FontStyle.Regular,
            GraphicsUnit.Point);
        button.Font = font;

        Size preferred = button.GetPreferredSize(Size.Empty);
        Size label = TextRenderer.MeasureText(text, font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);

        Assert.True(preferred.Width > label.Width);
        Assert.True(preferred.Height > label.Height);
    }

    private static IEnumerable<T> Controls<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match) yield return match;
            foreach (T descendant in Controls<T>(child)) yield return descendant;
        }
    }
}
