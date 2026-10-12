using System.Diagnostics;

using BtVolumeGuard.Core;
using BtVolumeGuard.Core.Audio;
using BtVolumeGuard.Core.Logging;

namespace BtVolumeGuard;

/// <summary>
/// Tray-only application shell (no main window). Runs the endpoint monitor in diagnostic mode:
/// volume changes are logged, not reverted yet (see docs/design.md, roadmap step 1).
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly FileLog _log;
    private readonly EndpointVolumeMonitor _monitor;

    public TrayApplicationContext(DeviceNameMask mask)
    {
        _log = new FileLog(FileLog.DefaultPath);
        _monitor = new EndpointVolumeMonitor(mask, _log);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open log", null, (_, _) => OpenLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = $"BtVolumeGuard (diagnostic): {mask}",
            ContextMenuStrip = menu,
            Visible = true,
        };

        _monitor.Start();
    }

    protected override void ExitThreadCore()
    {
        _monitor.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.ExitThreadCore();
    }

    private void OpenLog()
    {
        if (File.Exists(_log.Path))
        {
            Process.Start(new ProcessStartInfo(_log.Path) { UseShellExecute = true });
        }
    }
}
