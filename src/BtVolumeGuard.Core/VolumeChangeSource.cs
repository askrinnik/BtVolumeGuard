namespace BtVolumeGuard.Core;

/// <summary>Who changed the endpoint volume, as far as the event context tells.</summary>
public enum VolumeChangeSource
{
    /// <summary>Unrecognised context, or a change found by polling (no context).</summary>
    Unknown,

    /// <summary>A deliberate change by the user: keyboard keys, Windows sliders.</summary>
    User,

    /// <summary>Windows switched the Bluetooth endpoint between profiles (A2DP ↔ HFP).</summary>
    ProfileSwitch,

    /// <summary>Volume reported by the headset through the Bluetooth driver (buttons, HFP volume sync).</summary>
    HeadsetDriver,

    /// <summary>A change made by BtVolumeGuard itself.</summary>
    Own,
}

/// <summary>
/// Maps Core Audio event-context GUIDs to <see cref="VolumeChangeSource"/>. The defaults were
/// observed on Windows 11 with the LK12 headset (see docs/design.md); the lists come from the
/// settings so another machine can add its own values without a rebuild.
/// </summary>
public sealed class VolumeChangeContexts
{
    /// <summary>Event context BtVolumeGuard passes with its own volume changes.</summary>
    public static readonly Guid Own = new("7d3f8a52-6c1e-4b8e-9a0d-2f5b1c9e4a17");

    public static readonly Guid DefaultProfileSwitch = new("03016827-7b05-566a-ada3-14d84347d041");

    public static readonly Guid DefaultHeadsetDriver = new("0de6c1af-c506-4e60-bd8c-f3b00097de2a");

    /// <summary>Volume slider in the taskbar flyout.</summary>
    public static readonly Guid DefaultTaskbarSlider = new("355c8163-187b-45d8-9882-61afb3537f61");

    private readonly HashSet<Guid> _profileSwitch;
    private readonly HashSet<Guid> _headsetDriver;
    private readonly HashSet<Guid> _user;

    public VolumeChangeContexts(
        IEnumerable<Guid>? profileSwitch = null, IEnumerable<Guid>? headsetDriver = null, IEnumerable<Guid>? user = null)
    {
        _profileSwitch = [.. profileSwitch ?? [DefaultProfileSwitch]];
        _headsetDriver = [.. headsetDriver ?? [DefaultHeadsetDriver]];
        _user = [.. user ?? [DefaultTaskbarSlider]];
    }

    public VolumeChangeSource Classify(Guid context)
    {
        if (context == Own)
        {
            return VolumeChangeSource.Own;
        }

        if (_profileSwitch.Contains(context))
        {
            return VolumeChangeSource.ProfileSwitch;
        }

        if (_headsetDriver.Contains(context))
        {
            return VolumeChangeSource.HeadsetDriver;
        }

        // Keyboard keys and most apps pass an empty context.
        return context == Guid.Empty || _user.Contains(context)
            ? VolumeChangeSource.User
            : VolumeChangeSource.Unknown;
    }
}
