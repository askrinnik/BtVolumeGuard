using System.ComponentModel;
using System.Diagnostics;

using BtVolumeGuard.Core;
using BtVolumeGuard.Core.Audio;
using BtVolumeGuard.Core.Logging;
using BtVolumeGuard.Core.Settings;

using Microsoft.Win32;

namespace BtVolumeGuard;

/// <summary>
/// Tray-only application shell (no main window). Runs the volume guard, which reverts
/// headset-induced jumps to maximum and logs every change (see docs/design.md), and offers
/// its settings in the tray menu.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private const int MaxTooltipLength = 127;

    private readonly string? _maskOverride;
    private readonly FileLog _log;
    private readonly NotifyIcon _trayIcon;
    private readonly SynchronizationContext _ui;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _notifyItem;
    private readonly ToolStripMenuItem _startupItem;

    private AppSettings _settings = new();
    private bool _settingsFileUsable;
    private DeviceNameMask _mask = new("*");
    private EndpointVolumeGuard? _guard;
    private bool _exiting;

    /// <param name="maskOverride">Device mask from the command line; overrides the settings file.</param>
    public TrayApplicationContext(string? maskOverride)
    {
        _maskOverride = maskOverride;
        _log = new FileLog(FileLog.DefaultPath);

        _statusItem = new ToolStripMenuItem { Enabled = false };
        _enabledItem = new ToolStripMenuItem("Guard enabled", null, (_, _) => ToggleGuard());
        _notifyItem = new ToolStripMenuItem("Notify when a jump is reverted", null, (_, _) => ToggleNotify());
        _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup());

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(
        [
            _statusItem,
            new ToolStripSeparator(),
            _enabledItem,
            _notifyItem,
            _startupItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Open log", null, (_, _) => OpenFile(_log.Path)),
            new ToolStripMenuItem("Open settings", null, (_, _) => OpenFile(AppSettings.DefaultPath)),
            new ToolStripMenuItem("Reload settings", null, (_, _) => StartGuard()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()),
        ]);
        menu.Opening += (_, _) => UpdateMenu();

        // Creating the menu installs the WinForms synchronization context on this thread.
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _trayIcon = new NotifyIcon { ContextMenuStrip = menu };
        StartGuard();
        _trayIcon.Visible = true;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    protected override void ExitThreadCore()
    {
        _exiting = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        StopGuard();
        _trayIcon.Visible = false;
        _trayIcon.Icon?.Dispose();
        _trayIcon.Dispose();
        base.ExitThreadCore();
    }

    /// <summary>(Re)reads the settings and remembered volumes and starts a fresh guard.</summary>
    private void StartGuard()
    {
        StopGuard();

        (_settings, string? settingsProblem) = AppSettings.Load(AppSettings.DefaultPath);
        _settingsFileUsable = settingsProblem is null;
        if (settingsProblem is not null)
        {
            _log.Write(settingsProblem);
        }

        (VolumeMemory memory, string? memoryProblem) = VolumeMemory.Load(VolumeMemory.DefaultPath);
        if (memoryProblem is not null)
        {
            _log.Write(memoryProblem);
        }

        _mask = new DeviceNameMask(string.IsNullOrWhiteSpace(_maskOverride) ? _settings.DeviceMask : _maskOverride);
        _guard = new EndpointVolumeGuard(_mask, _settings.ToPolicy(), memory, _log) { Enabled = _settings.GuardEnabled };
        _guard.StatusChanged += () => _ui.Post(_ => UpdateTooltip(), null);
        _guard.Reverted += info => _ui.Post(_ => OnReverted(info), null);
        _guard.Start();

        UpdateIcon();
        UpdateTooltip();
    }

    private void StopGuard()
    {
        _guard?.Dispose();
        _guard = null;
    }

    private void ToggleGuard()
    {
        bool enabled = !_settings.GuardEnabled;
        SaveSettings(_settings with { GuardEnabled = enabled });
        _guard!.Enabled = enabled;
        UpdateIcon();
        UpdateTooltip();
    }

    private void ToggleNotify() => SaveSettings(_settings with { NotifyOnRevert = !_settings.NotifyOnRevert });

    private void ToggleStartup()
    {
        bool enable = !StartupRegistration.IsEnabled();
        try
        {
            StartupRegistration.Set(enable);
            _log.Write(enable ? $"Start with Windows: on ({Environment.ProcessPath})." : "Start with Windows: off.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            _log.Write($"Cannot change Start with Windows: {ex.Message}");
        }
    }

    /// <summary>Applies the settings now and writes them to the file, unless the file could not be read.</summary>
    private void SaveSettings(AppSettings settings)
    {
        _settings = settings;
        if (!_settingsFileUsable)
        {
            _log.Write($"Settings not saved: fix or delete {AppSettings.DefaultPath} first.");
            return;
        }

        try
        {
            settings.Save(AppSettings.DefaultPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write($"Cannot save settings: {ex.Message}");
        }
    }

    private void OnReverted(RevertInfo info)
    {
        if (!_exiting && _settings.NotifyOnRevert)
        {
            _trayIcon.ShowBalloonTip(
                3000, "BtVolumeGuard", $"{info.EndpointName}: {info.From}% → {info.To}%", ToolTipIcon.Info);
        }
    }

    private void UpdateMenu()
    {
        _statusItem.Text = DescribeEndpoints(" · ");
        _enabledItem.Checked = _settings.GuardEnabled;
        _notifyItem.Checked = _settings.NotifyOnRevert;
        _startupItem.Checked = StartupRegistration.IsEnabled();
    }

    private void UpdateTooltip()
    {
        if (_exiting)
        {
            return;
        }

        string text = $"BtVolumeGuard: {(_settings.GuardEnabled ? "on" : "off")}\n{DescribeEndpoints("\n")}";
        _trayIcon.Text = text.Length <= MaxTooltipLength ? text : text[..(MaxTooltipLength - 1)] + "…";
    }

    private string DescribeEndpoints(string separator)
    {
        IReadOnlyList<EndpointStatus> endpoints = _guard?.Status ?? [];
        return endpoints.Count == 0
            ? $"No active device matches {_mask}"
            : string.Join(separator, endpoints.Select(e => $"{e.Name}: {e.Volume}%{(e.Muted ? " (muted)" : "")}"));
    }

    // Taskbar theme or DPI may have changed: redraw the icon to match.
    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.Icon)
        {
            UpdateIcon();
        }
    }

    private void UpdateIcon()
    {
        Icon? previous = _trayIcon.Icon;
        _trayIcon.Icon = TrayIconRenderer.Create(
            SystemInformation.SmallIconSize.Width, TrayIconRenderer.IsTaskbarLight(), _settings.GuardEnabled);
        previous?.Dispose();
    }

    private void OpenFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            // No application is associated with the file type (e.g. .json).
            Process.Start("notepad.exe", $"\"{path}\"");
        }
    }
}
