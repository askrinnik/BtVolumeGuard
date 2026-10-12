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
/// Maps Core Audio event-context GUIDs to <see cref="VolumeChangeSource"/>.
/// The values were observed on Windows 11 with the LK12 headset (see docs/design.md).
/// </summary>
public static class VolumeChangeContexts
{
    /// <summary>Event context BtVolumeGuard passes with its own volume changes.</summary>
    public static readonly Guid Own = new("7d3f8a52-6c1e-4b8e-9a0d-2f5b1c9e4a17");

    public static readonly Guid ProfileSwitch = new("03016827-7b05-566a-ada3-14d84347d041");

    public static readonly Guid HeadsetDriver = new("0de6c1af-c506-4e60-bd8c-f3b00097de2a");

    /// <summary>Volume slider in the taskbar flyout.</summary>
    public static readonly Guid TaskbarSlider = new("355c8163-187b-45d8-9882-61afb3537f61");

    public static VolumeChangeSource Classify(Guid context)
    {
        if (context == Own)
        {
            return VolumeChangeSource.Own;
        }

        if (context == ProfileSwitch)
        {
            return VolumeChangeSource.ProfileSwitch;
        }

        if (context == HeadsetDriver)
        {
            return VolumeChangeSource.HeadsetDriver;
        }

        // Keyboard keys and most apps pass an empty context.
        return context == Guid.Empty || context == TaskbarSlider
            ? VolumeChangeSource.User
            : VolumeChangeSource.Unknown;
    }
}
