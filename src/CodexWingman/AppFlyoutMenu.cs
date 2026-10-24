using CodexWingman.Core;

namespace CodexWingman;

/// <summary>
/// One app line and its flyout, built from <see cref="TrayMenuLayout.AppFlyout"/> so every app gets
/// the same items in the same order with the same wording. The line itself never has a tooltip.
/// </summary>
internal sealed class AppFlyoutMenu
{
    public AppFlyoutMenu(ToolStripMenuItem line, EventHandler open, EventHandler repair, EventHandler status, bool openOnLaunch)
    {
        Line = line;
        Line.ToolTipText = null;
        OpenItem = new ToolStripMenuItem(TrayMenuLayout.Label(AppFlyoutItem.OpenWindow), null, open);
        RepairItem = new ToolStripMenuItem(TrayMenuLayout.Label(AppFlyoutItem.Repair), null, repair);
        StatusItem = new ToolStripMenuItem(TrayMenuLayout.Label(AppFlyoutItem.StatusDetails), null, status);
        OpenOnLaunchItem = new ToolStripMenuItem(TrayMenuLayout.Label(AppFlyoutItem.OpenOnLaunch))
        {
            CheckOnClick = true,
            Checked = openOnLaunch,
        };
        foreach (var item in TrayMenuLayout.AppFlyout)
        {
            Line.DropDownItems.Add(item switch
            {
                AppFlyoutItem.OpenWindow => OpenItem,
                AppFlyoutItem.Repair => RepairItem,
                AppFlyoutItem.StatusDetails => StatusItem,
                AppFlyoutItem.Separator => new ToolStripSeparator(),
                AppFlyoutItem.OpenOnLaunch => OpenOnLaunchItem,
                _ => throw new ArgumentOutOfRangeException(nameof(item)),
            });
        }
    }

    public ToolStripMenuItem Line { get; }
    public ToolStripMenuItem OpenItem { get; }
    public ToolStripMenuItem RepairItem { get; }
    public ToolStripMenuItem StatusItem { get; }
    public ToolStripMenuItem OpenOnLaunchItem { get; }

    public void SetBusy(bool value)
    {
        OpenItem.Enabled = !value;
        RepairItem.Enabled = !value;
    }

    /// <summary>A plain copy of a live menu, for the development --dump-menu command and its checks.</summary>
    public static IReadOnlyList<TrayMenuNode> Snapshot(ToolStripItemCollection items) =>
        items.Cast<ToolStripItem>().Select(item => new TrayMenuNode(
            item is ToolStripSeparator ? TrayMenuLayout.SeparatorText : item.Text ?? string.Empty,
            string.IsNullOrEmpty(item.ToolTipText) ? null : item.ToolTipText,
            item.Available,
            item.Enabled,
            item is ToolStripMenuItem { Checked: true },
            item is ToolStripMenuItem { DropDownItems.Count: > 0 } menu ? Snapshot(menu.DropDownItems) : null)).ToList();
}
