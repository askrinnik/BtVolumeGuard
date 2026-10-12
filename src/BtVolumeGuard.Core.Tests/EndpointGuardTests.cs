using BtVolumeGuard.Core;

using static BtVolumeGuard.Core.VolumeChangeSource;

namespace BtVolumeGuard.Core.Tests;

public class EndpointGuardTests
{
    private static readonly DateTimeOffset s_t0 = new(2026, 10, 12, 20, 0, 0, TimeSpan.Zero);

    private readonly VolumeJumpPolicy _policy = new(
        maxThreshold: 95, minJump: 20, defaultVolume: 20, profileSwitchWindow: TimeSpan.FromSeconds(2));

    private static DateTimeOffset At(double seconds) => s_t0.AddSeconds(seconds);

    // Sequences below are taken from real Teams calls (docs/design.md).

    [Fact]
    public void Headset_maximum_after_profile_switch_is_reverted_to_call_volume()
    {
        var guard = new EndpointGuard(_policy, currentVolume: 27);

        Assert.Equal(GuardAction.Accept, guard.OnChange(ProfileSwitch, 58, At(0)).Action);
        GuardDecision decision = guard.OnChange(HeadsetDriver, 100, At(0.5));

        Assert.Equal(GuardAction.Revert, decision.Action);
        Assert.Equal(58, decision.Target);
        Assert.Equal(58, guard.Volume);
    }

    [Fact]
    public void Small_headset_step_to_maximum_right_after_profile_switch_is_reverted()
    {
        var guard = new EndpointGuard(_policy, currentVolume: 27);

        guard.OnChange(ProfileSwitch, 88, At(0));
        GuardDecision decision = guard.OnChange(HeadsetDriver, 100, At(0.6));

        Assert.Equal(GuardAction.Revert, decision.Action);
        Assert.Equal(88, decision.Target);
    }

    [Fact]
    public void Profile_switch_straight_to_maximum_is_reverted_to_previous_volume()
    {
        var guard = new EndpointGuard(_policy, currentVolume: 25);

        GuardDecision decision = guard.OnChange(ProfileSwitch, 100, At(0));

        Assert.Equal(GuardAction.Revert, decision.Action);
        Assert.Equal(25, decision.Target);
    }

    [Fact]
    public void Headset_maximum_following_a_reverted_profile_switch_is_reverted_too()
    {
        var guard = new EndpointGuard(_policy, currentVolume: 25);

        guard.OnChange(ProfileSwitch, 100, At(0));
        guard.OnChange(Own, 25, At(0.1));
        GuardDecision decision = guard.OnChange(HeadsetDriver, 100, At(0.6));

        Assert.Equal(GuardAction.Revert, decision.Action);
        Assert.Equal(25, decision.Target);
    }

    [Fact]
    public void Headset_step_to_maximum_long_after_profile_switch_is_accepted()
    {
        var guard = new EndpointGuard(_policy, currentVolume: 27);

        guard.OnChange(ProfileSwitch, 88, At(0));
        GuardDecision decision = guard.OnChange(HeadsetDriver, 100, At(10));

        Assert.Equal(GuardAction.Accept, decision.Action);
        Assert.Equal(100, guard.Volume);
    }

    [Fact]
    public void Headset_buttons_without_profile_switch_are_accepted()
    {
        var guard = new EndpointGuard(_policy, currentVolume: 88);

        Assert.Equal(GuardAction.Accept, guard.OnChange(HeadsetDriver, 94, At(0)).Action);
        Assert.Equal(GuardAction.Accept, guard.OnChange(HeadsetDriver, 100, At(1)).Action);
    }

    [Fact]
    public void Large_headset_jump_to_maximum_is_reverted_even_without_profile_switch()
    {
        var guard = new EndpointGuard(_policy, currentVolume: 30);

        GuardDecision decision = guard.OnChange(HeadsetDriver, 100, At(0));

        Assert.Equal(GuardAction.Revert, decision.Action);
        Assert.Equal(30, decision.Target);
    }

    [Theory]
    [InlineData(20, 100)]
    [InlineData(58, 100)]
    public void User_may_set_maximum(int previous, int current)
    {
        var guard = new EndpointGuard(_policy, previous);
        guard.OnChange(ProfileSwitch, previous, At(0));

        Assert.Equal(GuardAction.Accept, guard.OnChange(User, current, At(0.5)).Action);
        Assert.Equal(current, guard.Volume);
    }

    [Theory]
    [InlineData(20, 100, GuardAction.Revert)]
    [InlineData(90, 100, GuardAction.Accept)]
    [InlineData(80, 20, GuardAction.Accept)]
    public void Unknown_source_falls_back_to_single_step_rule(int previous, int current, GuardAction expected)
    {
        var guard = new EndpointGuard(_policy, previous);

        Assert.Equal(expected, guard.OnChange(Unknown, current, At(0)).Action);
    }

    [Fact]
    public void Own_change_is_ignored_but_tracked()
    {
        var guard = new EndpointGuard(_policy, currentVolume: 50);

        Assert.Equal(GuardAction.Ignore, guard.OnChange(Own, 30, At(0)).Action);
        Assert.Equal(30, guard.Volume);
    }

    [Fact]
    public void Unchanged_percent_is_ignored()
    {
        var guard = new EndpointGuard(_policy, currentVolume: 26);

        Assert.Equal(GuardAction.Ignore, guard.OnChange(HeadsetDriver, 26, At(0)).Action);
    }

    [Fact]
    public void Remembered_tracks_last_accepted_volume_below_maximum()
    {
        var guard = new EndpointGuard(_policy, currentVolume: 30);

        guard.OnChange(User, 40, At(0));
        guard.OnChange(User, 100, At(1));

        Assert.Equal(40, guard.Remembered);
    }

    [Fact]
    public void Nothing_is_remembered_when_starting_at_maximum() =>
        Assert.Null(new EndpointGuard(_policy, currentVolume: 100).Remembered);

    [Fact]
    public void Endpoint_appearing_at_maximum_gets_remembered_volume()
    {
        var guard = new EndpointGuard(_policy, currentVolume: 100);

        Assert.Equal(35, guard.OnAppeared(rememberedFromEarlier: 35));
        Assert.Equal(35, guard.Volume);
    }

    [Fact]
    public void Endpoint_appearing_below_maximum_is_kept() =>
        Assert.Null(new EndpointGuard(_policy, currentVolume: 30).OnAppeared(rememberedFromEarlier: 20));
}
