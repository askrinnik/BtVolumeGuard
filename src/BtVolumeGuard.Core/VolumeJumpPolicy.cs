namespace BtVolumeGuard.Core;

/// <summary>
/// Decides whether an observed volume change is a headset-induced jump to maximum
/// that must be reverted. Pure logic, no Windows API calls; ported from the
/// PowerShell prototype (prototype/LK12-VolumeGuard.ps1).
/// </summary>
public sealed class VolumeJumpPolicy
{
    public VolumeJumpPolicy(int maxThreshold = 95, int minJump = 20, int defaultVolume = 20)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxThreshold, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxThreshold, 100);
        ArgumentOutOfRangeException.ThrowIfLessThan(minJump, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(defaultVolume, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(defaultVolume, maxThreshold);

        MaxThreshold = maxThreshold;
        MinJump = minJump;
        DefaultVolume = defaultVolume;
    }

    /// <summary>Volume (%) at or above which a value counts as "maximum".</summary>
    public int MaxThreshold { get; }

    /// <summary>Minimum single-step increase (%) treated as a jump rather than a manual change.</summary>
    public int MinJump { get; }

    /// <summary>Volume (%) used when an endpoint appears at maximum and no earlier volume is known.</summary>
    public int DefaultVolume { get; }

    /// <summary>
    /// Volume change on an endpoint that is already being watched.
    /// Returns the volume to restore, or <c>null</c> if the change should be accepted.
    /// </summary>
    public int? OnVolumeChanged(int previous, int current) =>
        current >= MaxThreshold && previous < MaxThreshold && current - previous >= MinJump
            ? previous
            : null;

    /// <summary>
    /// Endpoint became active (e.g. the Hands-Free endpoint at the start of a call).
    /// Returns the volume to set, or <c>null</c> if the current volume should be kept.
    /// </summary>
    public int? OnEndpointAppeared(int current, int? remembered) =>
        current >= MaxThreshold
            ? remembered is { } r && r < MaxThreshold ? r : DefaultVolume
            : null;
}
