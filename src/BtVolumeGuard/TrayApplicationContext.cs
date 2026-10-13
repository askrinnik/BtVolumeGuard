using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

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
    private readonly ToolStripMenuItem _devicesItem;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _notifyItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem[] _logLevelItems;
    private readonly ToolStripMenuItem _openLogItem;
    private readonly ToolStripMenuItem _clearLogItem;

    private AppSettings _settings = new();
    private bool _settingsFileUsable;
    private DeviceNameMask? _testMask;
    private EndpointVolumeGuard? _guard;
    private AboutForm? _aboutForm;
    private bool _exiting;

    /// <param name="maskOverride">Device mask from the command line; overrides the settings file.</param>
    public TrayApplicationContext(string? maskOverride)
    {
        _maskOverride = maskOverride;
        _log = new FileLog(FileLog.DefaultPath);

        _statusItem = new ToolStripMenuItem { Enabled = false };
        _devicesItem = new ToolStripMenuItem("Devices");
        _devicesItem.DropDownItems.Add(new ToolStripMenuItem("…") { Enabled = false });
        _devicesItem.DropDownOpening += (_, _) => UpdateDevicesMenu();
        _enabledItem = new ToolStripMenuItem("Guard enabled", null, (_, _) => ToggleGuard());
        _notifyItem = new ToolStripMenuItem("Notify when a jump is reverted", null, (_, _) => ToggleNotify());
        _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup());
        _logLevelItems =
        [
            .. Enum.GetValues<LogLevel>().Select(level =>
                new ToolStripMenuItem(DescribeLogLevel(level), null, (_, _) => SetLogLevel(level)) { Tag = level }),
        ];
        _openLogItem = new ToolStripMenuItem("Open log", null, (_, _) => OpenFile(_log.Path));
        _clearLogItem = new ToolStripMenuItem("Clear log", null, (_, _) => _log.Clear());
        var logItem = new ToolStripMenuItem("Log");
        logItem.DropDownItems.AddRange([.. _logLevelItems, new ToolStripSeparator(), _openLogItem, _clearLogItem]);
        logItem.DropDownOpening += (_, _) => UpdateLogMenu();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(
        [
            _statusItem,
            new ToolStripSeparator(),
            _devicesItem,
            _enabledItem,
            _notifyItem,
            _startupItem,
            new ToolStripSeparator(),
            logItem,
            new ToolStripMenuItem("Open settings", null, (_, _) => OpenFile(AppSettings.DefaultPath)),
            new ToolStripMenuItem("Reload settings", null, (_, _) => StartGuard()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("About BtVolumeGuard…", null, (_, _) => ShowAbout()),
            new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()),
        ]);
        menu.Opening += (_, _) => UpdateMenu();

        // Creating the menu installs the WinForms synchronization context on this thread.
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _trayIcon = new NotifyIcon { ContextMenuStrip = menu };
        _trayIcon.MouseUp += OnTrayIconMouseUp;
        StartGuard();
        _trayIcon.Visible = true;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    protected override void ExitThreadCore()
    {
        _exiting = true;
        _aboutForm?.Close();
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
        _log.Level = _settings.LogLevel;
        if (settingsProblem is not null)
        {
            _log.Write(settingsProblem);
        }

        (VolumeMemory memory, string? memoryProblem) = VolumeMemory.Load(VolumeMemory.DefaultPath);
        if (memoryProblem is not null)
        {
            _log.Write(memoryProblem);
        }

        _testMask = string.IsNullOrWhiteSpace(_maskOverride) ? null : new DeviceNameMask(_maskOverride);
        _guard = new EndpointVolumeGuard(
            _settings.ToSelection(_testMask), _settings.ToPolicy(), _settings.ToContexts(), memory, _log)
        {
            Enabled = _settings.GuardEnabled,
        };
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

    private void SetLogLevel(LogLevel level)
    {
        // Write the line at whichever of the two levels is not Off, so the switch itself is recorded.
        if (level == LogLevel.Off)
        {
            _log.Write("Log level: Off.");
        }

        _log.Level = level;
        if (level != LogLevel.Off)
        {
            _log.Write($"Log level: {level}.");
        }

        SaveSettings(_settings with { LogLevel = level });
    }

    private static string DescribeLogLevel(LogLevel level) => level switch
    {
        LogLevel.Off => "Off",
        LogLevel.Normal => "Normal (reverted jumps, errors)",
        _ => "Verbose (every volume change)",
    };

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

    // Every Bluetooth playback device Windows knows, connected or not; disconnected ones stay
    // clickable and are marked "(offline)".
    private void UpdateDevicesMenu()
    {
        _devicesItem.DropDownItems.Clear();
        IReadOnlyList<KnownDevice> devices = _guard?.Devices ?? [];
        if (devices.Count == 0)
        {
            string empty = _testMask is { } mask ? $"No device matches {mask}" : "No Bluetooth playback devices";
            _devicesItem.DropDownItems.Add(new ToolStripMenuItem(empty) { Enabled = false });
            return;
        }

        foreach (KnownDevice device in devices)
        {
            _devicesItem.DropDownItems.Add(
                new ToolStripMenuItem(device.MenuText, null, (_, _) => ToggleDevice(device)) { Checked = device.Guarded });
        }
    }

    private void ToggleDevice(KnownDevice device)
    {
        if (_guard is null)
        {
            return;
        }

        DeviceSelection selection = _guard.Selection.WithGuarded(device.Name, !device.Guarded);
        SaveSettings(_settings with { ExcludedDevices = [.. selection.ExcludedNames.Order(StringComparer.OrdinalIgnoreCase)] });
        _guard.SetSelection(selection);
    }

    // Modeless, one window at a time: a second click brings the open one to the front.
    private void ShowAbout()
    {
        if (_aboutForm is null)
        {
            _aboutForm = new AboutForm();
            _aboutForm.FormClosed += (sender, _) =>
            {
                ((Form)sender!).Dispose();
                _aboutForm = null;
            };
            _aboutForm.Show();
        }

        _aboutForm.Activate();
    }

    private void UpdateLogMenu()
    {
        foreach (ToolStripMenuItem item in _logLevelItems)
        {
            item.Checked = (LogLevel)item.Tag! == _settings.LogLevel;
        }

        bool hasLog = File.Exists(_log.Path) && new FileInfo(_log.Path).Length > 0;
        _openLogItem.Enabled = hasLog;
        _clearLogItem.Enabled = hasLog;
    }

    private void UpdateTooltip()
    {
        if (_exiting)
        {
            return;
        }

        string text = $"BtVolumeGuard {AboutForm.Current.Version}: {(_settings.GuardEnabled ? "on" : "off")}\n{DescribeEndpoints("\n")}";
        _trayIcon.Text = text.Length <= MaxTooltipLength ? text : text[..(MaxTooltipLength - 1)] + "…";
    }

    private string DescribeEndpoints(string separator)
    {
        IReadOnlyList<EndpointStatus> endpoints = _guard?.Status ?? [];
        return endpoints.Count == 0
            ? _testMask is { } mask ? $"No active device matches {mask}" : "No guarded device connected"
            : string.Join(separator, endpoints.Select(e => $"{e.Name}: {e.Volume}%{(e.Muted ? " (muted)" : "")}"));
    }

    // A left click opens the same menu as a right click. NotifyIcon's own ShowContextMenu positions
    // the menu and makes it close when the user clicks elsewhere; it is not public, hence reflection.
    private void OnTrayIconMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        typeof(NotifyIcon)
            .GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)?
            .Invoke(_trayIcon, null);
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
