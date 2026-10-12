namespace BtVolumeGuard;

/// <summary>
/// Tray-only application shell (no main window). Skeleton: icon and Exit menu only;
/// the volume guard itself is not wired in yet (see docs/design.md).
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;

    public TrayApplicationContext()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "BtVolumeGuard",
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    protected override void ExitThreadCore()
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.ExitThreadCore();
    }
}
