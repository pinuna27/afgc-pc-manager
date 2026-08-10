using AFGCPCManager.Core.Settings;
using AFGCPCManager.UI;
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
    public void ProgrammaticFormsDeclareThe96DpiDesignBaseline()
    {
        using var form = new Form();

        UiTheme.Apply(form);

        Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
        Assert.Equal(new SizeF(96F, 96F), form.AutoScaleDimensions);
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
