using System.Diagnostics;

using BtVolumeGuard.Core;
using BtVolumeGuard.Core.Audio;
using BtVolumeGuard.Core.Logging;

namespace BtVolumeGuard;

/// <summary>
/// Tray-only application shell (no main window). Runs the volume guard, which reverts
/// headset-induced jumps to maximum and logs every change (see docs/design.md).
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly FileLog _log;
    private readonly EndpointVolumeGuard _guard;

    public TrayApplicationContext(DeviceNameMask mask)
    {
        _log = new FileLog(FileLog.DefaultPath);
        _guard = new EndpointVolumeGuard(mask, new VolumeJumpPolicy(), _log);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open log", null, (_, _) => OpenLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = $"BtVolumeGuard: {mask}",
            ContextMenuStrip = menu,
            Visible = true,
        };

        _guard.Start();
    }

    protected override void ExitThreadCore()
    {
        _guard.Dispose();
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
