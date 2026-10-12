using BtVolumeGuard.Core;

namespace BtVolumeGuard.Core.Tests;

public class VolumeJumpPolicyTests
{
    private readonly VolumeJumpPolicy _policy = new(maxThreshold: 95, minJump: 20, defaultVolume: 20);

    [Theory]
    [InlineData(20, 100)]
    [InlineData(4, 95)]
    [InlineData(75, 100)]
    public void Jump_to_maximum_is_reverted_to_previous(int previous, int current) =>
        Assert.Equal(previous, _policy.OnVolumeChanged(previous, current));

    [Theory]
    [InlineData(20, 26)]   // headset button step
    [InlineData(90, 100)]  // gradual change near the top
    [InlineData(100, 100)] // already at maximum
    [InlineData(80, 20)]   // decrease
    [InlineData(20, 90)]   // big jump, but below the threshold
    public void Other_changes_are_accepted(int previous, int current) =>
        Assert.Null(_policy.OnVolumeChanged(previous, current));

    [Fact]
    public void Endpoint_appearing_at_maximum_gets_remembered_volume() =>
        Assert.Equal(35, _policy.OnEndpointAppeared(current: 100, remembered: 35));

    [Fact]
    public void Endpoint_appearing_at_maximum_without_memory_gets_default() =>
        Assert.Equal(20, _policy.OnEndpointAppeared(current: 100, remembered: null));

    [Fact]
    public void Remembered_maximum_is_not_restored() =>
        Assert.Equal(20, _policy.OnEndpointAppeared(current: 100, remembered: 100));

    [Fact]
    public void Endpoint_appearing_below_maximum_is_kept() =>
        Assert.Null(_policy.OnEndpointAppeared(current: 30, remembered: 20));
}
