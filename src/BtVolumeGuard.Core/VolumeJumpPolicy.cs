namespace BtVolumeGuard.Core;

/// <summary>
/// Thresholds and stateless decisions about jumps to maximum volume. Pure logic, no Windows API
/// calls; the single-step rule is ported from the PowerShell prototype (prototype/LK12-VolumeGuard.ps1).
/// </summary>
public sealed class VolumeJumpPolicy
{
    public VolumeJumpPolicy(
        int maxThreshold = 95,
        int minJump = 20,
        int defaultVolume = 20,
        TimeSpan? profileSwitchWindow = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxThreshold, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxThreshold, 100);
        ArgumentOutOfRangeException.ThrowIfLessThan(minJump, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(defaultVolume, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(defaultVolume, maxThreshold);

        TimeSpan window = profileSwitchWindow ?? TimeSpan.FromSeconds(2);
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);

        MaxThreshold = maxThreshold;
        MinJump = minJump;
        DefaultVolume = defaultVolume;
        ProfileSwitchWindow = window;
    }

    /// <summary>Volume (%) at or above which a value counts as "maximum".</summary>
    public int MaxThreshold { get; }

    /// <summary>Minimum single-step increase (%) treated as a jump rather than a manual change.</summary>
    public int MinJump { get; }

    /// <summary>Volume (%) used when an endpoint appears at maximum and no earlier volume is known.</summary>
    public int DefaultVolume { get; }

    /// <summary>
    /// How long after a profile switch a headset-reported rise to maximum counts as the headset's
    /// HFP volume sync, whatever the step size.
    /// </summary>
    public TimeSpan ProfileSwitchWindow { get; }

    public bool IsMaximum(int volume) => volume >= MaxThreshold;

    /// <summary>
    /// Volume change on an endpoint that is already being watched.
    /// Returns the volume to restore, or <c>null</c> if the change should be accepted.
    /// </summary>
    public int? OnVolumeChanged(int previous, int current) =>
        RisesToMaximum(previous, current) && current - previous >= MinJump
            ? previous
            : null;

    /// <summary>
    /// Endpoint became active (e.g. a separate Hands-Free endpoint at the start of a call).
    /// Returns the volume to set, or <c>null</c> if the current volume should be kept.
    /// </summary>
    public int? OnEndpointAppeared(int current, int? remembered) =>
        IsMaximum(current)
            ? remembered is { } r && !IsMaximum(r) ? r : DefaultVolume
            : null;

    public bool RisesToMaximum(int previous, int current) => IsMaximum(current) && !IsMaximum(previous);
}
