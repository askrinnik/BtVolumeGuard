using System.Text.Json;
using System.Text.Json.Serialization;

using BtVolumeGuard.Core.Logging;

namespace BtVolumeGuard.Core.Settings;

/// <summary>
/// User-editable settings, stored as JSON next to the executable. Missing properties keep
/// their defaults.
/// </summary>
public sealed record AppSettings
{
    public const string FileName = "BtVolumeGuard.settings.json";

    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Bluetooth playback devices (friendly names) that are not guarded. All other Bluetooth
    /// playback devices are. Changed from the tray menu.
    /// </summary>
    public IReadOnlyList<string> ExcludedDevices { get; init; } = [];

    /// <summary>Event contexts of Windows switching a Bluetooth headset between A2DP and HFP.</summary>
    public IReadOnlyList<Guid> ProfileSwitchContexts { get; init; } = [VolumeChangeContexts.DefaultProfileSwitch];

    /// <summary>Event contexts of volume reported by the headset through the Bluetooth driver.</summary>
    public IReadOnlyList<Guid> HeadsetDriverContexts { get; init; } = [VolumeChangeContexts.DefaultHeadsetDriver];

    /// <summary>Event contexts of deliberate user changes besides the empty GUID (keyboard keys).</summary>
    public IReadOnlyList<Guid> UserContexts { get; init; } = [VolumeChangeContexts.DefaultTaskbarSlider];

    /// <summary>Volume (%) at or above which a value counts as "maximum".</summary>
    public int MaxThreshold { get; init; } = 95;

    /// <summary>Minimum single-step increase (%) treated as a jump when the source is not recognised.</summary>
    public int MinJump { get; init; } = 20;

    /// <summary>Volume (%) set when an endpoint appears at maximum and no earlier volume is known.</summary>
    public int DefaultVolume { get; init; } = 20;

    /// <summary>How long after a profile switch the headset's push to maximum is reverted.</summary>
    public int ProfileSwitchWindowMs { get; init; } = 2000;

    /// <summary>Whether jumps are reverted. Toggled from the tray menu.</summary>
    public bool GuardEnabled { get; init; } = true;

    /// <summary>Whether a notification is shown when a jump is reverted. Toggled from the tray menu.</summary>
    public bool NotifyOnRevert { get; init; }

    /// <summary><c>Off</c>, <c>Normal</c> or <c>Verbose</c> (every volume change). Chosen in the tray menu.</summary>
    public LogLevel LogLevel { get; init; } = LogLevel.Normal;

    public static string DefaultPath { get; } = Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>Builds the decision policy; throws <see cref="ArgumentException"/> for invalid values.</summary>
    public VolumeJumpPolicy ToPolicy() =>
        new(MaxThreshold, MinJump, DefaultVolume, TimeSpan.FromMilliseconds(ProfileSwitchWindowMs));

    /// <param name="testMask">From the command line: guard endpoints of any kind whose name matches.</param>
    public DeviceSelection ToSelection(DeviceNameMask? testMask = null) => new(ExcludedDevices, testMask);

    public VolumeChangeContexts ToContexts() => new(ProfileSwitchContexts, HeadsetDriverContexts, UserContexts);

    /// <summary>Throws <see cref="ArgumentException"/> if any value is invalid.</summary>
    public void Validate()
    {
        _ = ToPolicy();
        if (ExcludedDevices is null || ProfileSwitchContexts is null || HeadsetDriverContexts is null || UserContexts is null)
        {
            throw new ArgumentException("Lists must not be null; use [] for an empty list.");
        }
    }

    // Lists compare by content, so that settings read from a file equal the same values in code.
    public bool Equals(AppSettings? other) =>
        other is not null
        && ExcludedDevices.SequenceEqual(other.ExcludedDevices)
        && ProfileSwitchContexts.SequenceEqual(other.ProfileSwitchContexts)
        && HeadsetDriverContexts.SequenceEqual(other.HeadsetDriverContexts)
        && UserContexts.SequenceEqual(other.UserContexts)
        && (MaxThreshold, MinJump, DefaultVolume, ProfileSwitchWindowMs, GuardEnabled, NotifyOnRevert, LogLevel)
            == (other.MaxThreshold, other.MinJump, other.DefaultVolume, other.ProfileSwitchWindowMs,
                other.GuardEnabled, other.NotifyOnRevert, other.LogLevel);

    public override int GetHashCode() =>
        HashCode.Combine(ExcludedDevices.Count, MaxThreshold, MinJump, DefaultVolume, ProfileSwitchWindowMs, GuardEnabled, NotifyOnRevert, LogLevel);

    /// <summary>
    /// Loads settings from <paramref name="path"/>. A missing file is created with defaults so it
    /// can be edited; an unreadable or invalid file is left untouched and defaults are used.
    /// </summary>
    public static (AppSettings Settings, string? Problem) Load(string path)
    {
        if (!File.Exists(path))
        {
            var defaults = new AppSettings();
            try
            {
                defaults.Save(path);
                return (defaults, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (defaults, $"Cannot create {path}: {ex.Message}. Using defaults.");
            }
        }

        try
        {
            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), s_json)
                ?? throw new JsonException("The file is empty.");
            settings.Validate();
            return (settings, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return (new AppSettings(), $"Cannot use {path}: {ex.Message} Using defaults.");
        }
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, s_json));
}
