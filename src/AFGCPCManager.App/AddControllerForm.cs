using AFGCPCManager.Core.Devices;
using AFGCPCManager.UI;

namespace AFGCPCManager.App;

internal sealed class AddControllerForm : Form
{
    private readonly ListBox _list = new()
    {
        Dock = DockStyle.Fill,
        DisplayMember = nameof(Item.Label),
        BorderStyle = BorderStyle.None,
        IntegralHeight = false,
        DrawMode = DrawMode.OwnerDrawFixed,
        ItemHeight = 54
    };

    public string? SelectedStableId => (_list.SelectedItem as Item)?.Id;

    public AddControllerForm(IReadOnlyList<DiscoveredFireController> candidates)
    {
        Text = "Add controller";
        ClientSize = new Size(640, 460);
        MinimumSize = new Size(540, 380);

        foreach (DiscoveredFireController candidate in candidates)
            _list.Items.Add(new Item(candidate.Identity.StableId,
                candidate.Identity.DisplayName));
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        _list.DrawItem += DrawControllerItem;
        _list.HandleCreated += (_, _) => ApplyListMetrics();
        _list.DpiChangedAfterParent += (_, _) => ApplyListMetrics();

        var add = new AfgcButton("Add controller", AfgcButtonKind.Primary)
        {
            DialogResult = DialogResult.OK,
            Enabled = _list.Items.Count > 0
        };
        var cancel = new AfgcButton("Cancel") { DialogResult = DialogResult.Cancel };

        Panel header = UiTheme.FormHeader(
            "Add a Fire controller",
            "Choose a connected controller to manage.",
            compact: true);

        var pairingHint = new AfgcCallout(
            "Hold the Home button on the controller for 10 seconds to enter pairing mode.")
        {
            Dock = DockStyle.Top,
            Height = 58,
            Margin = Padding.Empty
        };

        var card = new AfgcCard { Dock = DockStyle.Fill, Padding = new Padding(1) };
        if (_list.Items.Count > 0)
        {
            card.Controls.Add(_list);
            _list.DoubleClick += (_, _) =>
            {
                if (_list.SelectedIndex >= 0) DialogResult = DialogResult.OK;
            };
        }
        else
        {
            card.Controls.Add(new Label
            {
                Text = "No unregistered Fire controllers are connected.\n\n" +
                       "Hold the Home button on the controller for 10 seconds " +
                       "to enter pairing mode, then reopen this window.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = UiTheme.TextMuted,
                Padding = new Padding(24)
            });
        }

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            BackColor = UiTheme.Canvas
        };
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            BackColor = UiTheme.Canvas
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(pairingHint, 0, 0);
        body.Controls.Add(card, 0, 1);
        content.Controls.Add(body);

        Controls.Add(content);
        Controls.Add(UiTheme.FormFooter(add, cancel));
        Controls.Add(header);
        AcceptButton = add;
        CancelButton = cancel;
        UiTheme.Apply(this, centerParent: true);
    }

    private void DrawControllerItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        Color background = selected ? UiTheme.PrimarySoft : UiTheme.Surface;
        Color foreground = UiTheme.Text;
        using var backgroundBrush = new SolidBrush(background);
        e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
        int inset = UiTheme.Scale(_list, 18);
        Rectangle textBounds = new(e.Bounds.X + inset, e.Bounds.Y,
            e.Bounds.Width - (inset * 2), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, ((Item)_list.Items[e.Index]).Label,
            UiTheme.BodyFont, textBounds, foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        using var border = new Pen(UiTheme.Border);
        e.Graphics.DrawLine(border, e.Bounds.Left, e.Bounds.Bottom - 1,
            e.Bounds.Right, e.Bounds.Bottom - 1);
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
    }

    private void ApplyListMetrics() => _list.ItemHeight = UiTheme.Scale(_list, 54);

    private sealed record Item(string Id, string Label);
}
