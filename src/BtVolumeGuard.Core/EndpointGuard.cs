namespace BtVolumeGuard.Core;

public enum GuardAction
{
    /// <summary>The change is kept and becomes the endpoint's known volume.</summary>
    Accept,

    /// <summary>The change must be undone: set <see cref="GuardDecision.Target"/>.</summary>
    Revert,

    /// <summary>Nothing to do (own change, or no change in percent).</summary>
    Ignore,
}

public readonly record struct GuardDecision(GuardAction Action, int? Target, string Reason)
{
    public static GuardDecision Accept(string reason) => new(GuardAction.Accept, null, reason);

    public static GuardDecision Revert(int target, string reason) => new(GuardAction.Revert, target, reason);

    public static GuardDecision Ignore(string reason) => new(GuardAction.Ignore, null, reason);
}

/// <summary>
/// Per-endpoint guard state: the last known volume, the time of the last profile switch and the
/// last accepted volume below maximum. Decides what to do with each observed change.
/// Pure logic; time is passed in by the caller.
/// </summary>
/// <remarks>
/// The jump at the start of a call comes in two steps (docs/design.md): Windows switches the
/// endpoint to HFP and sets the remembered call volume (<see cref="VolumeChangeSource.ProfileSwitch"/>),
/// then the headset reports maximum through the driver (<see cref="VolumeChangeSource.HeadsetDriver"/>).
/// The second step is reverted to the volume set by the first.
/// </remarks>
public sealed class EndpointGuard
{
    private readonly VolumeJumpPolicy _policy;
    private DateTimeOffset? _lastProfileSwitch;

    public EndpointGuard(VolumeJumpPolicy policy, int currentVolume)
    {
        _policy = policy;
        Volume = currentVolume;
        Remembered = policy.IsMaximum(currentVolume) ? null : currentVolume;
    }

    /// <summary>Last known volume (%): the last accepted change or the last volume the guard set.</summary>
    public int Volume { get; private set; }

    /// <summary>Last volume (%) below maximum that was accepted, if any.</summary>
    public int? Remembered { get; private set; }

    public GuardDecision OnChange(VolumeChangeSource source, int current, DateTimeOffset at)
    {
        if (source == VolumeChangeSource.ProfileSwitch)
        {
            _lastProfileSwitch = at;
        }

        if (source == VolumeChangeSource.Own)
        {
            Volume = current;
            return GuardDecision.Ignore("own change");
        }

        if (current == Volume)
        {
            return GuardDecision.Ignore("no change");
        }

        int previous = Volume;
        int? target = source switch
        {
            VolumeChangeSource.User => null,
            VolumeChangeSource.HeadsetDriver when IsSoonAfterProfileSwitch(at)
                => _policy.RisesToMaximum(previous, current) ? previous : null,
            _ => _policy.OnVolumeChanged(previous, current),
        };

        if (target is { } t)
        {
            Volume = t;
            return GuardDecision.Revert(t, DescribeJump(source, at));
        }

        Volume = current;
        if (!_policy.IsMaximum(current))
        {
            Remembered = current;
        }

        return GuardDecision.Accept(source == VolumeChangeSource.User ? "user change" : "not a jump");
    }

    /// <summary>
    /// The endpoint has just appeared (not present when the app started) with the given volume.
    /// Returns the volume to set, or <c>null</c> to keep it.
    /// </summary>
    public int? OnAppeared(int? rememberedFromEarlier)
    {
        int? target = _policy.OnEndpointAppeared(Volume, rememberedFromEarlier);
        if (target is { } t)
        {
            Volume = t;
            Remembered = t;
        }

        return target;
    }

    private bool IsSoonAfterProfileSwitch(DateTimeOffset at) =>
        _lastProfileSwitch is { } switchedAt && at - switchedAt <= _policy.ProfileSwitchWindow;

    private string DescribeJump(VolumeChangeSource source, DateTimeOffset at) =>
        source == VolumeChangeSource.HeadsetDriver && IsSoonAfterProfileSwitch(at)
            ? $"headset pushed maximum {(at - _lastProfileSwitch!.Value).TotalMilliseconds:0} ms after profile switch"
            : $"single-step jump to maximum ({source})";
}
