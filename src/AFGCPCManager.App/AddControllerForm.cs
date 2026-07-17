using AFGCPCManager.Core.Devices;

namespace AFGCPCManager.App;

internal sealed class AddControllerForm : Form
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, DisplayMember = nameof(Item.Label) };
    public string? SelectedStableId => (_list.SelectedItem as Item)?.Id;
    public AddControllerForm(IReadOnlyList<DiscoveredFireController> candidates)
    {
        Text = "Add controller"; ClientSize = new(480, 280); StartPosition = FormStartPosition.CenterParent;
        foreach (var candidate in candidates) _list.Items.Add(new Item(candidate.Identity.StableId, candidate.Identity.DisplayName));
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new(8), FlowDirection = FlowDirection.RightToLeft };
        var add = new Button { Text = "Add", DialogResult = DialogResult.OK, Enabled = _list.Items.Count > 0 }; var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(add); buttons.Controls.Add(cancel); Controls.Add(_list); Controls.Add(buttons); AcceptButton = add; CancelButton = cancel;
        if (_list.Items.Count == 0) Controls.Add(new Label { Text = "No unregistered Fire controllers are currently connected.", Dock = DockStyle.Top, Height = 45, Padding = new(8) });
    }
    private sealed record Item(string Id, string Label);
}
